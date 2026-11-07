using AjoCircle.Application.Contracts;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Schemes;
using Microsoft.EntityFrameworkCore;

namespace AjoCircle.Application;

/// <summary>
/// Group use cases for every scheme type. Every method resolves the caller's relationship to the group (organizer /
/// member) on the server. A group the caller cannot see returns 404, so IDs cannot be probed (brief §2, §3.1).
/// Members who left a fixed-term group early keep read access to their own history and refund.
/// </summary>
public sealed class GroupService(IAppDbContext db, ICurrentUser currentUser, IAppClock clock, AuditWriter audit, Notifier notifier,
    SavingsLedger ledger, SavingsService savings, IdentityGate gate)
{
    private Guid Me => currentUser.UserId ?? throw new UnauthenticatedException();

    // ---------- Access helpers ----------

    private async Task<Group> OrganizerGroupAsync(Guid groupId, CancellationToken ct, bool track = true)
    {
        var q = track ? db.Groups : db.Groups.AsNoTracking();
        var g = await q.SingleOrDefaultAsync(x => x.Id == groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        return g;
    }

    private async Task<(Group Group, ViewerRole Role, GroupMembership? Membership)> ViewableGroupAsync(Guid groupId, CancellationToken ct)
    {
        var me = Me;
        var g = await db.Groups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == groupId, ct) ?? throw new NotFoundException();
        var membership = await db.GroupMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == me && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Exited), ct);
        if (g.IsOrganizer(me)) return (g, ViewerRole.Organizer, membership);
        if (membership is not null) return (g, ViewerRole.Member, membership);
        throw new NotFoundException();
    }

    /// <summary>Creating or running a group needs organizer-level identity verification (owner decision 7).</summary>
    private Task RequireOrganizerAsync(CancellationToken ct) => gate.RequireAsync(Me, Domain.Identity.IdentityLevel.Organizer, ct);

    /// <summary>The published terms, or the draft before publishing.</summary>
    private async Task<string> TermsJsonAsync(Group g, CancellationToken ct) => g.CurrentSchemeVersionId is { } vid
        ? await db.SchemeVersions.Where(v => v.Id == vid).Select(v => v.RulesJson).SingleAsync(ct)
        : g.DraftRulesJson ?? throw new DomainException("rules_missing", "Group has no rules.");

    private async Task<RotatingSchemeRules> CurrentRulesAsync(Group g, CancellationToken ct) => SchemeJson.Deserialize(await TermsJsonAsync(g, ct));
    private async Task<FixedTermSchemeRules> CurrentFixedTermAsync(Group g, CancellationToken ct) => SchemeJson.DeserializeFixedTerm(await TermsJsonAsync(g, ct));

    // ---------- Queries ----------

    public SchemePreviewDto Preview(RotatingRulesDto rules)
    {
        var domain = rules.ToDomain();
        domain.Validate(clock.LagosToday);
        return ToPreview(domain);
    }

    public SchemePreviewDto PreviewFixedTerm(FixedTermRulesDto rules)
    {
        var domain = rules.ToDomain();
        domain.Validate(clock.LagosToday);
        return ToPreview(domain);
    }

    private static SchemePreviewDto ToPreview(RotatingSchemeRules r) => new(
        SchemeSummaryDto.From(RotatingSchemeCalculator.Summarize(r)),
        RotatingSchemeCalculator.BuildSchedule(r).Select(p => new RoundPlanDto(p.RoundNumber, p.ContributionDueDate, p.PayoutDate)).ToList());

    private static SchemePreviewDto ToPreview(FixedTermSchemeRules r) => new(null,
        FixedTermCalculator.BuildSchedule(r).Select(p => new RoundPlanDto(p.Number, p.DueDate, null)).ToList(),
        FixedTermSummaryDto.From(FixedTermCalculator.Summarize(r)));

    public async Task<IReadOnlyList<GroupListItemDto>> ListMineAsync(CancellationToken ct)
    {
        var me = Me;
        var memberGroupIds = db.GroupMemberships.Where(m => m.UserId == me && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Exited)).Select(m => m.GroupId);
        var groups = await db.Groups.AsNoTracking()
            .Where(g => g.OrganizerId == me || memberGroupIds.Contains(g.Id))
            .OrderByDescending(g => g.CreatedAt).Take(200).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var counts = await db.GroupMemberships.Where(m => ids.Contains(m.GroupId) && m.Status == MembershipStatus.Active)
            .GroupBy(m => m.GroupId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var versionIds = groups.Where(g => g.CurrentSchemeVersionId != null).Select(g => g.CurrentSchemeVersionId!.Value).ToList();
        var versions = await db.SchemeVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        return groups.Select(g =>
        {
            var b = g.Basics(g.CurrentSchemeVersionId is { } vid ? versions[vid].RulesJson : null);
            return new GroupListItemDto(g.Id, g.Name, g.SchemeType, g.Status, g.IsOrganizer(me) ? ViewerRole.Organizer : ViewerRole.Member,
                counts.GetValueOrDefault(g.Id), b.MemberCount, new MoneyDto(b.ContributionAmountMinor, b.Currency), b.Frequency, b.StartDate);
        }).ToList();
    }

    public async Task<GroupDetailDto> GetAsync(Guid groupId, CancellationToken ct)
    {
        var (g, role, membership) = await ViewableGroupAsync(groupId, ct);
        var json = await TermsJsonAsync(g, ct);
        var organizerName = await db.Users.Where(u => u.Id == g.OrganizerId).Select(u => u.DisplayName).SingleAsync(ct);
        var activeCount = await db.GroupMemberships.CountAsync(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active, ct);
        var version = g.CurrentSchemeVersionId is { } vid
            ? await db.SchemeVersions.AsNoTracking().SingleAsync(v => v.Id == vid, ct) : null;

        MyMembershipDto? mine = null;
        if (membership is not null)
        {
            var pos = await db.PayoutPositions.Where(p => p.MembershipId == membership.Id).Select(p => (int?)p.Position).SingleOrDefaultAsync(ct);
            var share = await db.AllocationShares.Where(s => s.MembershipId == membership.Id).Select(s => (int?)s.BasisPoints).SingleOrDefaultAsync(ct);
            var acceptedVersion = await db.SchemeVersions.Where(v => v.Id == membership.AcceptedSchemeVersionId).Select(v => v.Version).SingleAsync(ct);
            mine = new MyMembershipDto(membership.Id, pos, acceptedVersion, membership.JoinedAt, membership.Status, membership.ExitedAt, share);
        }

        RotatingRulesDto? rRules = null; SchemeSummaryDto? rSummary = null;
        FixedTermRulesDto? fRules = null; FixedTermSummaryDto? fSummary = null;
        SavingsProgressDto? progress = null;
        IReadOnlyList<string> actions;
        int capacity;
        if (g.IsFixedTerm)
        {
            var f = SchemeJson.DeserializeFixedTerm(json);
            fRules = FixedTermRulesDto.From(f);
            var summary = FixedTermCalculator.Summarize(f);
            fSummary = FixedTermSummaryDto.From(summary);
            capacity = f.MemberCount;
            if (g.StartedAt is not null)
                progress = await ledger.ProgressAsync(g, f, role == ViewerRole.Organizer ? null : membership, ct);
            actions = role == ViewerRole.Organizer
                ? FixedTermActions(g, f, summary, activeCount)
                : g.Status == GroupStatus.Active && membership?.Status == MembershipStatus.Active ? ["leave"] : [];
        }
        else
        {
            var r = SchemeJson.Deserialize(json);
            rRules = RotatingRulesDto.From(r);
            rSummary = SchemeSummaryDto.From(RotatingSchemeCalculator.Summarize(r));
            capacity = r.MemberCount;
            actions = role == ViewerRole.Organizer ? RotatingActions(g, r, activeCount) : [];
        }

        return new GroupDetailDto(g.Id, g.Name, g.SchemeType, g.Status, role, organizerName, rRules, rSummary, fRules, fSummary, capacity,
            version?.Version, version?.Id, activeCount, g.CreatedAt, g.PublishedAt, g.StartedAt,
            g.CancelledAt, g.CancellationReason, // members are told why a group was cancelled (brief §14.1)
            g.CompletedAt, mine, progress, actions);
    }

    private static IReadOnlyList<string> RotatingActions(Group g, RotatingSchemeRules rules, int activeCount) => g.Status switch
    {
        GroupStatus.Draft => ["edit", "publish", "cancel"],
        GroupStatus.OpenForJoining => activeCount >= RotatingSchemeRules.MinMembers
            ? ["invite", "closeEnrolment", "cancel"] : ["invite", "cancel"],
        GroupStatus.ReadyToStart => rules.PayoutOrder == PayoutOrderMethod.OrganizerAssigned
            ? ["assignPayoutOrder", "start", "cancel"] : ["start", "cancel"],
        _ => [],
    };

    private IReadOnlyList<string> FixedTermActions(Group g, FixedTermSchemeRules rules, FixedTermSummary summary, int activeCount) => g.Status switch
    {
        GroupStatus.Draft => ["edit", "publish", "cancel"],
        GroupStatus.OpenForJoining => activeCount >= FixedTermSchemeRules.MinMembers
            ? ["invite", "closeEnrolment", "cancel"] : ["invite", "cancel"],
        GroupStatus.ReadyToStart => rules.Allocation == SavingsAllocation.OrganizerAssigned
            ? ["assignShares", "start", "cancel"] : ["start", "cancel"],
        GroupStatus.Active => clock.LagosToday >= summary.LastDueDate ? ["recordExit", "complete", "cancel"] : ["recordExit", "cancel"],
        _ => [],
    };

    public async Task<SchemePreviewDto> SchedulePreviewAsync(Guid groupId, CancellationToken ct)
    {
        var (g, _, _) = await ViewableGroupAsync(groupId, ct);
        return g.IsFixedTerm ? ToPreview(await CurrentFixedTermAsync(g, ct)) : ToPreview(await CurrentRulesAsync(g, ct));
    }

    public async Task<IReadOnlyList<MemberDto>> MembersAsync(Guid groupId, CancellationToken ct)
    {
        var (g, role, _) = await ViewableGroupAsync(groupId, ct);
        var me = Me;
        var rows = await (from m in db.GroupMemberships
                          join u in db.Users on m.UserId equals u.Id
                          join v in db.SchemeVersions on m.AcceptedSchemeVersionId equals v.Id
                          join p in db.PayoutPositions on m.Id equals p.MembershipId into pp
                          from p in pp.DefaultIfEmpty()
                          join s in db.AllocationShares on m.Id equals s.MembershipId into ss
                          from s in ss.DefaultIfEmpty()
                          where m.GroupId == g.Id && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Exited)
                          orderby m.JoinSequence
                          select new { m, u.DisplayName, u.Email, v.Version, Position = (int?)p.Position, Share = (int?)s.BasisPoints }).ToListAsync(ct);
        // Members never see other members' contact details or why someone left.
        var organizer = role == ViewerRole.Organizer;
        return rows.Select(r => new MemberDto(r.m.Id, r.DisplayName, organizer ? r.Email : null,
            r.m.JoinSequence, r.Position, r.Version, r.m.JoinedAt, r.m.UserId == me, r.m.Status, r.m.ExitedAt,
            organizer || r.m.UserId == me ? r.m.ExitReason : null, r.Share)).ToList();
    }

    public async Task<IReadOnlyList<SchemeVersionDto>> SchemeVersionsAsync(Guid groupId, CancellationToken ct)
    {
        var (g, _, _) = await ViewableGroupAsync(groupId, ct);
        var versions = await db.SchemeVersions.AsNoTracking().Where(v => v.GroupId == g.Id).OrderBy(v => v.Version).ToListAsync(ct);
        return versions.Select(v => g.IsFixedTerm
            ? new SchemeVersionDto(v.Id, v.Version, null, v.CreatedAt, FixedTermRulesDto.From(SchemeJson.DeserializeFixedTerm(v.RulesJson)))
            : new SchemeVersionDto(v.Id, v.Version, RotatingRulesDto.From(SchemeJson.Deserialize(v.RulesJson)), v.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<RoundDto>> RoundsAsync(Guid groupId, CancellationToken ct)
    {
        var (g, role, membership) = await ViewableGroupAsync(groupId, ct);
        var rows = await (from r in db.SchemeRounds
                          join p in db.Payouts.Where(p => p.Kind == PayoutKind.RotatingPayout) on (Guid?)r.Id equals p.RoundId into pp
                          from p in pp.DefaultIfEmpty()
                          join m in db.GroupMemberships on p.RecipientMembershipId equals m.Id into mm
                          from m in mm.DefaultIfEmpty()
                          join u in db.Users on m.UserId equals u.Id into uu
                          from u in uu.DefaultIfEmpty()
                          where r.GroupId == g.Id
                          orderby r.RoundNumber
                          select new { r, p, Name = u != null ? u.DisplayName : null }).ToListAsync(ct);

        // Round totals reveal who has paid, so only the organizer gets them.
        Dictionary<Guid, RoundProgressDto> progress = [];
        if (role == ViewerRole.Organizer)
        {
            var stats = await db.ContributionObligations.Where(o => o.GroupId == g.Id && o.Status != ObligationStatus.Cancelled)
                .GroupBy(o => o.RoundId)
                .Select(x => new
                {
                    RoundId = x.Key,
                    Expected = x.Count(),
                    Recorded = x.Count(o => o.Status == ObligationStatus.RecordedByOrganizer || o.Status == ObligationStatus.ConfirmedByMember),
                    Confirmed = x.Count(o => o.Status == ObligationStatus.ConfirmedByMember),
                    Disputed = x.Count(o => o.Status == ObligationStatus.Disputed),
                    ExpectedTotal = x.Sum(o => o.AmountMinor),
                }).ToListAsync(ct);
            // Recorded total uses the amounts on current records, not the amounts owed.
            var recorded = await (from c in db.ContributionRecords
                                  join o in db.ContributionObligations on c.ObligationId equals o.Id
                                  where c.GroupId == g.Id && (c.Status == Domain.Records.RecordStatus.RecordedByOrganizer || c.Status == Domain.Records.RecordStatus.ConfirmedByMember)
                                  group c by o.RoundId into x
                                  select new { RoundId = x.Key, Total = x.Sum(c => c.AmountMinor) }).ToDictionaryAsync(x => x.RoundId, x => x.Total, ct);
            progress = stats.ToDictionary(s => s.RoundId, s => new RoundProgressDto(s.Expected, s.Recorded, s.Confirmed, s.Disputed,
                new MoneyDto(s.ExpectedTotal, g.Currency), new MoneyDto(recorded.GetValueOrDefault(s.RoundId), g.Currency)));
        }

        return rows.Select(x => new RoundDto(x.r.Id, x.r.RoundNumber, x.r.ContributionDueDate, x.p is null ? null : x.r.PayoutDate, x.r.Status,
            x.Name, x.p is null ? null : new MoneyDto(x.p.AmountMinor, x.p.Currency), x.p?.Status, x.p?.Id,
            membership is not null && x.p is not null && x.p.RecipientMembershipId == membership.Id, progress.GetValueOrDefault(x.r.Id))).ToList();
    }

    /// <summary>
    /// Every payout. Rotating payouts are the group's shared schedule; money owed back (releases and refunds) is private,
    /// so members see only their own.
    /// </summary>
    public async Task<IReadOnlyList<PayoutListItemDto>> PayoutsAsync(Guid groupId, CancellationToken ct)
    {
        var (g, role, membership) = await ViewableGroupAsync(groupId, ct);
        var q = from p in db.Payouts
                join m in db.GroupMemberships on p.RecipientMembershipId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                join r in db.SchemeRounds on p.RoundId equals (Guid?)r.Id into rr
                from r in rr.DefaultIfEmpty()
                where p.GroupId == g.Id
                select new { p, u.DisplayName, Round = (int?)r.RoundNumber };
        if (role == ViewerRole.Member)
        {
            var mid = membership!.Id;
            q = q.Where(x => x.p.Kind == PayoutKind.RotatingPayout || x.p.RecipientMembershipId == mid);
        }
        var rows = await q.OrderBy(x => x.p.ScheduledDate).ThenBy(x => x.Round).Take(500).ToListAsync(ct);
        return rows.Select(x => new PayoutListItemDto(x.p.Id, x.p.Kind, x.Round, x.DisplayName, x.p.RecipientMembershipId,
            membership is not null && x.p.RecipientMembershipId == membership.Id, new MoneyDto(x.p.AmountMinor, x.p.Currency), x.p.ScheduledDate, x.p.Status)).ToList();
    }

    public async Task<PagedResult<ObligationDto>> ContributionsAsync(Guid groupId, int page, int pageSize, CancellationToken ct)
    {
        var (g, role, membership) = await ViewableGroupAsync(groupId, ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = from o in db.ContributionObligations
                join r in db.SchemeRounds on o.RoundId equals r.Id
                join m in db.GroupMemberships on o.MembershipId equals m.Id
                join u in db.Users on m.UserId equals u.Id
                where o.GroupId == g.Id
                select new { o, r.RoundNumber, u.DisplayName, m.JoinSequence };
        if (role == ViewerRole.Member) // members see only their own obligations
        {
            var mid = membership!.Id;
            q = q.Where(x => x.o.MembershipId == mid);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.RoundNumber).ThenBy(x => x.JoinSequence).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ObligationDto>(rows.Select(x => new ObligationDto(x.o.Id, x.RoundNumber, x.DisplayName, x.o.MembershipId,
            new MoneyDto(x.o.AmountMinor, x.o.Currency), x.o.DueDate, x.o.Status)).ToList(), page, pageSize, total);
    }

    public async Task<PagedResult<AuditLogDto>> AuditLogsAsync(Guid groupId, int page, int pageSize, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct, track: false);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = from a in db.AuditLogs
                join u in db.Users on a.ActorId equals u.Id into uu
                from u in uu.DefaultIfEmpty()
                where a.GroupId == g.Id
                select new { a, Name = u != null ? u.DisplayName : null };
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(rows.Select(x => new AuditLogDto(x.a.Id, x.a.Action, x.a.EntityType, x.a.EntityId, x.Name, x.a.Metadata, x.a.CreatedAt)).ToList(),
            page, pageSize, total);
    }

    // ---------- Commands ----------

    public async Task<Guid> CreateRotatingAsync(CreateRotatingGroupRequest req, CancellationToken ct)
    {
        await RequireOrganizerAsync(ct);
        var g = Group.CreateRotatingDraft(Me, req.Name, req.Rules.ToDomain(), clock.UtcNow);
        db.Groups.Add(g);
        audit.Write(Me, "group.created", "group", g.Id, g.Id, new { g.Name, scheme = g.SchemeType.ToString() });
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    public async Task UpdateDraftAsync(Guid groupId, UpdateDraftGroupRequest req, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct);
        g.UpdateDraft(req.Name, req.Rules.ToDomain());
        audit.Write(Me, "group.draft_updated", "group", g.Id, g.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(Guid groupId, CancellationToken ct)
    {
        var g = await OrganizerGroupAsync(groupId, ct);
        await RequireOrganizerAsync(ct);
        var version = g.Publish(Me, clock.LagosToday, clock.UtcNow);
        db.SchemeVersions.Add(version);
        audit.Write(Me, "group.published", "group", g.Id, g.Id, new { schemeVersion = version.Version, schemeVersionId = version.Id });
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseEnrolmentAsync(Guid groupId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        await RequireOrganizerAsync(ct);
        var members = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).ToListAsync(ct);
        g.CloseEnrolment(members.Count, clock.UtcNow);
        await AssignJoinOrderIfNeededAsync(g, members, Me, ct);
        audit.Write(Me, "group.enrolment_closed", "group", g.Id, g.Id, new { members = members.Count });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>When a rotating group's enrolment closes under the JoinOrder method, positions follow join sequence automatically.</summary>
    /// <param name="actorId">The organizer when closing manually; null when the system closes enrolment because the group filled up.</param>
    internal async Task AssignJoinOrderIfNeededAsync(Group g, IReadOnlyCollection<GroupMembership> activeMembers, Guid? actorId, CancellationToken ct)
    {
        if (g.IsFixedTerm) return; // fixed-term groups have no payout order
        var rules = await CurrentRulesAsync(g, ct);
        if (rules.PayoutOrder != PayoutOrderMethod.JoinOrder) return;
        var order = PayoutOrderPlanner.JoinOrder(activeMembers);
        for (var i = 0; i < order.Count; i++)
            db.PayoutPositions.Add(PayoutPosition.Create(g.Id, order[i], i + 1, actorId ?? g.OrganizerId, clock.UtcNow));
        audit.Write(actorId, "group.payout_order_assigned", "group", g.Id, g.Id, new { method = "JoinOrder", automatic = actorId is null, order });
    }

    public async Task AssignPayoutOrderAsync(Guid groupId, AssignPayoutOrderRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        await RequireOrganizerAsync(ct);
        if (g.IsFixedTerm) throw new DomainConflictException("scheme_mismatch", "Fixed-term savings groups have no payout order.");
        if (g.Status != GroupStatus.ReadyToStart)
            throw new DomainConflictException("group_not_ready", "Payout order can only be set after enrolment closes and before the group starts.");
        var rules = await CurrentRulesAsync(g, ct);
        if (rules.PayoutOrder != PayoutOrderMethod.OrganizerAssigned)
            throw new DomainConflictException("payout_order_fixed", "The accepted terms say payout order follows join order, so it cannot be reassigned.");

        var memberIds = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).Select(m => m.Id).ToListAsync(ct);
        PayoutOrderPlanner.ValidateAssignment(memberIds, req.MembershipIds ?? []);

        // Pre-start positions are configuration, not financial history; the audit log keeps the previous order.
        var existing = await db.PayoutPositions.Where(p => p.GroupId == g.Id).OrderBy(p => p.Position).ToListAsync(ct);
        var previous = existing.Select(p => p.MembershipId).ToList();
        db.PayoutPositions.RemoveRange(existing);
        await db.SaveChangesAsync(ct); // free unique (group, position) slots before re-inserting
        for (var i = 0; i < req.MembershipIds!.Count; i++)
            db.PayoutPositions.Add(PayoutPosition.Create(g.Id, req.MembershipIds[i], i + 1, Me, clock.UtcNow));
        audit.Write(Me, "group.payout_order_assigned", "group", g.Id, g.Id, new { method = "OrganizerAssigned", previous, order = req.MembershipIds });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Validates and activates the group, creating its schedule in one transaction.</summary>
    public async Task StartAsync(Guid groupId, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        await RequireOrganizerAsync(ct);
        if (g.CurrentSchemeVersionId is null) throw new DomainConflictException("group_not_ready", "Publish the group before starting it.");
        var version = await db.SchemeVersions.SingleAsync(v => v.Id == g.CurrentSchemeVersionId, ct);
        var members = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active).ToListAsync(ct);

        ActivationResult result;
        if (g.IsFixedTerm)
        {
            var shares = await db.AllocationShares.Where(s => s.GroupId == g.Id).ToDictionaryAsync(s => s.MembershipId, s => s.BasisPoints, ct);
            result = FixedTermActivation.Activate(g, version, members, shares, clock.LagosToday, clock.UtcNow);
            var release = FixedTermCalculator.Summarize(SchemeJson.DeserializeFixedTerm(version.RulesJson)).ReleaseDate;
            foreach (var m in members.Where(m => m.UserId != g.OrganizerId))
            {
                var mine = result.Obligations.Where(o => o.MembershipId == m.Id).ToList();
                notifier.Add(m.UserId, NotificationType.GroupStarted, g.Id, $"{g.Name} has started",
                    $"Your first contribution of {Notifier.Money(mine[0].AmountMinor)} is due {Notifier.Date(mine.Min(o => o.DueDate))}. " +
                    $"You've agreed to {mine.Count} contributions; the money is due back on {Notifier.Date(release)} under the agreed rule. The organizer holds the money, not the app.",
                    $"/app/groups/{g.Id}/contributions");
            }
        }
        else
        {
            var positions = await db.PayoutPositions.Where(p => p.GroupId == g.Id).ToListAsync(ct);
            result = RotatingActivation.Activate(g, version, members, positions, clock.LagosToday, clock.UtcNow);
            foreach (var m in members.Where(m => m.UserId != g.OrganizerId))
            {
                var first = result.Obligations.Where(o => o.MembershipId == m.Id).MinBy(o => o.DueDate)!;
                var payout = result.Payouts.Single(p => p.RecipientMembershipId == m.Id);
                var round = result.Rounds.Single(r => r.Id == payout.RoundId).RoundNumber;
                notifier.Add(m.UserId, NotificationType.GroupStarted, g.Id, $"{g.Name} has started",
                    $"Your first contribution of {Notifier.Money(first.AmountMinor)} is due {Notifier.Date(first.DueDate)}. Your payout is round {round}, scheduled for {Notifier.Date(payout.ScheduledDate)}. Scheduled isn't the same as paid.",
                    $"/app/groups/{g.Id}/contributions");
            }
        }
        db.SchemeRounds.AddRange(result.Rounds);
        db.ContributionObligations.AddRange(result.Obligations);
        db.Payouts.AddRange(result.Payouts);
        audit.Write(Me, "group.started", "group", g.Id, g.Id,
            new { rounds = result.Rounds.Count, obligations = result.Obligations.Count, schemeVersion = version.Version, members = members.Count });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task CancelAsync(Guid groupId, CancelGroupRequest req, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        var g = await db.LockGroupAsync(groupId, ct);
        if (g is null || !g.IsOrganizer(Me)) throw new NotFoundException();
        var running = g.Status == GroupStatus.Active;
        // A running fixed-term group returns what each member paid (owner decision 2); check before changing anything.
        Dictionary<Guid, long> refunds = running && g.IsFixedTerm ? await savings.SettleCancellationAsync(g, ct) : [];
        g.Cancel(req.Reason, clock.UtcNow);
        // Outstanding invitations stop working immediately.
        var open = await db.GroupInvitations.Where(i => i.GroupId == g.Id && i.RevokedAt == null).ToListAsync(ct);
        foreach (var inv in open) inv.Revoke(Me, clock.UtcNow);
        var members = await db.GroupMemberships.Where(m => m.GroupId == g.Id && m.Status == MembershipStatus.Active && m.UserId != g.OrganizerId)
            .Select(m => new { m.Id, m.UserId }).ToListAsync(ct);
        foreach (var m in members)
        {
            var refund = refunds.GetValueOrDefault(m.Id);
            var money = !running ? "Nothing further is scheduled for this group. The app never holds money, so speak to the organizer about anything you may have paid them."
                : refund > 0 ? $"The {Notifier.Money(refund)} recorded as paid by you is owed back to you. The organizer holds the money and will record paying you back."
                : "Nothing was recorded as paid by you, so nothing is owed back.";
            notifier.Add(m.UserId, NotificationType.GroupCancelled, g.Id, $"{g.Name} was cancelled",
                $"The organizer gave this reason: “{g.CancellationReason}”. {money}", running && refund > 0 ? $"/app/groups/{g.Id}/payouts" : $"/app/groups/{g.Id}");
        }
        audit.Write(Me, "group.cancelled", "group", g.Id, g.Id, new { reason = g.CancellationReason, whileRunning = running, refunds = refunds.Count, refundTotalMinor = refunds.Values.Sum() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
