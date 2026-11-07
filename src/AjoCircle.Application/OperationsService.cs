using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>
/// Who may use the operations workspace: staff with the operations role who passed the second factor in the last
/// 12 hours (brief §11.1 "Require MFA for privileged operations accounts"). Checked against the database on every call.
/// </summary>
public sealed class OperationsAccess(IAppDbContext db, ICurrentUser currentUser, IAppClock clock)
{
    public Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    /// <summary>Operations role only — used to set up or pass the second factor.</summary>
    public async Task<User> StaffAsync(CancellationToken ct)
    {
        var me = Me;
        var u = await db.Users.SingleAsync(x => x.Id == me, ct);
        if (!u.IsOperations) throw new NotFoundException(); // the workspace doesn't exist for everyone else
        return u;
    }

    /// <summary>Operations role + a recent second factor.</summary>
    public async Task<User> OperatorAsync(CancellationToken ct)
    {
        var u = await StaffAsync(ct);
        if (!u.IsMfaEnabled) throw new DomainException("mfa_setup_required", "Set up two-step sign-in to use the operations workspace.");
        if (currentUser.MfaVerifiedAt is not { } at || clock.UtcNow - at >= AuthService.OperationsSessionLifetime)
            throw new DomainException("mfa_required", "Enter the code from your authenticator app to continue.");
        return u;
    }

    /// <summary>Staff can't investigate a group they organize or belong to (least privilege, no conflicts of interest).</summary>
    public async Task EnsureNoConflictAsync(User staff, Guid groupId, CancellationToken ct)
    {
        var involved = await db.Groups.AnyAsync(g => g.Id == groupId && g.OrganizerId == staff.Id, ct)
                       || await db.GroupMemberships.AnyAsync(m => m.GroupId == groupId && m.UserId == staff.Id, ct);
        if (involved) throw new ForbiddenException("You're part of this group, so another member of the operations team must handle it.");
    }
}

public sealed class OperationsService(IAppDbContext db, OperationsAccess access, IAppClock clock, AuditWriter audit, ISecretProtector protector,
    Notifier notifier, AppOptions app)
{
    // ---------- Second factor ----------

    public async Task<MfaSetupDto> BeginMfaSetupAsync(CancellationToken ct)
    {
        var u = await access.StaffAsync(ct);
        var secret = Totp.NewSecret();
        u.BeginMfaSetup(protector.Protect(Convert.ToBase64String(secret)));
        await db.SaveChangesAsync(ct);
        var b32 = Totp.Base32(secret);
        var label = Uri.EscapeDataString($"{app.ProductName}:{u.Email}");
        return new MfaSetupDto(b32, $"otpauth://totp/{label}?secret={b32}&issuer={Uri.EscapeDataString(app.ProductName)}&digits=6&period=30");
    }

    /// <summary>Confirms setup with a first code. Returns the user so the API can mark this session as verified.</summary>
    public async Task<User> EnableMfaAsync(MfaCodeRequest req, CancellationToken ct)
    {
        var u = await access.StaffAsync(ct);
        if (u.IsMfaEnabled) throw new DomainConflictException("mfa_already_enabled", "Two-step sign-in is already set up.");
        if (u.MfaSecretProtected is null) throw new DomainConflictException("mfa_not_started", "Start the setup first.");
        var step = Totp.Verify(Secret(u), req.Code, clock.UtcNow, null) ?? throw BadCode();
        u.EnableMfa(step, clock.UtcNow);
        audit.Write(u.Id, "operations.mfa_enabled", "user", u.Id, null);
        await db.SaveChangesAsync(ct);
        return u;
    }

    public async Task<User> VerifyMfaAsync(MfaCodeRequest req, CancellationToken ct)
    {
        var u = await access.StaffAsync(ct);
        if (!u.IsMfaEnabled) throw new DomainException("mfa_setup_required", "Set up two-step sign-in first.");
        var step = Totp.Verify(Secret(u), req.Code, clock.UtcNow, u.MfaLastStep);
        if (step is null)
        {
            audit.Write(u.Id, "operations.mfa_failed", "user", u.Id, null); // brief §11.3: watch for suspicious sign-in activity
            await db.SaveChangesAsync(ct);
            throw BadCode();
        }
        u.RecordMfaStep(step.Value);
        audit.Write(u.Id, "operations.mfa_verified", "user", u.Id, null);
        await db.SaveChangesAsync(ct);
        return u;
    }

    private byte[] Secret(User u) => Convert.FromBase64String(protector.Unprotect(u.MfaSecretProtected!));
    private static DomainException BadCode() => new("mfa_code_invalid", "That code didn't match. Check your authenticator app and try again.");

    // ---------- Identity verification queue ----------

    public async Task<IReadOnlyList<VerificationQueueItemDto>> VerificationsAsync(IdentitySubmissionStatus? status, CancellationToken ct)
    {
        await access.OperatorAsync(ct);
        var q = from s in db.IdentitySubmissions join u in db.Users on s.UserId equals u.Id select new { s, u.DisplayName, u.Email };
        if (status is { } st) q = q.Where(x => x.s.Status == st);
        var rows = await q.OrderBy(x => x.s.SubmittedAt).Take(200).AsNoTracking().ToListAsync(ct);
        return rows.Select(x => Item(x.s, x.DisplayName, x.Email)).ToList();
    }

    public async Task<VerificationDetailDto> VerificationAsync(Guid id, CancellationToken ct)
    {
        var me = await access.OperatorAsync(ct);
        var row = await (from s in db.IdentitySubmissions join u in db.Users on s.UserId equals u.Id where s.Id == id select new { s, u.DisplayName, u.Email })
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
        var docs = await db.IdentityDocuments.Where(d => d.SubmissionId == id)
            .Select(d => new IdentityDocumentDto(d.Id, d.Kind, d.ContentType, d.SizeBytes, d.EncryptedContent != null)).ToListAsync(ct);
        var reviewer = row.s.ReviewedBy is { } rb ? await db.Users.Where(u => u.Id == rb).Select(u => u.DisplayName).SingleAsync(ct) : null;
        var actions = row.s.Status == IdentitySubmissionStatus.Pending && row.s.UserId != me.Id ? new[] { "approve", "reject" } : [];
        audit.Write(me.Id, "operations.identity_viewed", "identity_submission", id, null);
        await db.SaveChangesAsync(ct);
        return new VerificationDetailDto(Item(row.s, row.DisplayName, row.Email), PhoneNumbers.Mask(row.s.PhoneNumber), row.s.IdNumberLast4, row.s.BvnLast4,
            row.s.DataPurgedAt is not null, row.s.DecisionNote, reviewer, docs, actions);
    }

    /// <summary>Decrypts one document for viewing. Every view is audited (brief §11.2 access policy).</summary>
    public async Task<DocumentFile> DocumentAsync(Guid submissionId, Guid documentId, CancellationToken ct)
    {
        var me = await access.OperatorAsync(ct);
        var doc = await db.IdentityDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.SubmissionId == submissionId, ct)
                  ?? throw new NotFoundException();
        if (doc.EncryptedContent is null) throw new DomainConflictException("document_purged", "This document was deleted at the end of its retention period.");
        audit.Write(me.Id, "operations.identity_document_viewed", "identity_document", doc.Id, null, new { submissionId, kind = doc.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return new DocumentFile(doc.ContentType, protector.Unprotect(doc.EncryptedContent));
    }

    public async Task<RevealedIdentifiersDto> RevealAsync(Guid submissionId, CancellationToken ct)
    {
        var me = await access.OperatorAsync(ct);
        var s = await db.IdentitySubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == submissionId, ct) ?? throw new NotFoundException();
        if (s.IdNumberProtected is null) throw new DomainConflictException("document_purged", "These details were deleted at the end of their retention period.");
        audit.Write(me.Id, "operations.identity_revealed", "identity_submission", s.Id, null);
        await db.SaveChangesAsync(ct);
        return new RevealedIdentifiersDto(protector.Unprotect(s.IdNumberProtected), s.BvnProtected is null ? null : protector.Unprotect(s.BvnProtected));
    }

    public async Task ApproveAsync(Guid id, ReviewDecisionRequest req, CancellationToken ct)
    {
        var me = await access.OperatorAsync(ct);
        var s = await db.IdentitySubmissions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        var subject = await db.Users.SingleAsync(u => u.Id == s.UserId, ct);
        s.Approve(me, subject, req.Note, clock.UtcNow);
        audit.Write(me.Id, "operations.identity_approved", "identity_submission", s.Id, null, new { level = s.Level.ToString() });
        notifier.Add(subject.Id, NotificationType.IdentityApproved, null,
            s.Level == IdentityLevel.Organizer ? "You're verified as an organizer" : "Your identity is verified",
            s.Level == IdentityLevel.Organizer ? "You can now create and run groups." : "You can now join groups.",
            s.Level == IdentityLevel.Organizer ? "/app/groups/new" : "/app");
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid id, ReviewDecisionRequest req, CancellationToken ct)
    {
        var me = await access.OperatorAsync(ct);
        var s = await db.IdentitySubmissions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        s.Reject(me, req.Note, clock.UtcNow);
        audit.Write(me.Id, "operations.identity_rejected", "identity_submission", s.Id, null, new { level = s.Level.ToString() });
        notifier.Add(s.UserId, NotificationType.IdentityRejected, null, "Your identity check needs another look",
            $"Reason: “{s.DecisionNote}”. You can submit again with the corrected details.", "/app/verification");
        await db.SaveChangesAsync(ct);
    }

    private static VerificationQueueItemDto Item(IdentitySubmission s, string name, string email) =>
        new(s.Id, name, email, s.Level, s.IdType, s.Status, s.SubmittedAt, s.ReviewedAt);

    // ---------- Audit search ----------

    public async Task<PagedResult<AuditSearchItemDto>> SearchAuditAsync(Guid? groupId, string? action, string? actorEmail, DateOnly? from, DateOnly? to,
        int page, int pageSize, CancellationToken ct)
    {
        await access.OperatorAsync(ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = from a in db.AuditLogs
                join u in db.Users on a.ActorId equals u.Id into uu
                from u in uu.DefaultIfEmpty()
                join g in db.Groups on a.GroupId equals g.Id into gg
                from g in gg.DefaultIfEmpty()
                select new { a, ActorName = u != null ? u.DisplayName : null, ActorEmail = u != null ? u.Email : null, GroupName = g != null ? g.Name : null };
        if (groupId is { } gid) q = q.Where(x => x.a.GroupId == gid);
        if (!string.IsNullOrWhiteSpace(action)) { var act = action.Trim(); q = q.Where(x => x.a.Action.StartsWith(act)); }
        if (!string.IsNullOrWhiteSpace(actorEmail)) { var em = User.NormalizeEmail(actorEmail); q = q.Where(x => x.ActorEmail == em); }
        if (from is { } f) { var fromUtc = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(1)); q = q.Where(x => x.a.CreatedAt >= fromUtc); }
        if (to is { } t) { var toUtc = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(1)); q = q.Where(x => x.a.CreatedAt < toUtc); }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditSearchItemDto>(rows.Select(x => new AuditSearchItemDto(x.a.Id, x.a.Action, x.a.EntityType, x.a.EntityId, x.a.GroupId,
            x.GroupName, x.ActorName, x.ActorEmail, x.a.Metadata, x.a.CreatedAt)).ToList(), page, pageSize, total);
    }
}
