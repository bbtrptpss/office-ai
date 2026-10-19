using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Security;

/// <summary>
/// How long bulky operational data is kept. Null keeps it forever, which is the default: nothing is removed until a person
/// sets a period. Money (costs, usage), documents, knowledge, estimates and the append-only audit log are never removed.
/// </summary>
public class RetentionPolicy : Entity, IAuditable
{
    public const int MinDays = 30;
    public const int MaxDays = 3650;

    /// <summary>AI run transcripts (steps, saved conversation) and tool outputs of runs finished longer ago.</summary>
    public int? RunTranscriptDays { get; set; }

    /// <summary>Test run console output; pass/fail results stay.</summary>
    public int? TestOutputDays { get; set; }
    public int? NotificationDays { get; set; }

    /// <summary>Webhook delivery log rows.</summary>
    public int? IntegrationDeliveryDays { get; set; }

    /// <summary>Chat message text; the message shows as removed.</summary>
    public int? ChatMessageDays { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>JSON counts from the last clean-up.</summary>
    public string? LastResultJson { get; set; }
}
