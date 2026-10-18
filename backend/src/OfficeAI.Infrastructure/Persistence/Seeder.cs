using OfficeAI.Application.Communication;
using OfficeAI.Domain.Communication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Workflows;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Identity;

namespace OfficeAI.Infrastructure.Persistence;

public class SeedOptions
{
    public string CeoEmail { get; set; } = "";
    public string CeoPassword { get; set; } = "";
    public string CeoName { get; set; } = "CEO";
}

/// <summary>Creates permissions, system roles, the CEO account and the default Software House organization. Idempotent.</summary>
public class Seeder(AppDbContext db, UserManager<AppUser> users, RoleManager<AppRole> roles, IOptions<SeedOptions> options, ILogger<Seeder> log)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedPermissionsAsync(ct);
        var ceoUser = await SeedCeoAsync();
        if (ceoUser is not null && !await db.Departments.AnyAsync(ct))
            await SeedOrganizationAsync(ceoUser, ct);
        await SeedAiAsync(ct);
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var introduced = new List<string>();
        foreach (var (code, description) in Permissions.All)
            if (!await db.Permissions.AnyAsync(p => p.Code == code, ct))
            {
                db.Permissions.Add(new Permission { Code = code, Description = description });
                introduced.Add(code);
            }
        await db.SaveChangesAsync(ct);

        var permIds = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        foreach (var (roleName, perms) in SystemRoles.DefaultPermissions)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new AppRole { Name = roleName };
                Check(await roles.CreateAsync(role));
                foreach (var p in perms) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permIds[p] });
            }
            else
            {
                // Existing roles keep the admin's choices; only permissions new in this release get their default grants.
                foreach (var p in perms.Intersect(introduced))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permIds[p] });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAiAsync(CancellationToken ct)
    {
        foreach (var model in AgentDefaults.Models())
            if (!await db.AiModels.AnyAsync(m => m.Provider == model.Provider && m.ModelId == model.ModelId, ct))
                db.AiModels.Add(model);

        var unconfigured = await db.Employees
            .Where(e => e.Kind == EmployeeKind.AI && !db.AgentConfigurations.Any(c => c.EmployeeId == e.Id))
            .Select(e => new { Employee = e, e.RoleDefinition!.Level, e.RoleDefinition.Code })
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var x in unconfigured)
        {
            db.AgentConfigurations.Add(AgentDefaults.ConfigFor(x.Employee, x.Level));
            db.AgentToolPermissions.AddRange(AgentDefaults.GrantsFor(x.Employee.Id, x.Level, now, x.Code));
        }
        await db.SaveChangesAsync(ct);

        if (!await db.WorkflowDefinitions.AnyAsync(d => d.Code == ObjectiveService.DefaultDefinition, ct))
            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Code = ObjectiveService.DefaultDefinition, Name = "Objective delivery",
                Description = "Plan → CEO approves plan → agents work in parallel → AI review → CEO approves each result.",
            });

        // Phase 3 tools for AI employees configured before they existed (applied once; later changes by people are kept).
        const string marker = "phase3-tool-grants";
        if (!await db.SeedMarkers.AnyAsync(m => m.Key == marker, ct))
        {
            var employees = await db.AgentConfigurations.Join(db.Employees, c => c.EmployeeId, e => e.Id, (c, e) => new { e.Id, e.RoleDefinition!.Level }).ToListAsync(ct);
            var existing = (await db.AgentToolPermissions.Select(p => new { p.EmployeeId, p.ToolId }).ToListAsync(ct)).ToHashSet();
            foreach (var e in employees)
                foreach (var tool in new[] { ReviewSubmitTool.ToolId, PlanSubmitTool.ToolId, DelegateTaskTool.ToolId }.Intersect(AgentDefaults.ToolsFor(e.Level)))
                    if (!existing.Contains(new { EmployeeId = e.Id, ToolId = tool }))
                        db.AgentToolPermissions.Add(new AgentToolPermission { EmployeeId = e.Id, ToolId = tool, GrantedAt = now });
            db.SeedMarkers.Add(new SeedMarker { Key = marker, AppliedAt = now });
        }
        await db.SaveChangesAsync(ct);
        await SeedDeliveryAsync(now, ct);
        await GrantNewToolsOnceAsync("phase5-tool-grants",
            [OfficeAI.Application.Knowledge.SearchKnowledgeTool.ToolId, OfficeAI.Application.Knowledge.ProposeKnowledgeTool.ToolId, OfficeAI.Application.Knowledge.WriteDocumentTool.ToolId], now, ct);
        await GrantNewToolsOnceAsync("phase7-tool-grants",
            [.. AgentDefaults.CommsTools, OfficeAI.Application.Communication.DraftMinutesTool.ToolId, OfficeAI.Application.Communication.DraftAnnouncementTool.ToolId], now, ct);
        await GrantRoleToolOnceAsync("v0.10.4-operations-planner", "OPERATIONS", PlanSubmitTool.ToolId, now, ct);

        if (!await db.ChatChannels.AnyAsync(c => c.Kind == ChannelKind.Company && c.Name == ChatService.GeneralChannel, ct))
        {
            db.ChatChannels.Add(new ChatChannel { Kind = ChannelKind.Company, Name = ChatService.GeneralChannel, Description = "Company-wide conversation" });
            await db.SaveChangesAsync(ct);
        }
        if (!await db.ChatChannels.AnyAsync(c => c.Kind == ChannelKind.Company && c.Name == ChatService.AssistantChannel, ct))
        {
            db.ChatChannels.Add(new ChatChannel { Kind = ChannelKind.Company, Name = ChatService.AssistantChannel, Description = "Questions to the AI assistants from the dashboard" });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Gives one tool to the existing AI employees of one position (once; other positions and later changes by people are untouched).</summary>
    private async Task GrantRoleToolOnceAsync(string marker, string roleCode, string tool, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == marker, ct)) return;
        var ids = await db.AgentConfigurations.Join(db.Employees, c => c.EmployeeId, e => e.Id, (c, e) => new { e.Id, e.RoleDefinition!.Code })
            .Where(e => e.Code == roleCode).Select(e => e.Id).ToListAsync(ct);
        var has = await db.AgentToolPermissions.Where(p => p.ToolId == tool && ids.Contains(p.EmployeeId)).Select(p => p.EmployeeId).ToListAsync(ct);
        foreach (var id in ids.Except(has)) db.AgentToolPermissions.Add(new AgentToolPermission { EmployeeId = id, ToolId = tool, GrantedAt = now });
        db.SeedMarkers.Add(new SeedMarker { Key = marker, AppliedAt = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Gives existing AI employees the default grants of tools introduced in a release (once; later changes by people are kept).</summary>
    private async Task GrantNewToolsOnceAsync(string marker, string[] tools, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.SeedMarkers.AnyAsync(m => m.Key == marker, ct)) return;
        var employees = await db.AgentConfigurations.Join(db.Employees, c => c.EmployeeId, e => e.Id, (c, e) => new { e.Id, e.RoleDefinition!.Level, e.RoleDefinition.Code }).ToListAsync(ct);
        var existing = (await db.AgentToolPermissions.Select(p => new { p.EmployeeId, p.ToolId }).ToListAsync(ct)).ToHashSet();
        foreach (var e in employees)
            foreach (var tool in tools.Intersect(AgentDefaults.ToolsFor(e.Level, e.Code)))
                if (!existing.Contains(new { EmployeeId = e.Id, ToolId = tool }))
                    db.AgentToolPermissions.Add(new AgentToolPermission { EmployeeId = e.Id, ToolId = tool, GrantedAt = now });
        db.SeedMarkers.Add(new SeedMarker { Key = marker, AppliedAt = now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Phase 4: company settings, a starting rate card and the specialist tools (each applied once).</summary>
    private async Task SeedDeliveryAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.CompanySettings.AnyAsync(ct)) db.CompanySettings.Add(new CompanySettings());
        const string rates = "phase4-rate-card";
        if (!await db.SeedMarkers.AnyAsync(m => m.Key == rates, ct))
        {
            foreach (var role in await db.RoleDefinitions.Where(r => !db.RoleRates.Any(x => x.RoleDefinitionId == r.Id)).ToListAsync(ct))
                db.RoleRates.Add(new RoleRate { RoleDefinitionId = role.Id, HourlyRateThb = AgentDefaults.DefaultRateThb(role.Level) });
            db.SeedMarkers.Add(new SeedMarker { Key = rates, AppliedAt = now });
        }
        const string tools = "phase4-tool-grants";
        if (!await db.SeedMarkers.AnyAsync(m => m.Key == tools, ct))
        {
            var employees = await db.AgentConfigurations.Join(db.Employees, c => c.EmployeeId, e => e.Id, (c, e) => new { e.Id, e.RoleDefinition!.Level, e.RoleDefinition.Code }).ToListAsync(ct);
            var existing = (await db.AgentToolPermissions.Select(p => new { p.EmployeeId, p.ToolId }).ToListAsync(ct)).ToHashSet();
            var phase4 = new[] { ReadRequirementsTool.ToolId, AddRequirementTool.ToolId, ProposeDecisionTool.ToolId, SubmitEstimateTool.ToolId }
                .Concat(AgentDefaults.CodeReadTools).Concat(AgentDefaults.CodeWriteTools).ToArray();
            foreach (var e in employees)
                foreach (var tool in phase4.Intersect(AgentDefaults.ToolsFor(e.Level, e.Code)))
                    if (!existing.Contains(new { EmployeeId = e.Id, ToolId = tool }))
                        db.AgentToolPermissions.Add(new AgentToolPermission { EmployeeId = e.Id, ToolId = tool, GrantedAt = now });
            db.SeedMarkers.Add(new SeedMarker { Key = tools, AppliedAt = now });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<AppUser?> SeedCeoAsync()
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.CeoEmail) || string.IsNullOrWhiteSpace(o.CeoPassword))
        {
            log.LogWarning("Seed:CeoEmail / Seed:CeoPassword not configured; skipping CEO and organization seed.");
            return null;
        }

        var user = await users.FindByEmailAsync(o.CeoEmail);
        if (user is not null) return user;

        user = new AppUser { UserName = o.CeoEmail, Email = o.CeoEmail, EmailConfirmed = true, DisplayName = o.CeoName };
        Check(await users.CreateAsync(user, o.CeoPassword));
        Check(await users.AddToRoleAsync(user, SystemRoles.Ceo));
        log.LogInformation("Created CEO account {Email}", o.CeoEmail);
        return user;
    }

    private record Pos(string Code, string Title, OrgLevel Level, string? ManagerCode, string[] Skills, bool Human = false);
    private record Dept(string Code, string Name, string Description, Pos[] Positions);

    private static readonly Dept[] Organization =
    [
        new("EXEC", "ฝ่ายบริหาร", "Executive Office", [
            new("CEO", "CEO", OrgLevel.Executive, null, ["strategy", "approval"], Human: true),
            new("COO", "COO", OrgLevel.Executive, "CEO", ["operations", "prioritization"]),
            new("CTO", "CTO", OrgLevel.Executive, "CEO", ["architecture", "engineering-standards"]),
            new("CFO", "CFO", OrgLevel.Executive, "CEO", ["budgeting", "cost-tracking", "profitability"]),
            new("HR", "HR Agent", OrgLevel.Head, "CEO", ["role-assignment", "evaluation", "staffing"]),
        ]),
        new("SALES", "ฝ่ายขายและธุรกิจ", "Sales and Business", [
            new("SALES", "Sales Agent", OrgLevel.Head, "COO", ["lead-intake", "customer-communication"]),
            new("BA", "Business Analyst Agent", OrgLevel.Senior, "SALES", ["requirements", "user-stories"]),
            new("SOLCON", "Solution Consultant Agent", OrgLevel.Senior, "SALES", ["solution-design", "presales"]),
            new("ACCMGR", "Account Manager Agent", OrgLevel.Staff, "SALES", ["account-management"]),
            new("PROPOSAL", "Proposal and Estimation Agent", OrgLevel.Staff, "SALES", ["proposal", "estimation"]),
        ]),
        new("PMO", "ฝ่ายบริหารโครงการ", "Project Management", [
            new("PM", "Project Manager Agent", OrgLevel.Head, "COO", ["planning", "tracking", "risk"]),
            new("SCRUM", "Scrum Master Agent", OrgLevel.Senior, "PM", ["agile", "facilitation"]),
            new("BREQ", "Business Requirements Agent", OrgLevel.Staff, "PM", ["requirements", "acceptance-criteria"]),
            new("DOCS", "Documentation Agent", OrgLevel.Staff, "PM", ["documentation"]),
        ]),
        new("ENG", "ฝ่ายวิศวกรรมซอฟต์แวร์", "Software Engineering", [
            new("ARCH", "Software Architect Agent", OrgLevel.Head, "CTO", ["architecture", "api-design"]),
            new("BACKEND", "Backend Developer Agent", OrgLevel.Staff, "ARCH", ["csharp", "dotnet", "api"]),
            new("FRONTEND", "Frontend Developer Agent", OrgLevel.Staff, "ARCH", ["typescript", "react", "nextjs"]),
            new("MOBILE", "Mobile Developer Agent", OrgLevel.Staff, "ARCH", ["mobile"]),
            new("DBENG", "Database Engineer Agent", OrgLevel.Staff, "ARCH", ["postgresql", "data-modeling"]),
            new("DEVOPS", "DevOps and Platform Engineer Agent", OrgLevel.Staff, "ARCH", ["docker", "ci-cd"]),
            new("INTEG", "Integration Engineer Agent", OrgLevel.Staff, "ARCH", ["integration", "apis"]),
        ]),
        new("QA", "ฝ่ายคุณภาพและความปลอดภัย", "Quality and Security", [
            new("QAENG", "QA Engineer Agent", OrgLevel.Head, "CTO", ["testing", "quality-gates"]),
            new("AUTOTEST", "Automation Test Engineer Agent", OrgLevel.Staff, "QAENG", ["test-automation"]),
            new("REVIEWER", "Code Reviewer Agent", OrgLevel.Senior, "QAENG", ["code-review"]),
            new("SECENG", "Security Engineer Agent", OrgLevel.Senior, "QAENG", ["security-review", "threat-modeling"]),
            new("RELEASE", "Release Manager Agent", OrgLevel.Staff, "QAENG", ["release", "rollback-planning"]),
        ]),
        new("OPS", "ฝ่ายปฏิบัติการองค์กร", "Corporate Operations", [
            new("FINANCE", "Finance Agent", OrgLevel.Staff, "CFO", ["accounting", "reporting"]),
            new("PROCURE", "Procurement Agent", OrgLevel.Staff, "CFO", ["vendor-management"]),
            new("OPERATIONS", "Operations Agent", OrgLevel.Staff, "COO", ["operations"]),
            new("LEGAL", "Legal and Compliance Assistant Agent", OrgLevel.Staff, "COO", ["compliance"]),
            new("SUPPORT", "Customer Support Agent", OrgLevel.Staff, "COO", ["customer-support"]),
        ]),
    ];

    private async Task SeedOrganizationAsync(AppUser ceoUser, CancellationToken ct)
    {
        var employeesByCode = new Dictionary<string, EmployeeProfile>();
        var managerOf = new Dictionary<EmployeeProfile, string?>();
        var order = 0;

        foreach (var d in Organization)
        {
            var dept = new Department { Code = d.Code, Name = d.Name, Description = d.Description, SortOrder = order++ };
            db.Departments.Add(dept);
            foreach (var p in d.Positions)
            {
                var role = new EmployeeRoleDefinition
                {
                    Code = p.Code, Title = p.Title, DepartmentId = dept.Id, Level = p.Level, Skills = [.. p.Skills],
                    RequiresHuman = p.Human, Description = $"{p.Title} — {d.Description}",
                };
                db.RoleDefinitions.Add(role);
                var emp = new EmployeeProfile
                {
                    DisplayName = p.Human ? ceoUser.DisplayName : p.Title.Replace(" Agent", "") + " AI",
                    Kind = p.Human ? EmployeeKind.Human : EmployeeKind.AI,
                    UserId = p.Human ? ceoUser.Id : null,
                    DepartmentId = dept.Id,
                    RoleDefinitionId = role.Id,
                };
                db.Employees.Add(emp);
                employeesByCode[p.Code] = emp;
                managerOf[emp] = p.ManagerCode;
            }
        }
        foreach (var (emp, managerCode) in managerOf)
            if (managerCode is not null) emp.ManagerId = employeesByCode[managerCode].Id;

        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded {Departments} departments and {Employees} employees", Organization.Length, employeesByCode.Count);
    }

    private static void Check(IdentityResult r)
    {
        if (!r.Succeeded) throw new InvalidOperationException(string.Join("; ", r.Errors.Select(e => e.Description)));
    }
}
