using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AjoCircle.Application;

/// <summary>
/// Adds notifications to the current unit of work. Callers save them together with the change that caused them, so a
/// rolled-back change never notifies anyone and a committed change always does (transactional outbox, brief §10.5).
/// </summary>
public sealed class Notifier(IAppDbContext db, IAppClock clock)
{
    public void Add(Guid userId, NotificationType type, Guid? groupId, string title, string body, string link, string? dedupeKey = null) =>
        db.Notifications.Add(Notification.Create(userId, type, groupId, title, body, link, dedupeKey, clock.UtcNow));

    public Task<Guid> UserOfMembershipAsync(Guid membershipId, CancellationToken ct) =>
        db.GroupMemberships.Where(m => m.Id == membershipId).Select(m => m.UserId).SingleAsync(ct);

    public Task<string> NameOfAsync(Guid userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).SingleAsync(ct);

    public static string Money(long minor) => $"₦{minor / 100:N0}";
    public static string Date(DateOnly d) => d.ToString("ddd, d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class NotificationService(IAppDbContext db, ICurrentUser currentUser, IAppClock clock, AuditWriter audit)
{
    private Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    public async Task<NotificationPageDto> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct)
    {
        var me = Me;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me);
        var unread = await q.CountAsync(n => n.ReadAt == null, ct);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new NotificationPageDto(rows.Select(n => new NotificationDto(n.Id, n.Type, n.GroupId, n.Title, n.Body, n.LinkPath, n.CreatedAt, n.ReadAt)).ToList(),
            page, pageSize, total, unread);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var me = Me;
        return await db.Notifications.CountAsync(n => n.UserId == me && n.ReadAt == null, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == me, ct) ?? throw new NotFoundException();
        n.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var me = Me;
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.UserId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    public async Task<SettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var me = Me;
        return await db.Users.Where(u => u.Id == me).Select(u => new SettingsDto(u.EmailNotificationsEnabled)).SingleAsync(ct);
    }

    public async Task<SettingsDto> UpdateSettingsAsync(UpdateSettingsRequest req, CancellationToken ct)
    {
        var me = Me;
        var u = await db.Users.SingleAsync(x => x.Id == me, ct);
        u.SetEmailNotifications(req.EmailNotifications);
        audit.Write(me, "user.settings_updated", "user", me, null, new { emailNotifications = req.EmailNotifications });
        await db.SaveChangesAsync(ct);
        return new SettingsDto(u.EmailNotificationsEnabled);
    }
}

/// <summary>
/// Emails pending notifications. Rows are claimed with FOR UPDATE SKIP LOCKED so several API instances can run this
/// safely, and each row moves to Sent/Skipped/Failed exactly once, so a retry never sends a duplicate.
/// </summary>
public sealed class NotificationDispatcher(IAppDbContext db, IEmailSender email, EmailTemplates templates, IAppClock clock, ILogger<NotificationDispatcher> log)
{
    public async Task<int> DispatchAsync(int batchSize, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var batch = await db.ClaimPendingNotificationsAsync(clock.UtcNow, batchSize, ct);
        if (batch.Count == 0) return 0;

        var userIds = batch.Select(n => n.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.Email, u.DisplayName, u.EmailNotificationsEnabled, u.IsContactVerified }, ct);

        foreach (var n in batch)
        {
            var u = users[n.UserId];
            if (!u.EmailNotificationsEnabled || !u.IsContactVerified) { n.EmailSkipped(); continue; }
            try
            {
                await email.SendAsync(templates.Notification(u.Email, u.DisplayName, n.Title, n.LinkPath), ct);
                n.EmailSent(clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                n.EmailFailed(ex.GetType().Name + ": " + ex.Message, clock.UtcNow);
                log.LogWarning("Notification email {NotificationId} failed (attempt {Attempt}): {Error}", n.Id, n.EmailAttempts, ex.Message);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}

/// <summary>
/// Date-driven reminders (brief §14.1). Safe to run as often as you like: each reminder has a dedupe key, so it is
/// created at most once. A reminder only says what the records show — "no payment recorded" — never that money is late.
/// </summary>
public sealed class ReminderScheduler(IAppDbContext db, Notifier notifier, IAppClock clock)
{
    public const int DueSoonDays = 2;
    public const int OverdueLookbackDays = 7;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var today = clock.LagosToday;
        var created = 0;

        var obligations = await (from o in db.ContributionObligations
                                 join g in db.Groups on o.GroupId equals g.Id
                                 join m in db.GroupMemberships on o.MembershipId equals m.Id
                                 join r in db.SchemeRounds on o.RoundId equals r.Id
                                 where g.Status == GroupStatus.Active && o.Status == ObligationStatus.Due
                                       && o.DueDate >= today.AddDays(-OverdueLookbackDays) && o.DueDate <= today.AddDays(DueSoonDays)
                                 select new { o.Id, o.GroupId, GroupName = g.Name, m.UserId, o.AmountMinor, o.DueDate, r.RoundNumber }).ToListAsync(ct);
        foreach (var o in obligations)
        {
            var overdue = o.DueDate < today;
            var key = $"reminder:{(overdue ? "overdue" : "due_soon")}:{o.Id}";
            var link = $"/app/groups/{o.GroupId}/contributions/{o.Id}";
            if (overdue)
                created += await AddOnceAsync(o.UserId, NotificationType.ContributionOverdue, o.GroupId, key, link,
                    $"No payment is recorded yet for your round {o.RoundNumber} contribution in {o.GroupName}",
                    $"Your {Notifier.Money(o.AmountMinor)} contribution was due {Notifier.Date(o.DueDate)}. If you've already paid, ask your organizer to record it.", ct);
            else
                created += await AddOnceAsync(o.UserId, NotificationType.ContributionDueSoon, o.GroupId, key, link,
                    $"Your round {o.RoundNumber} contribution in {o.GroupName} is due {(o.DueDate == today ? "today" : Notifier.Date(o.DueDate))}",
                    $"{Notifier.Money(o.AmountMinor)} is due {Notifier.Date(o.DueDate)}. Pay your organizer the usual way; the app doesn't collect money.", ct);
        }

        var payouts = await (from p in db.Payouts
                             join g in db.Groups on p.GroupId equals g.Id
                             join m in db.GroupMemberships on p.RecipientMembershipId equals m.Id
                             join r in db.SchemeRounds on p.RoundId equals (Guid?)r.Id
                             where g.Status == GroupStatus.Active && p.Kind == PayoutKind.RotatingPayout && p.Status == PayoutStatus.Scheduled
                                   && p.ScheduledDate >= today && p.ScheduledDate <= today.AddDays(DueSoonDays)
                             select new { p.Id, p.GroupId, GroupName = g.Name, m.UserId, p.AmountMinor, p.ScheduledDate, r.RoundNumber }).ToListAsync(ct);
        foreach (var p in payouts)
            created += await AddOnceAsync(p.UserId, NotificationType.PayoutApproaching, p.GroupId, $"reminder:payout:{p.Id}",
                $"/app/groups/{p.GroupId}/payouts/{p.Id}",
                $"Your payout from {p.GroupName} is scheduled for {Notifier.Date(p.ScheduledDate)}",
                $"Round {p.RoundNumber}: {Notifier.Money(p.AmountMinor)} is scheduled. Scheduled isn't the same as paid — your organizer pays you outside the app and records it here.", ct);

        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<int> AddOnceAsync(Guid userId, NotificationType type, Guid groupId, string key, string link, string title, string body, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.DedupeKey == key, ct)) return 0;
        notifier.Add(userId, type, groupId, title, body, link, key);
        return 1;
    }
}
