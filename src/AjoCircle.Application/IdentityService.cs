using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>Encrypts sensitive values at rest (TOTP secrets, ID numbers, BVN, identity documents).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>Sends a text message. The SMS vendor is an open decision (brief §16.3).</summary>
public interface ISmsSender
{
    Task SendAsync(string e164Phone, string text, CancellationToken ct);
}

public sealed class IdentityOptions
{
    public const string Section = "Identity";
    /// <summary>Days after a decision before encrypted documents, ID numbers and BVN are deleted (brief §11.2).</summary>
    public int DocumentRetentionDays { get; init; } = 30;
}

/// <summary>
/// The rule that unverified users can't act (owner decision 7). Creating or running a group needs organizer-level
/// verification; joining needs member-level. Reading, confirming, disputing and leaving stay open — they protect members.
/// </summary>
public sealed class IdentityGate(IAppDbContext db)
{
    public async Task RequireAsync(Guid userId, IdentityLevel needed, CancellationToken ct)
    {
        var u = await db.Users.Where(u => u.Id == userId).Select(u => new { u.IdentityLevel, u.Role }).SingleAsync(ct);
        if (needed == IdentityLevel.Organizer && u.Role != AccountRole.Organizer)
            throw new DomainException("organizer_role_required",
                "Only organizer accounts can create or run groups. To become an organizer, complete organizer verification from your settings.");
        if (u.IdentityLevel >= needed) return;
        throw new DomainException(needed == IdentityLevel.Organizer ? "organizer_unverified" : "identity_unverified",
            needed == IdentityLevel.Organizer
                ? "Verify your identity as an organizer (ID, BVN, phone and passport photo) before creating or running a group."
                : "Verify your phone number and ID before joining a group.");
    }
}

/// <summary>The user's side of verification: phone code, then a submission that operations reviews.</summary>
public sealed class IdentityService(IAppDbContext db, ICurrentUser currentUser, IAppClock clock, AuditWriter audit,
    ISmsSender sms, ISecretProtector protector, AppOptions app)
{
    private Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    public async Task RequestPhoneCodeAsync(PhoneRequest req, CancellationToken ct)
    {
        var me = Me;
        var phone = PhoneNumbers.Normalize(req.Phone);
        if (await db.Users.AnyAsync(u => u.Id != me && u.PhoneNumber == phone, ct))
            throw new DomainConflictException("phone_in_use", "This phone number is already used by another account.");
        var now = clock.UtcNow;
        var recent = await db.ContactVerifications.CountAsync(v => v.UserId == me && v.Channel == ContactChannel.Phone && v.CreatedAt > now.AddHours(-1), ct);
        if (recent >= 5) throw new DomainConflictException("too_many_codes", "Too many codes requested. Try again in an hour.");
        var (verification, code) = ContactVerification.Create(me, now, ContactChannel.Phone, phone);
        db.ContactVerifications.Add(verification);
        await db.SaveChangesAsync(ct);
        await sms.SendAsync(phone, $"Your {app.ProductName} code is {code}. It expires in 15 minutes. Never share it.", ct);
    }

    public async Task VerifyPhoneAsync(VerifyContactRequest req, CancellationToken ct)
    {
        var me = Me;
        var now = clock.UtcNow;
        var pending = await db.ContactVerifications
            .Where(v => v.UserId == me && v.Channel == ContactChannel.Phone && v.ConsumedAt == null && v.ExpiresAt > now)
            .OrderByDescending(v => v.CreatedAt).FirstOrDefaultAsync(ct);
        var ok = pending is not null && pending.TryConsume(req.Code, now);
        if (ok)
        {
            if (await db.Users.AnyAsync(u => u.Id != me && u.PhoneNumber == pending!.Target, ct))
                throw new DomainConflictException("phone_in_use", "This phone number is already used by another account.");
            var user = await db.Users.SingleAsync(u => u.Id == me, ct);
            user.SetVerifiedPhone(pending!.Target!, now);
            audit.Write(me, "user.phone_verified", "user", me, null);
        }
        await db.SaveChangesAsync(ct); // persist the attempt count either way
        if (!ok) throw new DomainException("code_invalid", "That code is incorrect or has expired. Request a new code if needed.");
    }

    public async Task<VerificationStatusDto> StatusAsync(CancellationToken ct)
    {
        var me = Me;
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == me, ct);
        var latest = await db.IdentitySubmissions.AsNoTracking().Where(s => s.UserId == me).OrderByDescending(s => s.SubmittedAt).FirstOrDefaultAsync(ct);
        return new VerificationStatusDto(u.IdentityLevel, u.PhoneNumber is null ? null : PhoneNumbers.Mask(u.PhoneNumber), u.IsPhoneVerified,
            latest is null ? null : new SubmissionSummaryDto(latest.Id, latest.Level, latest.Status, latest.SubmittedAt, latest.ReviewedAt,
                latest.Status == IdentitySubmissionStatus.Rejected ? latest.DecisionNote : null));
    }

    public async Task SubmitAsync(SubmitVerificationCommand cmd, CancellationToken ct)
    {
        var me = Me;
        var user = await db.Users.SingleAsync(u => u.Id == me, ct);
        if (await db.IdentitySubmissions.AnyAsync(s => s.UserId == me && s.Status == IdentitySubmissionStatus.Pending, ct))
            throw new DomainConflictException("submission_pending", "Your identity check is already being reviewed.");
        var now = clock.UtcNow;
        var docs = cmd.Files.Select(f => new UploadedDocument(f.Kind, f.ContentType, f.Content)).ToList();
        var submission = IdentitySubmission.Create(user, cmd.Level, cmd.IdType, cmd.IdNumber, cmd.Bvn, docs, protector.Protect, now);
        db.IdentitySubmissions.Add(submission);
        foreach (var d in docs) db.IdentityDocuments.Add(IdentityDocument.Create(submission.Id, d, protector.Protect, now));
        // Never log identifiers or file contents — only that a check was submitted.
        audit.Write(me, "identity.submitted", "identity_submission", submission.Id, null, new { level = cmd.Level.ToString(), idType = cmd.IdType.ToString(), files = docs.Count });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Deletes encrypted identity data once its retention period ends (runs in the background worker).</summary>
public sealed class IdentityRetention(IAppDbContext db, IAppClock clock, IdentityOptions options, AuditWriter audit)
{
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddDays(-options.DocumentRetentionDays);
        var due = await db.IdentitySubmissions
            .Where(s => s.Status != IdentitySubmissionStatus.Pending && s.DataPurgedAt == null && s.ReviewedAt <= cutoff)
            .Take(100).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var ids = due.Select(s => s.Id).ToList();
        foreach (var doc in await db.IdentityDocuments.Where(d => ids.Contains(d.SubmissionId) && d.EncryptedContent != null).ToListAsync(ct))
            doc.Purge();
        foreach (var s in due)
        {
            s.PurgeSensitiveData(clock.UtcNow);
            audit.Write(null, "identity.data_purged", "identity_submission", s.Id, null, new { retentionDays = options.DocumentRetentionDays });
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
