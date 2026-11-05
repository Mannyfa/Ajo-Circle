using AjoCircle.Domain.Common;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.Domain.Groups;

public static class PayoutOrderPlanner
{
    /// <summary>Position k goes to the k-th member to join.</summary>
    public static IReadOnlyList<Guid> JoinOrder(IEnumerable<GroupMembership> activeMembers) =>
        activeMembers.OrderBy(m => m.JoinSequence).Select(m => m.Id).ToList();

    /// <summary>
    /// An organizer-supplied order must be a permutation of the active members: every eligible member
    /// exactly once, no unknown or duplicate entries (brief §7.2).
    /// </summary>
    public static void ValidateAssignment(IReadOnlyCollection<Guid> activeMembershipIds, IReadOnlyList<Guid> orderedMembershipIds)
    {
        var errors = new List<string>();
        if (orderedMembershipIds.Count != activeMembershipIds.Count)
            errors.Add($"Provide exactly {activeMembershipIds.Count} positions, one per active member.");
        var dupes = orderedMembershipIds.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
        if (dupes.Count > 0) errors.Add("A member appears more than once in the payout order.");
        if (orderedMembershipIds.Except(activeMembershipIds).Any())
            errors.Add("The payout order references someone who is not an active member of this group.");
        if (activeMembershipIds.Except(orderedMembershipIds).Any())
            errors.Add("Every active member must have a payout position.");
        if (errors.Count > 0)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["membershipIds"] = errors.ToArray() });
    }
}

public sealed record ActivationResult(
    IReadOnlyList<SchemeRound> Rounds,
    IReadOnlyList<ContributionObligation> Obligations,
    IReadOnlyList<Payout> Payouts);

/// <summary>
/// Activation is a domain command, not a status update (brief §6.1). It checks membership, accepted terms,
/// payout positions and dates, then generates rounds, obligations and scheduled payouts.
/// </summary>
public static class RotatingActivation
{
    public static ActivationResult Activate(
        Group group,
        SchemeVersion currentVersion,
        IReadOnlyCollection<GroupMembership> activeMembers,
        IReadOnlyCollection<PayoutPosition> positions,
        DateOnly today,
        DateTimeOffset now)
    {
        if (group.Status != GroupStatus.ReadyToStart)
            throw new DomainConflictException("group_not_ready", "Only groups that are ready to start can be started.");
        if (group.SchemeType != SchemeType.RotatingAjo || currentVersion.GroupId != group.Id || group.CurrentSchemeVersionId != currentVersion.Id)
            throw new DomainException("scheme_mismatch", "The group's current scheme version could not be confirmed.");

        var rules = SchemeJson.Deserialize(currentVersion.RulesJson);
        var problems = new List<string>();

        if (activeMembers.Count != rules.MemberCount)
            problems.Add($"The accepted terms are for exactly {rules.MemberCount} members; {activeMembers.Count} have joined. " +
                         "A different member count changes everyone's commitment and needs an amendment.");
        if (activeMembers.Any(m => m.AcceptedSchemeVersionId != currentVersion.Id))
            problems.Add("Some members accepted a different terms version than the current one.");
        if (activeMembers.Any(m => m.GroupId != group.Id) || positions.Any(p => p.GroupId != group.Id))
            problems.Add("Membership data belongs to another group.");

        var memberIds = activeMembers.Select(m => m.Id).ToHashSet();
        var expectedPositions = Enumerable.Range(1, activeMembers.Count).ToHashSet();
        if (positions.Count != activeMembers.Count
            || !positions.Select(p => p.Position).ToHashSet().SetEquals(expectedPositions)
            || !positions.Select(p => p.MembershipId).ToHashSet().SetEquals(memberIds))
            problems.Add("Every member needs exactly one unique payout position before the group can start.");

        if (rules.StartDate < today)
            problems.Add("The start date in the accepted terms has passed. Cancel the group or amend the terms with member approval.");

        if (problems.Count > 0)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["activation"] = problems.ToArray() });

        var schedule = RotatingSchemeCalculator.BuildSchedule(rules);
        var summary = RotatingSchemeCalculator.Summarize(rules);
        var recipientByPosition = positions.ToDictionary(p => p.Position, p => p.MembershipId);

        var rounds = new List<SchemeRound>();
        var obligations = new List<ContributionObligation>();
        var payouts = new List<Payout>();
        foreach (var plan in schedule)
        {
            var round = SchemeRound.Create(group.Id, plan.RoundNumber, plan.ContributionDueDate, plan.PayoutDate, now);
            rounds.Add(round);
            foreach (var m in activeMembers.OrderBy(m => m.JoinSequence))
                obligations.Add(ContributionObligation.Create(group.Id, m.Id, round.Id, rules.Contribution, plan.ContributionDueDate, now));
            payouts.Add(Payout.Create(group.Id, round.Id, recipientByPosition[plan.RoundNumber], summary.ExpectedPoolPerRound, plan.PayoutDate, now));
        }

        group.MarkStarted(now);
        return new ActivationResult(rounds, obligations, payouts);
    }
}
