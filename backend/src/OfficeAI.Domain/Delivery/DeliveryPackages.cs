using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Delivery;

/// <summary>Draft → Delivered (a person sent it to the customer) → Accepted, or ChangesRequested, which allows a new package.</summary>
public enum DeliveryPackageStatus
{
    Draft = 1,
    Delivered = 2,
    Accepted = 3,
    ChangesRequested = 4,
}

/// <summary>
/// What is handed to the customer (Master Prompt §7 stage 8): a deployed release, the exact document versions provided, the test
/// evidence of that release and handover notes. The system never sends it; people record delivery and the customer's response.
/// </summary>
public class DeliveryPackage : Entity, IAuditable
{
    public Guid ProjectId { get; set; }

    /// <summary>Per project: PKG-001, PKG-002…</summary>
    public required string Code { get; set; }
    public required string Title { get; set; }
    public Guid ReleaseId { get; set; }

    /// <summary>Document rows are versions, so these ids pin the exact revisions handed over.</summary>
    public List<Guid> DocumentIds { get; set; } = [];

    /// <summary>How to access, operate and support the system. Never secrets: those go to the customer through a secure channel.</summary>
    public string? HandoverNotes { get; set; }
    public DeliveryPackageStatus Status { get; set; } = DeliveryPackageStatus.Draft;
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? DeliveryNote { get; set; }
    public string? CustomerFeedback { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>The task created from the customer's change request.</summary>
    public Guid? ChangeTaskId { get; set; }
}
