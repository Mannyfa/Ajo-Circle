using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

public sealed class AuthService(IAppDbContext db, IPasswordHashing hasher, IContactCodeSender codeSender, IAppClock clock, AuditWriter audit)
{
    public async Task<User> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        User.ValidatePassword(req.Password);
        var user = User.Register(req.Email, req.DisplayName, req.AcceptTerms, clock.UtcNow);
        if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
            throw new DomainConflictException("account_exists", "An account with this email may already exist. Try signing in or recovering your password.");

        user.SetPasswordHash(hasher.Hash(user, req.Password));
        db.Users.Add(user);
        var (verification, code) = ContactVerification.Create(user.Id, clock.UtcNow);
        db.ContactVerifications.Add(verification);
        audit.Write(user.Id, "user.registered", "user", user.Id, null);
        await db.SaveChangesAsync(ct);

        await codeSender.SendVerificationCodeAsync(user.Email, user.DisplayName, code, ct);
        return user;
    }

    /// <summary>Returns null for any failure so callers cannot tell a wrong password from an unknown email.</summary>
    public async Task<User?> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = User.NormalizeEmail(req.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            hasher.Hash(User.Register("timing@example.invalid", "Timing", true, clock.UtcNow), req.Password ?? ""); // equalize timing
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
        await codeSender.SendVerificationCodeAsync(user.Email, user.DisplayName, code, ct);
    }

    public async Task<MeDto> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new UnauthenticatedException();
        return new MeDto(u.Id, u.Email, u.DisplayName, u.IsContactVerified, u.CreatedAt);
    }

    public async Task<MeDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest req, CancellationToken ct)
    {
        var u = await db.Users.SingleAsync(x => x.Id == userId, ct);
        u.UpdateDisplayName(req.DisplayName);
        audit.Write(userId, "user.profile_updated", "user", userId, null);
        await db.SaveChangesAsync(ct);
        return new MeDto(u.Id, u.Email, u.DisplayName, u.IsContactVerified, u.CreatedAt);
    }
}

public sealed class AuditWriter(IAppDbContext db, IAppClock clock)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Write(Guid? actorId, string action, string entityType, Guid entityId, Guid? groupId, object? metadata = null) =>
        db.AuditLogs.Add(Domain.Audit.AuditLog.Create(actorId, action, entityType, entityId, groupId,
            metadata is null ? null : System.Text.Json.JsonSerializer.Serialize(metadata, Json), clock.UtcNow));
}
