namespace OfficeAI.Domain.Security;

/// <summary>Permission codes in "resource.action" form. Checked server-side on every endpoint.</summary>
public static class Permissions
{
    public const string OrgRead = "org.read";
    public const string OrgManage = "org.manage";
    public const string ProjectRead = "project.read";
    public const string ProjectManage = "project.manage";
    public const string TaskRead = "task.read";
    public const string TaskManage = "task.manage";
    public const string TaskAssign = "task.assign";
    public const string TaskUpdateStatus = "task.update_status";
    public const string TaskApprove = "task.approve";
    public const string AuditRead = "audit.read";
    public const string SecurityManage = "security.manage";
    public const string AgentRead = "agent.read";
    public const string AgentRun = "agent.run";
    public const string AgentManage = "agent.manage";
    public const string FinanceRead = "finance.read";
    public const string FinanceManage = "finance.manage";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [OrgRead] = "View departments, positions and employees",
        [OrgManage] = "Create and change departments, positions and employees",
        [ProjectRead] = "View projects and milestones",
        [ProjectManage] = "Create and change projects and milestones",
        [TaskRead] = "View tasks",
        [TaskManage] = "Create and edit tasks and dependencies",
        [TaskAssign] = "Assign tasks to employees",
        [TaskUpdateStatus] = "Move tasks through their workflow",
        [TaskApprove] = "Mark reviewed tasks as Done",
        [AuditRead] = "View the audit log",
        [SecurityManage] = "Change roles and permissions",
        [AgentRead] = "View AI runs, transcripts, usage and deliverables",
        [AgentRun] = "Start and cancel AI employee runs",
        [AgentManage] = "Configure AI models, tools, budgets and prices",
        [FinanceRead] = "View finance and performance reports",
        [FinanceManage] = "Set company, department and project budgets",
    };

    /// <summary>Permissions an AI identity can never use, whatever roles it holds.</summary>
    public static readonly IReadOnlySet<string> HumanOnly = new HashSet<string> { SecurityManage, TaskApprove, AgentManage, FinanceManage };
}

public static class SystemRoles
{
    public const string Ceo = "CEO";
    public const string Manager = "Manager";
    public const string Member = "Member";
    public const string Auditor = "Auditor";
    public const string AiAgent = "AIAgent";

    public static readonly IReadOnlyDictionary<string, string[]> DefaultPermissions = new Dictionary<string, string[]>
    {
        [Ceo] = [.. Permissions.All.Keys],
        [Manager] =
        [
            Permissions.OrgRead, Permissions.ProjectRead, Permissions.ProjectManage, Permissions.TaskRead,
            Permissions.TaskManage, Permissions.TaskAssign, Permissions.TaskUpdateStatus, Permissions.TaskApprove,
            Permissions.AgentRead, Permissions.AgentRun, Permissions.FinanceRead,
        ],
        [Member] = [Permissions.OrgRead, Permissions.ProjectRead, Permissions.TaskRead, Permissions.TaskUpdateStatus, Permissions.AgentRead],
        [Auditor] = [Permissions.OrgRead, Permissions.ProjectRead, Permissions.TaskRead, Permissions.AuditRead, Permissions.AgentRead, Permissions.FinanceRead],
        [AiAgent] = [Permissions.OrgRead, Permissions.ProjectRead, Permissions.TaskRead, Permissions.TaskUpdateStatus],
    };
}
