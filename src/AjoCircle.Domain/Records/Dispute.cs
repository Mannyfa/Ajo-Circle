using System.Security.Cryptography;
using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Records;

public enum DisputeCategory { DidNotHappen, WrongAmount, WrongDate, Other }
/// <summary>Open → (after the response window) Escalated to operations → Resolved.</summary>
public enum DisputeStatus { Open, Escalated, Resolved }

/// <summary>
/// An operations investigator decides the outcome only (owner decision 5): the record stands, or the organizer must
/// correct it. Investigators never edit records themselves.
/// </summary>
public enum OperationsDecision { RecordUpheld, CorrectionRequired }

/// <summary>
/// How a dispute closed. The organizer can't close a dispute by overruling the member: they either correct or reverse
/// the record, or the member withdraws. Operations review is a later slice (brief §4.7).
/// </summary>
public enum DisputeResolution { WithdrawnByMember, RecordCorrected, RecordReversed, UpheldByOperations }

public sealed class Dispute
{
    public const int MinDescription = 10;
    public const int MaxDescription = 1000;

    private Dispute() { }
    public Guid Id { get; private set; }
    /// <summary>Human-friendly reference people can quote, e.g. DSP-7K3M9QX2.</summary>
    public string Reference { get; private set; } = "";
    public Guid GroupId { get; private set; }
    public RecordKind RecordType { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid RaisedBy { get; private set; }
    public DisputeCategory Category { get; private set; }
    public string Description { get; private set; } = "";
    public DateTimeOffset RaisedAt { get; private set; }

    public DisputeStatus Status { get; private set; }
    public DisputeResolution? Resolution { get; private set; }
    public string? ResolutionNote { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? CorrectiveRecordId { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }
    public Guid? EscalatedBy { get; private set; }
    public OperationsDecision? Decision { get; private set; }
    public string? DecisionNote { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    public bool IsUnresolved => Status != DisputeStatus.Resolved;

    /// <summary>When either party may hand the dispute to operations (owner decision 6).</summary>
    public DateTimeOffset EscalationAllowedFrom(TimeSpan responseWindow) => RaisedAt + responseWindow;

    /// <summary>The member who raised it or the organizer escalates once the organizer has had time to respond.</summary>
    public void Escalate(Guid actorId, bool actorIsParty, TimeSpan responseWindow, DateTimeOffset now)
    {
        if (!actorIsParty) throw new DomainException("not_a_party", "Only the member who raised the dispute or the organizer can escalate it.");
        if (Status == DisputeStatus.Escalated) throw new DomainConflictException("dispute_escalated", "This dispute is already with the operations team.");
        if (Status == DisputeStatus.Resolved) throw new DomainConflictException("dispute_resolved", "This dispute is already resolved.");
        if (now < EscalationAllowedFrom(responseWindow))
            throw new DomainConflictException("escalation_too_early",
                $"The organizer has until {EscalationAllowedFrom(responseWindow):d MMM yyyy} to respond. You can escalate after that.");
        Status = DisputeStatus.Escalated;
        EscalatedAt = now;
        EscalatedBy = actorId;
    }

    /// <summary>
    /// Records the investigator's finding. Upholding resolves the dispute; requiring a correction leaves it with the
    /// organizer, and it resolves when they correct or reverse the record.
    /// </summary>
    public void Decide(Guid investigatorId, OperationsDecision decision, string? note, ExternalRecord record, DateTimeOffset now)
    {
        if (Status != DisputeStatus.Escalated) throw new DomainConflictException("dispute_not_escalated", "Only escalated disputes are decided by operations.");
        if (Decision is not null) throw new DomainConflictException("dispute_decided", "A decision has already been recorded.");
        if (!Enum.IsDefined(decision)) throw new DomainValidationException(new Dictionary<string, string[]> { ["decision"] = ["Choose a decision."] });
        var n = (note ?? "").Trim();
        if (n.Length is < 10 or > 1000)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["note"] = ["Explain the finding to both parties in 10–1000 characters."] });
        Decision = decision;
        DecisionNote = n;
        DecidedBy = investigatorId;
        DecidedAt = now;
        if (decision == OperationsDecision.RecordUpheld)
        {
            record.RestoreAfterUpheld();
            Resolve(investigatorId, DisputeResolution.UpheldByOperations, n, null, now);
        }
    }
    /// <summary>Opens a dispute and moves the record to Disputed. Caller checks the raiser is the record's subject.</summary>
    public static Dispute Open(ExternalRecord record, RecordKind kind, Guid raisedBy, DisputeCategory category, string? description, DateTimeOffset now,
        bool previouslyUpheld = false)
    {
        if (previouslyUpheld)
            throw new DomainConflictException("record_upheld", "Operations already investigated this record and upheld it, so it can't be disputed again.");
        var errors = new Dictionary<string, List<string>>();
        var text = description?.Trim() ?? "";
        if (!Enum.IsDefined(category)) errors["category"] = ["Choose what is wrong."];
        if (text.Length is < MinDescription or > MaxDescription) errors["description"] = [$"Describe the problem in {MinDescription}–{MaxDescription} characters."];
        DomainValidationException.ThrowIfAny(errors);

        record.MarkDisputed(raisedBy);
        return new Dispute
        {
            Id = Guid.CreateVersion7(now), Reference = NewReference(), GroupId = record.GroupId, RecordType = kind, RecordId = record.Id,
            RaisedBy = raisedBy, Category = category, Description = text, RaisedAt = now, Status = DisputeStatus.Open,
        };
    }

    public void Resolve(Guid actorId, DisputeResolution resolution, string? note, Guid? correctiveRecordId, DateTimeOffset now)
    {
        if (Status == DisputeStatus.Resolved)
            throw new DomainConflictException("dispute_resolved", "This dispute is already resolved.");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n?.Length > RecordRules.MaxReasonLength)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["note"] = [$"Keep the note under {RecordRules.MaxReasonLength} characters."] });
        Status = DisputeStatus.Resolved;
        Resolution = resolution;
        ResolutionNote = n;
        ResolvedBy = actorId;
        ResolvedAt = now;
        CorrectiveRecordId = correctiveRecordId;
    }

    /// <summary>8 characters from an alphabet without look-alikes (no 0/O, 1/I/L).</summary>
    private static string NewReference()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        return "DSP-" + string.Create(8, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }
}

/// <summary>A message on a dispute thread. Append-only.</summary>
public sealed class DisputeMessage
{
    public const int MaxLength = 1000;

    private DisputeMessage() { }
    public Guid Id { get; private set; }
    public Guid DisputeId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static DisputeMessage Create(Dispute dispute, Guid authorId, string? body, DateTimeOffset now)
    {
        if (dispute.Status == DisputeStatus.Resolved)
            throw new DomainConflictException("dispute_resolved", "This dispute is resolved. Raise a new one if something is still wrong.");
        var text = body?.Trim() ?? "";
        if (text.Length is < 1 or > MaxLength)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["body"] = [$"Write a message of 1–{MaxLength} characters."] });
        return new DisputeMessage { Id = Guid.CreateVersion7(now), DisputeId = dispute.Id, AuthorId = authorId, Body = text, CreatedAt = now };
    }
}
