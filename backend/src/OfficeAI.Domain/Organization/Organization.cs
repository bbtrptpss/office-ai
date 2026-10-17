using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Organization;

public class Department : Entity, IAuditable
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public Department? Parent { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A reusable job position (e.g. "Backend Developer"). Many employees can share one.</summary>
public class EmployeeRoleDefinition : Entity, IAuditable
{
    public required string Code { get; set; }
    public required string Title { get; set; }
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
    public OrgLevel Level { get; set; }
    public string? Description { get; set; }
    public List<string> Responsibilities { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<string> TaskTypes { get; set; } = [];
    public string? SystemInstructions { get; set; }

    /// <summary>Position may only be held by a human (e.g. CEO).</summary>
    public bool RequiresHuman { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum OrgLevel
{
    Executive = 1,
    Head = 2,
    Senior = 3,
    Staff = 4,
}

public enum EmployeeKind
{
    Human = 1,
    AI = 2,
}

public enum EmployeeStatus
{
    Available = 1,
    Working = 2,
    WaitingForApproval = 3,
    Blocked = 4,
    Failed = 5,
    Inactive = 6,
}

/// <summary>A member of the organization: a human or an AI employee.</summary>
public class EmployeeProfile : Entity, IAuditable
{
    public required string DisplayName { get; set; }
    public EmployeeKind Kind { get; set; }

    /// <summary>Login account. Required for humans; for AI employees, the service account used by the runtime (Phase 2).</summary>
    public Guid? UserId { get; set; }
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid RoleDefinitionId { get; set; }
    public EmployeeRoleDefinition? RoleDefinition { get; set; }
    public Guid? ManagerId { get; set; }
    public EmployeeProfile? Manager { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Available;
}
