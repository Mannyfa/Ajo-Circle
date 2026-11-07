using System.Text.Json;
using AjoCircle.Domain.Common;
using AjoCircle.Domain.Schemes;

namespace AjoCircle.Domain.Groups;

public enum SchemeType { RotatingAjo, FixedTermIndividual, FixedTermShared }

/// <summary>Brief §6.1 lifecycle. Transitions happen only through the domain commands below.</summary>
public enum GroupStatus { Draft, OpenForJoining, ReadyToStart, Active, Suspended, Completed, Cancelled }

public sealed class Group
{
    private Group() { } // EF

    public Guid Id { get; private set; }
    public Guid OrganizerId { get; private set; }
    public string Name { get; private set; } = "";
    public SchemeType SchemeType { get; private set; }
    public GroupStatus Status { get; private set; }
    public string Currency { get; private set; } = Currencies.Ngn;
    /// <summary>Editable rules while in Draft only. Frozen into a <see cref="SchemeVersion"/> on publish.</summary>
    public string? DraftRulesJson { get; private set; }
    public Guid? CurrentSchemeVersionId { get; private set; }
    public int NextJoinSequence { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? EnrolmentClosedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CancellationReason { get; private set; }
    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint RowVersion { get; private set; }

    public static Group CreateRotatingDraft(Guid organizerId, string name, RotatingSchemeRules rules, DateTimeOffset now)
    {
        var group = new Group
        {
            Id = Guid.CreateVersion7(now),
            OrganizerId = organizerId,
            SchemeType = SchemeType.RotatingAjo,
            Status = GroupStatus.Draft,
            CreatedAt = now,
        };
        group.SetName(name);
        group.SetDraftRules(rules);
        return group;
    }

    public bool IsFixedTerm => SchemeType is SchemeType.FixedTermIndividual or SchemeType.FixedTermShared;

    public static Group CreateFixedTermDraft(Guid organizerId, string name, FixedTermSchemeRules rules, DateTimeOffset now)
    {
        var group = new Group
        {
            Id = Guid.CreateVersion7(now),
            OrganizerId = organizerId,
            SchemeType = rules.Variant == SchemeVariant.Individual ? SchemeType.FixedTermIndividual : SchemeType.FixedTermShared,
            Status = GroupStatus.Draft,
            CreatedAt = now,
        };
        group.SetName(name);
        group.SetFixedTermDraftRules(rules);
        return group;
    }

    public void UpdateFixedTermDraft(string name, FixedTermSchemeRules rules)
    {
        EnsureStatus("group_not_draft", "Only draft groups can be edited. Published terms change only through an amendment.", GroupStatus.Draft);
        if (!IsFixedTerm) throw new DomainException("scheme_mismatch", "This group is not a fixed-term savings group.");
        SetName(name);
        SetFixedTermDraftRules(rules);
    }

    public FixedTermSchemeRules GetFixedTermDraftRules() =>
        SchemeJson.DeserializeFixedTerm(DraftRulesJson ?? throw new DomainException("rules_missing", "Group has no draft rules."));

    public void UpdateDraft(string name, RotatingSchemeRules rules)
    {
        if (IsFixedTerm) throw new DomainException("scheme_mismatch", "This group is a fixed-term savings group.");
        EnsureStatus("group_not_draft", "Only draft groups can be edited. Published terms change only through an amendment.", GroupStatus.Draft);
        SetName(name);
        SetDraftRules(rules);
    }

    public RotatingSchemeRules GetDraftRules() =>
        SchemeJson.Deserialize(DraftRulesJson ?? throw new DomainException("rules_missing", "Group has no draft rules."));

    /// <summary>Validates terms, freezes them into version 1 and opens the group for joining.</summary>
    public SchemeVersion Publish(Guid actorId, DateOnly today, DateTimeOffset now)
    {
        EnsureStatus("group_not_draft", "Only draft groups can be published.", GroupStatus.Draft);
        string json;
        if (IsFixedTerm)
        {
            var ft = GetFixedTermDraftRules();
            ft.Validate(today);
            json = SchemeJson.SerializeFixedTerm(ft);
        }
        else
        {
            var rules = GetDraftRules();
            rules.Validate(today);
            json = SchemeJson.Serialize(rules);
        }
        var version = SchemeVersion.Create(Id, 1, json, actorId, now);
        CurrentSchemeVersionId = version.Id;
        Status = GroupStatus.OpenForJoining;
        PublishedAt = now;
        return version;
    }

    /// <summary>Allocates a join sequence for a new member. Caller must hold a row lock on the group.</summary>
    public int AdmitMember(int activeMemberCount, int capacity, DateTimeOffset now)
    {
        EnsureStatus("group_not_open", "This group is not accepting new members.", GroupStatus.OpenForJoining);
        if (activeMemberCount >= capacity)
            throw new DomainConflictException("group_full", "This group is full.");
        var seq = NextJoinSequence++;
        if (activeMemberCount + 1 == capacity)
        {
            Status = GroupStatus.ReadyToStart; // enrolment closes automatically when full
            EnrolmentClosedAt = now;
        }
        return seq;
    }

    public void CloseEnrolment(int activeMemberCount, DateTimeOffset now)
    {
        EnsureStatus("group_not_open", "Enrolment can only be closed while the group is open for joining.", GroupStatus.OpenForJoining);
        if (activeMemberCount < RotatingSchemeRules.MinMembers)
            throw new DomainException("too_few_members", $"At least {RotatingSchemeRules.MinMembers} members are needed to close enrolment.");
        Status = GroupStatus.ReadyToStart;
        EnrolmentClosedAt = now;
    }

    public void MarkStarted(DateTimeOffset now)
    {
        EnsureStatus("group_not_ready", "Only groups that are ready to start can be started.", GroupStatus.ReadyToStart);
        Status = GroupStatus.Active;
        StartedAt = now;
    }

    /// <summary>
    /// Before the start any group can be cancelled. An active fixed-term group can be cancelled too, because the owner set
    /// the rule: everyone gets back what they paid (decision 2). An active rotating ajo can't — some members have already
    /// been paid out, and no rule for unwinding that has been approved (brief §6.1).
    /// </summary>
    public void Cancel(string reason, DateTimeOffset now)
    {
        if (!(Status is GroupStatus.Draft or GroupStatus.OpenForJoining or GroupStatus.ReadyToStart || (Status == GroupStatus.Active && IsFixedTerm)))
            throw new DomainConflictException("cancel_not_allowed", Status == GroupStatus.Active
                ? "An active rotating ajo can't be cancelled in the app yet: no approved rule exists for unwinding payouts already made."
                : "This group cannot be cancelled in its current state.");
        reason = (reason ?? "").Trim();
        if (reason.Length is < 5 or > 500)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["reason"] = ["Give a reason of 5–500 characters."] });
        Status = GroupStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason;
    }

    /// <summary>Ends a fixed-term group so the money can be released. Caller checks the term has run and no disputes are open.</summary>
    public void CompleteTerm(DateTimeOffset now)
    {
        EnsureStatus("group_not_active", "Only an active group can be completed.", GroupStatus.Active);
        if (!IsFixedTerm) throw new DomainException("scheme_mismatch", "Rotating groups complete differently; that isn't available yet.");
        Status = GroupStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>Member limit and contribution, whatever the scheme type.</summary>
    public SchemeBasics Basics(string? publishedRulesJson = null)
    {
        var json = publishedRulesJson ?? DraftRulesJson ?? throw new DomainException("rules_missing", "Group has no rules.");
        if (IsFixedTerm)
        {
            var f = SchemeJson.DeserializeFixedTerm(json);
            return new SchemeBasics(f.MemberCount, f.ContributionAmountMinor, f.Currency, f.Frequency, f.StartDate);
        }
        var r = SchemeJson.Deserialize(json);
        return new SchemeBasics(r.MemberCount, r.ContributionAmountMinor, r.Currency, r.Frequency, r.StartDate);
    }

    public bool IsOrganizer(Guid userId) => OrganizerId == userId;

    private void SetName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 3 or > 80)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["name"] = ["Name must be 3–80 characters."] });
        Name = name;
    }

    private void SetDraftRules(RotatingSchemeRules rules)
    {
        if (!Currencies.IsSupported(rules.Currency))
            throw new DomainValidationException(new Dictionary<string, string[]> { ["currency"] = ["Only NGN is supported."] });
        Currency = rules.Currency;
        DraftRulesJson = SchemeJson.Serialize(rules);
    }

    private void SetFixedTermDraftRules(FixedTermSchemeRules rules)
    {
        if (!Currencies.IsSupported(rules.Currency))
            throw new DomainValidationException(new Dictionary<string, string[]> { ["currency"] = ["Only NGN is supported."] });
        var expected = SchemeType == SchemeType.FixedTermIndividual ? SchemeVariant.Individual : SchemeVariant.Shared;
        if (rules.Variant != expected)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["variant"] = ["The savings type can't change after the group is created."] });
        Currency = rules.Currency;
        DraftRulesJson = SchemeJson.SerializeFixedTerm(rules with { Goal = string.IsNullOrWhiteSpace(rules.Goal) ? null : rules.Goal.Trim() });
    }

    private void EnsureStatus(string code, string message, params GroupStatus[] allowed)
    {
        if (!allowed.Contains(Status)) throw new DomainConflictException(code, message);
    }
}

public static class SchemeJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string Serialize(RotatingSchemeRules rules) => JsonSerializer.Serialize(rules, Options);

    public static RotatingSchemeRules Deserialize(string json) =>
        JsonSerializer.Deserialize<RotatingSchemeRules>(json, Options)
        ?? throw new DomainException("rules_invalid", "Stored scheme rules could not be read.");

    public static string SerializeFixedTerm(FixedTermSchemeRules rules) => JsonSerializer.Serialize(rules, Options);

    public static FixedTermSchemeRules DeserializeFixedTerm(string json) =>
        JsonSerializer.Deserialize<FixedTermSchemeRules>(json, Options)
        ?? throw new DomainException("rules_invalid", "Stored scheme rules could not be read.");
}

/// <summary>The parts of any scheme's terms that lists and capacity checks need.</summary>
public sealed record SchemeBasics(int MemberCount, long ContributionAmountMinor, string Currency, ContributionFrequency Frequency, DateOnly StartDate);

