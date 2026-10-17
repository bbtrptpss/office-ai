using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Organization;

namespace OfficeAI.Application.Organization;

public record DepartmentDto(Guid Id, string Code, string Name, string? Description, Guid? ParentId, int SortOrder, bool IsActive, uint Version);
public record SaveDepartmentRequest(string Code, string Name, string? Description, Guid? ParentId, int SortOrder, bool IsActive = true, uint? Version = null);

public record RoleDefinitionDto(
    Guid Id, string Code, string Title, Guid DepartmentId, OrgLevel Level, string? Description,
    List<string> Responsibilities, List<string> Skills, List<string> TaskTypes, string? SystemInstructions,
    bool RequiresHuman, bool IsActive, uint Version);
public record SaveRoleDefinitionRequest(
    string Code, string Title, Guid DepartmentId, OrgLevel Level, string? Description,
    List<string>? Responsibilities, List<string>? Skills, List<string>? TaskTypes, string? SystemInstructions,
    bool RequiresHuman = false, bool IsActive = true, uint? Version = null);

public record EmployeeDto(
    Guid Id, string DisplayName, EmployeeKind Kind, Guid? UserId, Guid DepartmentId, string DepartmentName,
    Guid RoleDefinitionId, string JobTitle, Guid? ManagerId, EmployeeStatus Status, uint Version);
public record SaveEmployeeRequest(
    string DisplayName, EmployeeKind Kind, Guid DepartmentId, Guid RoleDefinitionId, Guid? ManagerId,
    EmployeeStatus Status = EmployeeStatus.Available, Guid? UserId = null, uint? Version = null);

public record OrgChartNode(Guid Id, string DisplayName, EmployeeKind Kind, string JobTitle, string DepartmentName, EmployeeStatus Status, List<OrgChartNode> Reports);

public class OrganizationService(IAppDbContext db, IRealtimeNotifier notifier, TimeProvider clock)
{
    // ---------- Departments ----------

    public async Task<List<DepartmentDto>> ListDepartmentsAsync(CancellationToken ct) =>
        await db.Departments.AsNoTracking().OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.Description, d.ParentId, d.SortOrder, d.IsActive, d.Version))
            .ToListAsync(ct);

    public async Task<DepartmentDto> CreateDepartmentAsync(SaveDepartmentRequest req, CancellationToken ct)
    {
        Validate.Required(req.Code, "code");
        Validate.Required(req.Name, "name");
        if (req.ParentId is { } parentId) await EnsureExistsAsync(db.Departments, parentId, ct);

        var dept = new Department { Code = req.Code.Trim(), Name = req.Name.Trim() };
        Apply(dept, req);
        db.Departments.Add(dept);
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return ToDto(dept);
    }

    public async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, SaveDepartmentRequest req, CancellationToken ct)
    {
        Validate.Required(req.Code, "code");
        Validate.Required(req.Name, "name");
        var dept = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Department", id);
        if (req.Version is { } v) db.ExpectVersion(dept, v);

        if (req.ParentId is { } parentId)
        {
            await EnsureExistsAsync(db.Departments, parentId, ct);
            var parents = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.ParentId, ct);
            if (Graph.WouldCreateParentCycle(id, parentId, parents))
                throw new DomainException("department.cycle", "A department cannot be placed under itself or one of its sub-departments.");
        }

        dept.Code = req.Code.Trim();
        dept.Name = req.Name.Trim();
        Apply(dept, req);
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return ToDto(dept);
    }

    private static void Apply(Department d, SaveDepartmentRequest r)
    {
        d.Description = r.Description;
        d.ParentId = r.ParentId;
        d.SortOrder = r.SortOrder;
        d.IsActive = r.IsActive;
    }

    private static DepartmentDto ToDto(Department d) =>
        new(d.Id, d.Code, d.Name, d.Description, d.ParentId, d.SortOrder, d.IsActive, d.Version);

    // ---------- Role definitions ----------

    public async Task<List<RoleDefinitionDto>> ListRoleDefinitionsAsync(CancellationToken ct) =>
        (await db.RoleDefinitions.AsNoTracking().OrderBy(r => r.Level).ThenBy(r => r.Title).ToListAsync(ct))
            .Select(ToDto).ToList();

    public async Task<RoleDefinitionDto> CreateRoleDefinitionAsync(SaveRoleDefinitionRequest req, CancellationToken ct)
    {
        Validate.Required(req.Code, "code");
        Validate.Required(req.Title, "title");
        await EnsureExistsAsync(db.Departments, req.DepartmentId, ct);

        var role = new EmployeeRoleDefinition { Code = req.Code.Trim(), Title = req.Title.Trim() };
        Apply(role, req);
        db.RoleDefinitions.Add(role);
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return ToDto(role);
    }

    public async Task<RoleDefinitionDto> UpdateRoleDefinitionAsync(Guid id, SaveRoleDefinitionRequest req, CancellationToken ct)
    {
        Validate.Required(req.Code, "code");
        Validate.Required(req.Title, "title");
        var role = await db.RoleDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("RoleDefinition", id);
        if (req.Version is { } v) db.ExpectVersion(role, v);
        await EnsureExistsAsync(db.Departments, req.DepartmentId, ct);

        if (req.RequiresHuman && await db.Employees.AnyAsync(e => e.RoleDefinitionId == id && e.Kind == EmployeeKind.AI, ct))
            throw new DomainException("role.requires_human", "AI employees already hold this position; reassign them first.");

        role.Code = req.Code.Trim();
        role.Title = req.Title.Trim();
        Apply(role, req);
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return ToDto(role);
    }

    private static void Apply(EmployeeRoleDefinition r, SaveRoleDefinitionRequest q)
    {
        r.DepartmentId = q.DepartmentId;
        r.Level = q.Level;
        r.Description = q.Description;
        r.Responsibilities = q.Responsibilities ?? [];
        r.Skills = q.Skills ?? [];
        r.TaskTypes = q.TaskTypes ?? [];
        r.SystemInstructions = q.SystemInstructions;
        r.RequiresHuman = q.RequiresHuman;
        r.IsActive = q.IsActive;
    }

    private static RoleDefinitionDto ToDto(EmployeeRoleDefinition r) =>
        new(r.Id, r.Code, r.Title, r.DepartmentId, r.Level, r.Description, r.Responsibilities, r.Skills, r.TaskTypes,
            r.SystemInstructions, r.RequiresHuman, r.IsActive, r.Version);

    // ---------- Employees ----------

    public async Task<List<EmployeeDto>> ListEmployeesAsync(CancellationToken ct) =>
        await db.Employees.AsNoTracking().OrderBy(e => e.DisplayName)
            .Select(e => new EmployeeDto(e.Id, e.DisplayName, e.Kind, e.UserId, e.DepartmentId, e.Department!.Name,
                e.RoleDefinitionId, e.RoleDefinition!.Title, e.ManagerId, e.Status, e.Version))
            .ToListAsync(ct);

    public async Task<EmployeeDto> GetEmployeeAsync(Guid id, CancellationToken ct) =>
        await db.Employees.AsNoTracking().Where(e => e.Id == id)
            .Select(e => new EmployeeDto(e.Id, e.DisplayName, e.Kind, e.UserId, e.DepartmentId, e.Department!.Name,
                e.RoleDefinitionId, e.RoleDefinition!.Title, e.ManagerId, e.Status, e.Version))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Employee", id);

    public async Task<EmployeeDto> CreateEmployeeAsync(SaveEmployeeRequest req, CancellationToken ct)
    {
        var emp = new EmployeeProfile { DisplayName = "" };
        await ApplyAsync(emp, req, isNew: true, ct);
        db.Employees.Add(emp);
        await EnsureAgentDefaultsAsync(emp, ct);
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return await GetEmployeeAsync(emp.Id, ct);
    }

    public async Task<EmployeeDto> UpdateEmployeeAsync(Guid id, SaveEmployeeRequest req, CancellationToken ct)
    {
        var emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Employee", id);
        if (req.Version is { } v) db.ExpectVersion(emp, v);
        await ApplyAsync(emp, req, isNew: false, ct);
        await EnsureAgentDefaultsAsync(emp, ct);
        await db.SaveChangesAsync(ct);
        await notifier.OrganizationChangedAsync(ct);
        return await GetEmployeeAsync(emp.Id, ct);
    }

    private async Task ApplyAsync(EmployeeProfile emp, SaveEmployeeRequest req, bool isNew, CancellationToken ct)
    {
        Validate.Required(req.DisplayName, "displayName");
        if (!Enum.IsDefined(req.Kind)) throw new DomainException("validation", "kind is invalid.");
        if (!Enum.IsDefined(req.Status)) throw new DomainException("validation", "status is invalid.");
        await EnsureExistsAsync(db.Departments, req.DepartmentId, ct);
        var role = await db.RoleDefinitions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RoleDefinitionId, ct)
            ?? throw new NotFoundException("RoleDefinition", req.RoleDefinitionId);

        if (role.RequiresHuman && req.Kind != EmployeeKind.Human)
            throw new DomainException("role.requires_human", $"The position '{role.Title}' can only be held by a human.");
        if (req.Kind == EmployeeKind.Human && req.UserId is null)
            throw new DomainException("employee.user_required", "A human employee must be linked to a user account.");

        if (req.ManagerId is { } managerId)
        {
            await EnsureExistsAsync(db.Employees, managerId, ct);
            if (!isNew)
            {
                var managers = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.ManagerId, ct);
                if (Graph.WouldCreateParentCycle(emp.Id, managerId, managers))
                    throw new DomainException("employee.manager_cycle", "This manager would create a reporting loop.");
            }
        }

        emp.DisplayName = req.DisplayName.Trim();
        emp.Kind = req.Kind;
        emp.UserId = req.UserId;
        emp.DepartmentId = req.DepartmentId;
        emp.RoleDefinitionId = req.RoleDefinitionId;
        emp.ManagerId = req.ManagerId;
        emp.Status = req.Status;
    }

    /// <summary>Every AI employee gets a model configuration and the default tool grants.</summary>
    private async Task EnsureAgentDefaultsAsync(EmployeeProfile emp, CancellationToken ct)
    {
        if (emp.Kind != EmployeeKind.AI || await db.AgentConfigurations.AnyAsync(c => c.EmployeeId == emp.Id, ct)) return;
        var role = await db.RoleDefinitions.Where(r => r.Id == emp.RoleDefinitionId).Select(r => new { r.Level, r.Code }).FirstAsync(ct);
        db.AgentConfigurations.Add(AgentDefaults.ConfigFor(emp, role.Level));
        db.AgentToolPermissions.AddRange(AgentDefaults.GrantsFor(emp.Id, role.Level, clock.GetUtcNow(), role.Code));
    }

    public async Task<List<OrgChartNode>> GetOrgChartAsync(CancellationToken ct)
    {
        var all = await ListEmployeesAsync(ct);
        var byManager = all.ToLookup(e => e.ManagerId);
        List<OrgChartNode> Build(Guid? managerId) =>
            byManager[managerId]
                .OrderBy(e => e.JobTitle)
                .Select(e => new OrgChartNode(e.Id, e.DisplayName, e.Kind, e.JobTitle, e.DepartmentName, e.Status, Build(e.Id)))
                .ToList();
        return Build(null);
    }

    private static async Task EnsureExistsAsync<T>(DbSet<T> set, Guid id, CancellationToken ct) where T : Entity
    {
        if (!await set.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException(typeof(T).Name, id);
    }
}
