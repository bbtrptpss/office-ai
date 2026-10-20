using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Integrations;

public enum IntegrationKind
{
    Webhook = 1,
}

/// <summary>
/// An outbound notification channel configured by a person. The target URL (often a secret, e.g. a Slack webhook)
/// is never stored: only the name of the environment variable that holds it, which must start with INTEGRATION_.
/// </summary>
public class IntegrationConfiguration : Entity, IAuditable
{
    public required string Name { get; set; }
    public IntegrationKind Kind { get; set; } = IntegrationKind.Webhook;
    public required string SecretEnvVar { get; set; }
    public List<string> Events { get; set; } = [];
    public bool IsEnabled { get; set; } = true;

    /// <summary>Everything that happened up to this moment has been considered for delivery.</summary>
    public DateTimeOffset Cursor { get; set; }
}

public enum DeliveryStatus
{
    Sent = 1,
    Failed = 2,
}

public class IntegrationDelivery
{
    public long Id { get; set; }
    public Guid IntegrationId { get; set; }
    public required string Event { get; set; }
    public required string Title { get; set; }
    public DeliveryStatus Status { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class IntegrationEvents
{
    public const string ApprovalRequested = "approval.requested";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowEscalated = "workflow.escalated";
    public const string RunFailed = "run.failed";
    public const string BudgetReached = "budget.reached";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { ApprovalRequested, WorkflowCompleted, WorkflowEscalated, RunFailed, BudgetReached };
    public const string EnvPrefix = "INTEGRATION_";
}

/// <summary>A person's own delivery choices. One row per user, created the first time they change something.</summary>
public class UserPreference
{
    public Guid UserId { get; set; }

    /// <summary>Email copies of personal notifications created from this moment on; null = off.</summary>
    public DateTimeOffset? EmailNotificationsSince { get; set; }

    /// <summary>SHA-256 of the secret in the personal calendar feed URL; the secret itself is shown once and never stored.</summary>
    public string? CalendarTokenHash { get; set; }
    public DateTimeOffset? CalendarTokenCreatedAt { get; set; }
}

public enum EmailStatus
{
    Sent = 1,
    Failed = 2,
}

/// <summary>An email copy of one personal notification. At most one row per notification; failures are retried a few times.</summary>
public class EmailDelivery
{
    public long Id { get; set; }
    public long NotificationId { get; set; }
    public Guid UserId { get; set; }
    public required string Subject { get; set; }
    public EmailStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
