using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Knowledge;

public enum KnowledgeKind
{
    Policy = 1,
    Standard = 2,
    Decision = 3,
    Lesson = 4,
    Template = 5,
    Reference = 6,
}

public enum KnowledgeStatus
{
    /// <summary>Written by an agent (or a person) and not yet checked. Never returned by agent search.</summary>
    Proposed = 1,
    Validated = 2,
    Archived = 3,
}

/// <summary>A piece of company or project knowledge. Only validated entries are authoritative.</summary>
public class KnowledgeEntry : Entity, IAuditable
{
    public KnowledgeKind Kind { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>Null = company-wide; otherwise visible only within the project.</summary>
    public Guid? ProjectId { get; set; }
    public KnowledgeStatus Status { get; set; } = KnowledgeStatus.Proposed;

    /// <summary>Where it came from, e.g. "task:…", "run:…", "adr:…", or a URL.</summary>
    public string? Source { get; set; }
    public Guid? AuthorEmployeeId { get; set; }
    public Guid? ValidatedByUserId { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
}

public enum DocumentKind
{
    Proposal = 1,
    StatementOfWork = 2,
    Specification = 3,
    ReleaseNotes = 4,
    CompletionReport = 5,
    MeetingMinutes = 6,
    Other = 7,
}

public enum DocumentStatus
{
    Draft = 1,
    Final = 2,
}

/// <summary>A versioned project document. Each write creates a new version; old versions are kept.</summary>
public class ProjectDocument : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public DocumentKind Kind { get; set; }
    public required string Title { get; set; }

    /// <summary>1, 2, 3… per (project, kind, title). Not called Version: that name is the row-version token.</summary>
    public int Revision { get; set; }
    public required string Content { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public Guid? AuthorEmployeeId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? RunId { get; set; }
    public bool IsSimulated { get; set; }
}
