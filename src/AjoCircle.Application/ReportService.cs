using System.Globalization;
using System.Text;
using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Records;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>The six reports of brief §14.2.</summary>
public enum ReportKind { Roster, Contributions, Outstanding, Payouts, Timeline, Disputes }

/// <summary>
/// Group reports and CSV exports (brief §14.2). Every report is built from what the caller may see in the app: the
/// organizer gets the whole group; a member gets their own rows plus what the group already shares with them (the
/// rotating payout schedule, terms history). "Recorded" always means organizer records — never verified money.
/// </summary>
public sealed class ReportService(IAppDbContext db, GroupAccess access, IAppClock clock, AuditWriter audit)
{
    private static readonly RecordStatus[] Standing = [RecordStatus.RecordedByOrganizer, RecordStatus.ConfirmedByMember];

    public async Task<ReportSummaryDto> SummaryAsync(Guid groupId, CancellationToken ct)
    {
        var v = await access.ViewAsync(groupId, ct);
        var mine = v.IsOrganizer ? null : v.Membership?.Id;
        var today = clock.LagosToday;
        var obligations = db.ContributionObligations.Where(o => o.GroupId == groupId && o.Status != ObligationStatus.Cancelled);
        var records = db.ContributionRecords.Where(r => r.GroupId == groupId && Standing.Contains(r.Status));
        var disputes = db.Disputes.Where(d => d.GroupId == groupId);
        if (mine is { } m)
        {
            obligations = obligations.Where(o => o.MembershipId == m);
            records = records.Where(r => r.SubjectMembershipId == m);
            disputes = disputes.Where(d => d.RaisedBy == access.Me);
        }
        var scheduledToDate = await obligations.Where(o => o.DueDate <= today).SumAsync(o => (long?)o.AmountMinor, ct) ?? 0;
        var recorded = await records.SumAsync(r => (long?)r.AmountMinor, ct) ?? 0;
        var overdue = await obligations.Where(o => o.Status == ObligationStatus.Due && o.DueDate < today).ToListAsync(ct);
        var confirmed = await records.CountAsync(r => r.Status == RecordStatus.ConfirmedByMember, ct);
        var recordedCount = await records.CountAsync(ct);
        return new ReportSummaryDto(!v.IsOrganizer, v.Group.Name, today,
            new MoneyDto(scheduledToDate, v.Group.Currency), new MoneyDto(recorded, v.Group.Currency),
            overdue.Count, new MoneyDto(overdue.Sum(o => o.AmountMinor), v.Group.Currency),
            recordedCount, confirmed, await disputes.CountAsync(d => d.Status != DisputeStatus.Resolved, ct),
            Enum.GetValues<ReportKind>().Where(k => k != ReportKind.Timeline || v.IsOrganizer).ToList());
    }

    public async Task<ReportTable> BuildAsync(Guid groupId, ReportKind kind, CancellationToken ct)
    {
        var v = await access.ViewAsync(groupId, ct);
        return kind switch
        {
            ReportKind.Roster => await RosterAsync(v, ct),
            ReportKind.Contributions => await ContributionsAsync(v, overdueOnly: false, ct),
            ReportKind.Outstanding => await ContributionsAsync(v, overdueOnly: true, ct),
            ReportKind.Payouts => await PayoutsAsync(v, ct),
            ReportKind.Timeline => await TimelineAsync(v, ct),
            ReportKind.Disputes => await DisputesAsync(v, ct),
            _ => throw new NotFoundException(),
        };
    }

    /// <summary>CSV for spreadsheets. Exports are audited (who exported what), never their contents.</summary>
    public async Task<(string FileName, byte[] Content)> ExportCsvAsync(Guid groupId, ReportKind kind, CancellationToken ct)
    {
        var table = await BuildAsync(groupId, kind, ct);
        audit.Write(access.Me, "report.exported", "group", groupId, groupId, new { report = kind.ToString(), rows = table.Rows.Count });
        await db.SaveChangesAsync(ct);
        var slug = new string(table.GroupName.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return ($"{slug}-{kind.ToString().ToLowerInvariant()}-{clock.LagosToday:yyyy-MM-dd}.csv", Csv(table));
    }

    // ---------- reports ----------

    private async Task<ReportTable> RosterAsync(GroupViewer v, CancellationToken ct)
    {
        var org = v.IsOrganizer;
        var rows = await (from m in db.GroupMemberships
                          join u in db.Users on m.UserId equals u.Id
                          join sv in db.SchemeVersions on m.AcceptedSchemeVersionId equals sv.Id
                          join p in db.PayoutPositions on m.Id equals p.MembershipId into pp
                          from p in pp.DefaultIfEmpty()
                          join s in db.AllocationShares on m.Id equals s.MembershipId into ss
                          from s in ss.DefaultIfEmpty()
                          where m.GroupId == v.Group.Id && m.Status != MembershipStatus.Removed
                          orderby m.JoinSequence
                          select new { m, u.DisplayName, u.Email, sv.Version, Position = (int?)p.Position, Share = (int?)s.BasisPoints }).ToListAsync(ct);
        // Members see names and positions (as on the Members tab) but never contact details or exit reasons.
        return new ReportTable(ReportKind.Roster, "Group roster and membership status", v.Group.Name, clock.LagosToday,
            org ? ["Member", "Email", "Joined", "Status", "Left on", "Reason for leaving", "Terms version", "Payout position", "Share %"]
                : ["Member", "Joined", "Status", "Terms version", "Payout position", "Share %"],
            rows.Select(r => org
                ? Row(r.DisplayName, r.Email, Day(r.m.JoinedAt), Human(r.m.Status.ToString()), r.m.ExitedAt is { } x ? Day(x) : "", r.m.ExitReason ?? "",
                    r.Version.ToString(), r.Position?.ToString() ?? "", Pct(r.Share))
                : Row(r.DisplayName, Day(r.m.JoinedAt), Human(r.m.Status.ToString()), r.Version.ToString(), r.Position?.ToString() ?? "", Pct(r.Share))).ToList(),
            "Members are listed in join order.");
    }

    private async Task<ReportTable> ContributionsAsync(GroupViewer v, bool overdueOnly, CancellationToken ct)
    {
        var today = clock.LagosToday;
        var q = from o in db.ContributionObligations
                join r in db.SchemeRounds on o.RoundId equals r.Id
                join m in db.GroupMemberships on o.MembershipId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                where o.GroupId == v.Group.Id
                select new { o, r.RoundNumber, u.DisplayName, m.JoinSequence };
        if (!v.IsOrganizer) { var mid = v.Membership!.Id; q = q.Where(x => x.o.MembershipId == mid); }
        if (overdueOnly) q = q.Where(x => x.o.Status == ObligationStatus.Due && x.o.DueDate < today);
        var rows = await q.OrderBy(x => x.RoundNumber).ThenBy(x => x.JoinSequence).Take(10_000).ToListAsync(ct);

        var ids = rows.Select(x => x.o.Id).ToList();
        var current = await db.ContributionRecords.Where(c => ids.Contains(c.ObligationId) && c.Status != RecordStatus.Reversed)
            .ToDictionaryAsync(c => c.ObligationId, ct);
        var names = await access.NamesAsync(current.Values.Select(c => (Guid?)c.RecordedBy), ct);

        if (overdueOnly)
            return new ReportTable(ReportKind.Outstanding, "Outstanding obligations (no payment recorded after the due date)", v.Group.Name, today,
                ["Round", "Member", "Due date", "Days overdue", "Amount owed (NGN)"],
                rows.Select(x => Row(x.RoundNumber.ToString(), x.DisplayName, x.o.DueDate.ToString("yyyy-MM-dd"),
                    (today.DayNumber - x.o.DueDate.DayNumber).ToString(), Naira(x.o.AmountMinor))).ToList(),
                "Overdue means no payment has been recorded by the organizer — the member may have paid and not yet been recorded.");

        return new ReportTable(ReportKind.Contributions, "Contributions by member and round", v.Group.Name, today,
            ["Round", "Member", "Due date", "Amount owed (NGN)", "Status", "Recorded amount (NGN)", "Paid on", "Method", "Recorded by", "Confirmed by member on"],
            rows.Select(x =>
            {
                current.TryGetValue(x.o.Id, out var c);
                return Row(x.RoundNumber.ToString(), x.DisplayName, x.o.DueDate.ToString("yyyy-MM-dd"), Naira(x.o.AmountMinor), Human(x.o.Status.ToString()),
                    c is null ? "" : Naira(c.AmountMinor), c?.PaidOn.ToString("yyyy-MM-dd") ?? "", c is null ? "" : Human(c.Method.ToString()),
                    c is null ? "" : names.GetValueOrDefault(c.RecordedBy, ""), c?.ConfirmedAt is { } at ? Day(at) : "");
            }).ToList(),
            "Recorded = reported by the organizer; confirmed = the member agreed. Neither is a bank or provider verification. Evidence references are not exported.");
    }

    private async Task<ReportTable> PayoutsAsync(GroupViewer v, CancellationToken ct)
    {
        var q = from p in db.Payouts
                join m in db.GroupMemberships on p.RecipientMembershipId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                join r in db.SchemeRounds on p.RoundId equals (Guid?)r.Id into rr
                from r in rr.DefaultIfEmpty()
                where p.GroupId == v.Group.Id
                select new { p, u.DisplayName, Round = (int?)r.RoundNumber };
        if (!v.IsOrganizer)
        {
            var mid = v.Membership!.Id;
            q = q.Where(x => x.p.Kind == PayoutKind.RotatingPayout || x.p.RecipientMembershipId == mid); // same rule as the app
        }
        var rows = await q.OrderBy(x => x.p.ScheduledDate).ThenBy(x => x.Round).Take(10_000).ToListAsync(ct);
        var ids = rows.Select(x => x.p.Id).ToList();
        var current = await db.PayoutRecords.Where(c => ids.Contains(c.PayoutId) && c.Status != RecordStatus.Reversed).ToDictionaryAsync(c => c.PayoutId, ct);
        var mineId = v.Membership?.Id;
        return new ReportTable(ReportKind.Payouts, "Payout order, scheduled payouts and recorded outcomes", v.Group.Name, clock.LagosToday,
            ["Kind", "Round", "Recipient", "Scheduled date", "Scheduled amount (NGN)", "Status", "Recorded amount (NGN)", "Paid on"],
            rows.Select(x =>
            {
                // Someone else's recorded outcome is private to them and the organizer.
                var see = v.IsOrganizer || x.p.RecipientMembershipId == mineId;
                current.TryGetValue(x.p.Id, out var c);
                return Row(KindText(x.p.Kind), x.Round?.ToString() ?? "", x.DisplayName, x.p.ScheduledDate.ToString("yyyy-MM-dd"), Naira(x.p.AmountMinor),
                    Human(x.p.Status.ToString()), see && c is not null ? Naira(c.AmountMinor) : "", see ? c?.PaidOn.ToString("yyyy-MM-dd") ?? "" : "");
            }).ToList(),
            "Scheduled is not paid or guaranteed. Recorded outcomes are the organizer's reports; the app never moves money.");
    }

    /// <summary>Organizer only: the activity log plus every terms version (members see versions on the Overview).</summary>
    private async Task<ReportTable> TimelineAsync(GroupViewer v, CancellationToken ct)
    {
        if (!v.IsOrganizer) throw new ForbiddenException("Only the organizer can export the group's activity log.");
        var log = await (from a in db.AuditLogs
                         join u in db.Users on a.ActorId equals u.Id into uu
                         from u in uu.DefaultIfEmpty()
                         where a.GroupId == v.Group.Id
                         orderby a.CreatedAt
                         select new { a.CreatedAt, a.Action, Name = u != null ? u.DisplayName : "System" }).Take(10_000).ToListAsync(ct);
        var versions = await db.SchemeVersions.Where(s => s.GroupId == v.Group.Id).OrderBy(s => s.Version).ToListAsync(ct);
        var rows = versions.Select(s => (s.CreatedAt, Row(Stamp(s.CreatedAt), "terms.version_published", $"Terms version {s.Version}", "")))
            .Concat(log.Select(a => (a.CreatedAt, Row(Stamp(a.CreatedAt), a.Action, "", a.Name))))
            .OrderBy(x => x.CreatedAt).Select(x => x.Item2).ToList();
        return new ReportTable(ReportKind.Timeline, "Group timeline and terms history", v.Group.Name, clock.LagosToday,
            ["When (WAT)", "Event", "Detail", "By"], rows, "From the append-only activity log. Entries can't be edited or deleted.");
    }

    private async Task<ReportTable> DisputesAsync(GroupViewer v, CancellationToken ct)
    {
        var q = db.Disputes.Where(d => d.GroupId == v.Group.Id);
        if (!v.IsOrganizer) { var me = access.Me; q = q.Where(d => d.RaisedBy == me); }
        var rows = await q.OrderBy(d => d.RaisedAt).Take(10_000).ToListAsync(ct);
        var names = await access.NamesAsync(rows.Select(d => (Guid?)d.RaisedBy), ct);
        // The dispute description and messages stay private to the thread; the report lists status only.
        return new ReportTable(ReportKind.Disputes, "Disputes and resolution status", v.Group.Name, clock.LagosToday,
            ["Reference", "Raised by", "Raised on", "About", "Category", "Status", "Escalated on", "Operations decision", "Resolution", "Resolved on"],
            rows.Select(d => Row(d.Reference, names.GetValueOrDefault(d.RaisedBy, ""), Day(d.RaisedAt),
                d.RecordType == RecordKind.ContributionRecord ? "Contribution record" : "Payout record", Human(d.Category.ToString()), Human(d.Status.ToString()),
                d.EscalatedAt is { } e ? Day(e) : "", Human(d.Decision?.ToString()), Human(d.Resolution?.ToString()), d.ResolvedAt is { } r ? Day(r) : "")).ToList(),
            "Dispute descriptions and messages are not exported.");
    }

    // ---------- formatting ----------

    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);
    /// <summary>"RecordedByOrganizer" → "Recorded by organizer", so reports read as plain language.</summary>
    private static string Human(string? code) => string.IsNullOrEmpty(code) ? "" :
        string.Concat(code.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
    private static IReadOnlyList<string> Row(params string[] cells) => cells;
    private static string Day(DateTimeOffset t) => t.ToOffset(Wat).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Stamp(DateTimeOffset t) => t.ToOffset(Wat).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static string Naira(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    private static string Pct(int? bp) => bp is { } b ? (b / 100m).ToString("0.##", CultureInfo.InvariantCulture) : "";
    private static string KindText(PayoutKind k) => k switch
    {
        PayoutKind.RotatingPayout => "Rotating payout",
        PayoutKind.Release => "Savings release",
        PayoutKind.ExitRefund => "Refund (left early)",
        _ => "Cancellation refund",
    };

    /// <summary>RFC 4180 CSV with a UTF-8 BOM (so Excel shows ₦ and names correctly) and formula-injection protection.</summary>
    internal static byte[] Csv(ReportTable t)
    {
        var sb = new StringBuilder();
        sb.Append(Line([$"{t.Title} — {t.GroupName}"])).Append(Line([$"Generated {t.AsOf:yyyy-MM-dd}. {t.Note}"])).Append("\r\n");
        sb.Append(Line(t.Columns));
        foreach (var r in t.Rows) sb.Append(Line(r));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];

        static string Line(IEnumerable<string> cells) => string.Join(",", cells.Select(Cell)) + "\r\n";
        static string Cell(string v)
        {
            // A cell starting with = + - @ (or tab/CR) is executed as a formula by spreadsheet apps — neutralise it.
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) v = "'" + v;
            return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        }
    }
}
