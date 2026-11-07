using AjoCircle.Domain.Common;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.Domain.Groups;

/// <summary>
/// Starting a fixed-term group is a domain command (brief §6.1). Unlike a rotating ajo it can start with fewer members than
/// the limit, because each member's commitment (contribution × periods) doesn't depend on how many others joined.
/// </summary>
public static class FixedTermActivation
{
    public static ActivationResult Activate(Group group, SchemeVersion currentVersion, IReadOnlyCollection<GroupMembership> activeMembers,
        IReadOnlyDictionary<Guid, int> shares, DateOnly today, DateTimeOffset now)
    {
        if (group.Status != GroupStatus.ReadyToStart)
            throw new DomainConflictException("group_not_ready", "Only groups that are ready to start can be started.");
        if (!group.IsFixedTerm || currentVersion.GroupId != group.Id || group.CurrentSchemeVersionId != currentVersion.Id)
            throw new DomainException("scheme_mismatch", "The group's current scheme version could not be confirmed.");

        var rules = SchemeJson.DeserializeFixedTerm(currentVersion.RulesJson);
        var problems = new List<string>();
        if (activeMembers.Count < FixedTermSchemeRules.MinMembers)
            problems.Add($"At least {FixedTermSchemeRules.MinMembers} members are needed to start.");
        if (activeMembers.Count > rules.MemberCount)
            problems.Add($"The terms allow at most {rules.MemberCount} members.");
        if (activeMembers.Any(m => m.AcceptedSchemeVersionId != currentVersion.Id))
            problems.Add("Some members accepted a different terms version than the current one.");
        if (activeMembers.Any(m => m.GroupId != group.Id))
            problems.Add("Membership data belongs to another group.");
        if (rules.Allocation == SavingsAllocation.OrganizerAssigned)
        {
            try { AllocationShare.Validate(activeMembers.Select(m => m.Id).ToList(), shares); }
            catch (DomainValidationException) { problems.Add("Assign every member's share (adding up to 100%) on the Members tab before starting."); }
        }
        if (rules.StartDate < today)
            problems.Add("The start date in the accepted terms has passed. Cancel the group or amend the terms with member approval.");
        if (problems.Count > 0)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["activation"] = problems.ToArray() });

        var rounds = new List<SchemeRound>();
        var obligations = new List<ContributionObligation>();
        foreach (var period in FixedTermCalculator.BuildSchedule(rules))
        {
            // A fixed-term period has no payout of its own; the "payout date" column holds the due date.
            var round = SchemeRound.Create(group.Id, period.Number, period.DueDate, period.DueDate, now);
            rounds.Add(round);
            foreach (var m in activeMembers.OrderBy(m => m.JoinSequence))
                obligations.Add(ContributionObligation.Create(group.Id, m.Id, round.Id, rules.Contribution, period.DueDate, now));
        }
        group.MarkStarted(now);
        return new ActivationResult(rounds, obligations, []);
    }
}

/// <summary>What was recorded as paid by one member (organizer records still standing, confirmed or not).</summary>
public sealed record MemberPaid(Guid MembershipId, int JoinSequence, long RecordedMinor);

/// <summary>How much the organizer owes back to each member when a fixed-term group ends (brief §7.3; owner decisions 1–2).</summary>
public static class FixedTermSettlement
{
    /// <summary>
    /// Splits the recorded total among the members still in the group, per the accepted allocation rule. Members who
    /// left early were already refunded what they paid and take no part. Amounts are exact to the kobo and add up to
    /// the recorded total.
    /// </summary>
    public static IReadOnlyList<(Guid MembershipId, long AmountMinor)> Release(FixedTermSchemeRules rules, IReadOnlyList<MemberPaid> members,
        IReadOnlyDictionary<Guid, int> shares)
    {
        var ordered = members.OrderBy(m => m.JoinSequence).ToList();
        var total = ordered.Sum(m => m.RecordedMinor);
        IReadOnlyList<long> weights = rules.Allocation switch
        {
            SavingsAllocation.OwnContributions or SavingsAllocation.Proportional => ordered.Select(m => m.RecordedMinor).ToList(),
            SavingsAllocation.Equal => ordered.Select(_ => 1L).ToList(),
            SavingsAllocation.OrganizerAssigned => ordered.Select(m => (long)shares.GetValueOrDefault(m.MembershipId)).ToList(),
            _ => throw new DomainException("allocation_unknown", "Unknown allocation rule."),
        };
        if (rules.Allocation == SavingsAllocation.OrganizerAssigned && weights.Sum() == 0)
            throw new DomainException("shares_missing", "Member shares are missing, so the release can't be calculated.");
        var parts = Allocator.Split(total, weights);
        return ordered.Select((m, i) => (m.MembershipId, parts[i])).ToList();
    }

    /// <summary>Early exit or cancellation: a member gets back exactly what was recorded for them.</summary>
    public static long Refund(MemberPaid member) => member.RecordedMinor;
}
