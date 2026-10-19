using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Delivery;

public enum ReleaseStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Deployed = 4,
    RolledBack = 5,
    Cancelled = 6,
}

/// <summary>
/// A release candidate (Master Prompt §7 stage 7): release notes, deployment instructions and a rollback plan, bound to the exact
/// commit and passing test run it was checked against. Production deployment needs a person's approval; the deployment itself
/// happens outside the system and people record its outcome.
/// </summary>
public class Release : Entity, IAuditable
{
    public Guid ProjectId { get; set; }

    /// <summary>Version label, unique per project, e.g. 1.2.0.</summary>
    public required string Tag { get; set; }
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public string? DeploymentPlan { get; set; }
    public string? RollbackPlan { get; set; }

    /// <summary>Known issues the team accepts for this release, written by people or agents.</summary>
    public string? KnownIssues { get; set; }
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Draft;

    // Snapshot taken when the release is submitted for approval.
    public Guid? RepositoryId { get; set; }
    public string? CommitSha { get; set; }
    public Guid? TestRunId { get; set; }

    /// <summary>Unresolved issues found by the readiness check at submission (open defects, blocked tasks, open merge requests).</summary>
    public List<string> UnresolvedIssues { get; set; } = [];
    public Guid? ApprovalRequestId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? DeployedAt { get; set; }
    public DateTimeOffset? RolledBackAt { get; set; }
    public string? OutcomeNote { get; set; }

    public Guid? AuthorEmployeeId { get; set; }
    public Guid? RunId { get; set; }
}
