namespace AjoCircle.Domain.Audit;

/// <summary>Append-only record of sensitive actions (brief §11.4). Never updated or deleted by the application.</summary>
public sealed class AuditLog
{
    private AuditLog() { }
    public Guid Id { get; private set; }
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = "";
    public string EntityType { get; private set; } = "";
    public Guid EntityId { get; private set; }
    public Guid? GroupId { get; private set; }
    /// <summary>JSON. Must not contain secrets, tokens or unnecessary personal data.</summary>
    public string? Metadata { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditLog Create(Guid? actorId, string action, string entityType, Guid entityId, Guid? groupId, string? metadata, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now), ActorId = actorId, Action = action, EntityType = entityType,
            EntityId = entityId, GroupId = groupId, Metadata = metadata, CreatedAt = now,
        };
}
