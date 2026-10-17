namespace OfficeAI.Domain.Audit;

/// <summary>Append-only record of a change or security-relevant event. UPDATE/DELETE are blocked by a database trigger.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? CorrelationId { get; set; }
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LockedOut = "LockedOut";
    public const string AccessDenied = "AccessDenied";
    public const string PermissionChangeDenied = "PermissionChangeDenied";
}
