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

    public void UpdateDraft(string name, RotatingSchemeRules rules)
    {
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
        var rules = GetDraftRules();
        rules.Validate(today);
        var version = SchemeVersion.Create(Id, 1, SchemeJson.Serialize(rules), actorId, now);
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

    public void Cancel(string reason, DateTimeOffset now)
    {
        // Active/Suspended cancellation needs approved rules (brief §6.1) — not available in this release.
        EnsureStatus("cancel_not_allowed", "This group cannot be cancelled in its current state.",
            GroupStatus.Draft, GroupStatus.OpenForJoining, GroupStatus.ReadyToStart);
        reason = (reason ?? "").Trim();
        if (reason.Length is < 5 or > 500)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["reason"] = ["Give a reason of 5–500 characters."] });
        Status = GroupStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason;
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
}
