using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AjoCircle.Application;

public sealed class AuthService(IAppDbContext db, IPasswordHashing hasher, IEmailSender email, EmailTemplates templates, IAppClock clock, AuditWriter audit,
    ICurrentUser currentUser,
    ILogger<AuthService> log)
{
    public async Task<User> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        User.ValidatePassword(req.Password);
        var user = User.Register(req.Email, req.DisplayName, req.AcceptTerms, req.Role, clock.UtcNow);
        if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
            throw new DomainConflictException("account_exists", "An account with this email may already exist. Try signing in or recovering your password.");

        user.SetPasswordHash(hasher.Hash(user, req.Password));
        db.Users.Add(user);
        var (verification, code) = ContactVerification.Create(user.Id, clock.UtcNow);
        db.ContactVerifications.Add(verification);
        audit.Write(user.Id, "user.registered", "user", user.Id, null, new { role = user.Role.ToString() });
        await db.SaveChangesAsync(ct);

        await email.SendAsync(templates.VerificationCode(user.Email, user.DisplayName, code), ct);
        return user;
    }

    /// <summary>Returns null for any failure so callers cannot tell a wrong password from an unknown email.</summary>
    public async Task<User?> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = User.NormalizeEmail(req.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            hasher.Hash(User.Register("timing@example.invalid", "Timing", true, AccountRole.Member, clock.UtcNow), req.Password ?? ""); // equalize timing
            return null;
        }
        if (user.Status != UserStatus.Active || !hasher.Verify(user, req.Password ?? "")) return null;
        return user;
    }

    public async Task VerifyContactAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.IsContactVerified) return;
        var now = clock.UtcNow;
        var pending = await db.ContactVerifications
            .Where(v => v.UserId == userId && v.ConsumedAt == null && v.ExpiresAt > now)
            .OrderByDescending(v => v.CreatedAt).FirstOrDefaultAsync(ct);
        var ok = pending is not null && pending.TryConsume(code, now);
        if (ok)
        {
            user.MarkContactVerified(now);
            audit.Write(userId, "user.contact_verified", "user", userId, null);
        }
        await db.SaveChangesAsync(ct); // persist attempt count either way
        if (!ok)
            throw new DomainException("code_invalid", "That code is incorrect or has expired. Request a new code if needed.");
    }

    public async Task ResendCodeAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (user.IsContactVerified) return;
        var (verification, code) = ContactVerification.Create(user.Id, clock.UtcNow);
        db.ContactVerifications.Add(verification);
        await db.SaveChangesAsync(ct);
        await email.SendAsync(templates.VerificationCode(user.Email, user.DisplayName, code), ct);
    }

    /// <summary>
    /// Starts account recovery. Always looks the same to the caller (no hint whether the email has an account), and sends
    /// at most <see cref="PasswordResetToken.MaxPerHour"/> links per account per hour on top of the IP rate limit.
    /// </summary>
    public async Task ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(req.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null || user.Status != UserStatus.Active) return;
        var now = clock.UtcNow;
        var recent = await db.PasswordResetTokens.CountAsync(t => t.UserId == user.Id && t.CreatedAt > now.AddHours(-1), ct);
        if (recent >= PasswordResetToken.MaxPerHour) return;

        var (token, raw) = PasswordResetToken.Create(user.Id, now);
        db.PasswordResetTokens.Add(token);
        audit.Write(user.Id, "user.password_reset_requested", "user", user.Id, null);
        await db.SaveChangesAsync(ct);
        await SendQuietlyAsync(templates.PasswordReset(user.Email, user.DisplayName, raw), ct);
    }

    /// <summary>
    /// For recovery emails a send failure must not change the response (that would reveal the account exists), and a
    /// failed "password changed" notice must not report the reset itself as failed. Failures are logged without the address.
    /// </summary>
    private async Task SendQuietlyAsync(EmailMessage message, CancellationToken ct)
    {
        try { await email.SendAsync(message, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Account email could not be sent: {Subject}", message.Subject);
        }
    }

    /// <summary>
    /// Completes recovery: sets the new password, signs out every session (new security stamp), burns every outstanding
    /// link, and — because the link proved control of the inbox — marks the email verified.
    /// </summary>
    public async Task ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = PasswordResetToken.Hash(req.Token ?? "");
        var token = await db.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || !token.IsUsable(now))
            throw new DomainException("reset_link_invalid", "This reset link is invalid or has expired. Request a new one.");
        User.ValidatePassword(req.NewPassword);

        var user = await db.Users.SingleAsync(u => u.Id == token.UserId, ct);
        user.SetPasswordHash(hasher.Hash(user, req.NewPassword)); // also rotates the security stamp → other sessions end
        user.MarkContactVerified(now);
        var outstanding = await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.ConsumedAt == null).ToListAsync(ct);
        foreach (var t in outstanding) t.Consume(now);
        audit.Write(user.Id, "user.password_reset", "user", user.Id, null);
        await db.SaveChangesAsync(ct);
        await SendQuietlyAsync(templates.PasswordChanged(user.Email, user.DisplayName), ct);
    }

    public async Task<MeDto> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new UnauthenticatedException();
        return ToMe(u);
    }

    /// <summary>Operations sessions need a second factor in the last 12 hours.</summary>
    public static readonly TimeSpan OperationsSessionLifetime = TimeSpan.FromHours(12);

    private MeDto ToMe(User u) => new(u.Id, u.Email, u.DisplayName, u.IsContactVerified, u.CreatedAt, u.IdentityLevel, u.IsPhoneVerified,
        u.IsOperations, u.IsMfaEnabled, u.IsOperations && u.IsMfaEnabled && currentUser.MfaVerifiedAt is { } at && clock.UtcNow - at < OperationsSessionLifetime, u.Role);

    public async Task<MeDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest req, CancellationToken ct)
    {
        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        u.UpdateDisplayName(req.DisplayName);
        audit.Write(userId, "user.profile_updated", "user", userId, null);
        await db.SaveChangesAsync(ct);
        return ToMe(u);
    }
}

public sealed class AuditWriter(IAppDbContext db, IAppClock clock)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Write(Guid? actorId, string action, string entityType, Guid entityId, Guid? groupId, object? metadata = null) =>
        db.AuditLogs.Add(Domain.Audit.AuditLog.Create(actorId, action, entityType, entityId, groupId,
            metadata is null ? null : System.Text.Json.JsonSerializer.Serialize(metadata, Json), clock.UtcNow));
}
