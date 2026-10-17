using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Api.Infrastructure;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Code;
using OfficeAI.Application.Knowledge;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Application.Delivery;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Workflows;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Identity;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Api.Endpoints;

public record LoginRequest(string Email, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record MeDto(Guid UserId, string Email, string DisplayName, bool IsAgent, List<string> Roles, List<string> Permissions, Guid? EmployeeId);

public static class Endpoints
{
    public static void MapAppEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        MapAuth(api);
        MapOrganization(api);
        MapWork(api);
        MapSecurity(api);
        MapAgents(api);
        MapWorkflows(api);
        MapDelivery(api);
        MapCode(api);
        MapReports(api);
        MapKnowledge(api);
        MapIntegrations(api);
        app.MapHub<EventsHub>("/hubs/events");
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    private static void MapAuth(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/login", async (LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn, SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
            if (user is null || !user.IsActive || user.IsAgent)
            {
                await audit.WriteAsync(AuditActions.LoginFailed, "User", user?.Id.ToString(), user?.Id, req.Email, new { Reason = "unknown_or_inactive" }, ct);
                return Results.Problem(statusCode: 401, title: "Invalid email or password.");
            }

            var result = await signIn.PasswordSignInAsync(user, req.Password ?? "", isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await audit.WriteAsync(AuditActions.LoginSucceeded, "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
                return Results.NoContent();
            }
            if (result.IsLockedOut)
            {
                await audit.WriteAsync(AuditActions.LockedOut, "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
                return Results.Problem(statusCode: 423, title: "Account is locked. Try again later.");
            }
            await audit.WriteAsync(AuditActions.LoginFailed, "User", user.Id.ToString(), user.Id, user.DisplayName, new { Reason = "bad_password" }, ct);
            return Results.Problem(statusCode: 401, title: "Invalid email or password.");
        }).AllowAnonymous().RequireRateLimiting("login");

        auth.MapPost("/change-password", async (ChangePasswordRequest req, ICurrentUser me, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(me.UserId!.Value.ToString());
            if (user is null) return Results.Unauthorized();
            var result = await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", req.NewPassword ?? "");
            if (!result.Succeeded)
                return Results.Problem(statusCode: 400, title: string.Join(" ", result.Errors.Select(e => e.Description)));
            await signIn.RefreshSignInAsync(user); // the security stamp changed: other sessions are signed out
            await audit.WriteAsync(AuditActions.Updated, "UserPassword", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (ICurrentUser current, UserManager<AppUser> users, PermissionService perms, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(current.UserId!.Value.ToString());
            if (user is null) return Results.Unauthorized();
            var employeeId = await db.Employees.Where(e => e.UserId == user.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            return Results.Ok(new MeDto(user.Id, user.Email!, user.DisplayName, user.IsAgent,
                [.. await users.GetRolesAsync(user)], [.. (await perms.GetPermissionsAsync(user.Id, ct)).Order()], employeeId));
        }).RequireAuthorization();
    }

    private static void MapOrganization(RouteGroupBuilder api)
    {
        api.MapGet("/departments", (OrganizationService s, CancellationToken ct) => s.ListDepartmentsAsync(ct)).RequirePermission(Permissions.OrgRead);
        api.MapPost("/departments", (SaveDepartmentRequest r, OrganizationService s, CancellationToken ct) => s.CreateDepartmentAsync(r, ct)).RequirePermission(Permissions.OrgManage);
        api.MapPut("/departments/{id:guid}", (Guid id, SaveDepartmentRequest r, OrganizationService s, CancellationToken ct) => s.UpdateDepartmentAsync(id, r, ct)).RequirePermission(Permissions.OrgManage);

        api.MapGet("/role-definitions", (OrganizationService s, CancellationToken ct) => s.ListRoleDefinitionsAsync(ct)).RequirePermission(Permissions.OrgRead);
        api.MapPost("/role-definitions", (SaveRoleDefinitionRequest r, OrganizationService s, CancellationToken ct) => s.CreateRoleDefinitionAsync(r, ct)).RequirePermission(Permissions.OrgManage);
        api.MapPut("/role-definitions/{id:guid}", (Guid id, SaveRoleDefinitionRequest r, OrganizationService s, CancellationToken ct) => s.UpdateRoleDefinitionAsync(id, r, ct)).RequirePermission(Permissions.OrgManage);

        api.MapGet("/employees", (OrganizationService s, CancellationToken ct) => s.ListEmployeesAsync(ct)).RequirePermission(Permissions.OrgRead);
        api.MapGet("/employees/{id:guid}", (Guid id, OrganizationService s, CancellationToken ct) => s.GetEmployeeAsync(id, ct)).RequirePermission(Permissions.OrgRead);
        api.MapPost("/employees", (SaveEmployeeRequest r, OrganizationService s, CancellationToken ct) => s.CreateEmployeeAsync(r, ct)).RequirePermission(Permissions.OrgManage);
        api.MapPut("/employees/{id:guid}", (Guid id, SaveEmployeeRequest r, OrganizationService s, CancellationToken ct) => s.UpdateEmployeeAsync(id, r, ct)).RequirePermission(Permissions.OrgManage);

        api.MapGet("/org-chart", (OrganizationService s, CancellationToken ct) => s.GetOrgChartAsync(ct)).RequirePermission(Permissions.OrgRead);
    }

    private static void MapWork(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", (WorkService s, CancellationToken ct) => s.GetDashboardAsync(ct)).RequirePermission(Permissions.ProjectRead);

        api.MapGet("/projects", (WorkService s, CancellationToken ct) => s.ListProjectsAsync(ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/projects/{id:guid}", (Guid id, WorkService s, CancellationToken ct) => s.GetProjectAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/projects", (SaveProjectRequest r, WorkService s, CancellationToken ct) => s.CreateProjectAsync(r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapPut("/projects/{id:guid}", (Guid id, SaveProjectRequest r, WorkService s, CancellationToken ct) => s.UpdateProjectAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);

        api.MapGet("/projects/{id:guid}/milestones", (Guid id, WorkService s, CancellationToken ct) => s.ListMilestonesAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/projects/{id:guid}/milestones", (Guid id, SaveMilestoneRequest r, WorkService s, CancellationToken ct) => s.CreateMilestoneAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);

        api.MapGet("/projects/{id:guid}/tasks", (Guid id, WorkService s, CancellationToken ct) => s.ListTasksAsync(id, ct)).RequirePermission(Permissions.TaskRead);
        api.MapPost("/projects/{id:guid}/tasks", (Guid id, SaveTaskRequest r, WorkService s, CancellationToken ct) => s.CreateTaskAsync(id, r, ct)).RequirePermission(Permissions.TaskManage);
        api.MapGet("/tasks/{id:guid}", (Guid id, WorkService s, CancellationToken ct) => s.GetTaskAsync(id, ct)).RequirePermission(Permissions.TaskRead);
        api.MapPut("/tasks/{id:guid}", (Guid id, SaveTaskRequest r, WorkService s, CancellationToken ct) => s.UpdateTaskAsync(id, r, ct)).RequirePermission(Permissions.TaskManage);
        api.MapPost("/tasks/{id:guid}/status", async (Guid id, ChangeStatusRequest r, WorkService s, PermissionService perms, ICurrentUser me, CancellationToken ct) =>
        {
            var canApprove = (await perms.GetPermissionsAsync(me.UserId!.Value, ct)).Contains(Permissions.TaskApprove);
            return await s.ChangeStatusAsync(id, r, canApprove, ct);
        }).RequirePermission(Permissions.TaskUpdateStatus);
        api.MapPost("/tasks/{id:guid}/assign", (Guid id, AssignRequest r, WorkService s, CancellationToken ct) => s.AssignAsync(id, r, ct)).RequirePermission(Permissions.TaskAssign);
        api.MapPost("/tasks/{id:guid}/dependencies", (Guid id, AddDependencyRequest r, WorkService s, CancellationToken ct) => s.AddDependencyAsync(id, r, ct)).RequirePermission(Permissions.TaskManage);
        api.MapDelete("/tasks/{id:guid}/dependencies/{dependsOn:guid}", (Guid id, Guid dependsOn, WorkService s, CancellationToken ct) => s.RemoveDependencyAsync(id, dependsOn, ct)).RequirePermission(Permissions.TaskManage);
    }

    private static void MapAgents(RouteGroupBuilder api)
    {
        // Configuration (human only: agent.manage is never usable by AI identities)
        api.MapGet("/ai/providers", (AgentAdminService s) => s.ListProviders()).RequirePermission(Permissions.AgentRead);
        api.MapGet("/ai/models", (AgentAdminService s, CancellationToken ct) => s.ListModelsAsync(ct)).RequirePermission(Permissions.AgentRead);
        api.MapPut("/ai/models/{id:guid}", (Guid id, UpdateAiModelRequest r, AgentAdminService s, CancellationToken ct) => s.UpdateModelAsync(id, r, ct)).RequirePermission(Permissions.AgentManage);
        api.MapGet("/ai/tools", (AgentAdminService s) => s.ListTools()).RequirePermission(Permissions.AgentRead);
        api.MapGet("/agents/{employeeId:guid}/config", (Guid employeeId, AgentAdminService s, CancellationToken ct) => s.GetConfigAsync(employeeId, ct)).RequirePermission(Permissions.AgentRead);
        api.MapPut("/agents/{employeeId:guid}/config", (Guid employeeId, UpdateAgentConfigRequest r, AgentAdminService s, CancellationToken ct) => s.UpdateConfigAsync(employeeId, r, ct)).RequirePermission(Permissions.AgentManage);
        api.MapPut("/agents/{employeeId:guid}/tools", (Guid employeeId, SetAgentToolsRequest r, AgentAdminService s, ICurrentUser me, CancellationToken ct) => s.SetToolsAsync(employeeId, r, me.UserId, ct)).RequirePermission(Permissions.AgentManage);

        // Runs
        api.MapPost("/tasks/{id:guid}/runs", (Guid id, StartRunRequest? r, AgentRunService s, CancellationToken ct) => s.StartAsync(id, r ?? new StartRunRequest(), ct)).RequirePermission(Permissions.AgentRun);
        api.MapGet("/tasks/{id:guid}/deliverables", (Guid id, AgentRunService s, CancellationToken ct) => s.ListDeliverablesAsync(id, ct)).RequirePermission(Permissions.TaskRead);
        api.MapGet("/agent-runs", (Guid? taskId, Guid? employeeId, Guid? projectId, int? take, AgentRunService s, CancellationToken ct) =>
            s.ListRunsAsync(taskId, employeeId, projectId, take ?? 50, ct)).RequirePermission(Permissions.AgentRead);
        api.MapGet("/agent-runs/{id:guid}", (Guid id, AgentRunService s, CancellationToken ct) => s.GetRunDetailAsync(id, ct)).RequirePermission(Permissions.AgentRead);
        api.MapPost("/agent-runs/{id:guid}/cancel", (Guid id, AgentRunService s, CancellationToken ct) => s.CancelAsync(id, ct)).RequirePermission(Permissions.AgentRun);
        api.MapGet("/ai/usage", (AgentRunService s, CancellationToken ct) => s.GetUsageSummaryAsync(ct)).RequirePermission(Permissions.AgentRead);
    }

    private static void MapWorkflows(RouteGroupBuilder api)
    {
        api.MapPost("/projects/{id:guid}/objectives", (Guid id, StartObjectiveRequest r, ObjectiveService s, CancellationToken ct) => s.StartAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapGet("/workflows", (Guid? projectId, ObjectiveService s, CancellationToken ct) => s.ListAsync(projectId, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/workflows/{id:guid}", (Guid id, ObjectiveService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/workflows/{id:guid}/events", (Guid id, ObjectiveService s, CancellationToken ct) => s.ListEventsAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/workflows/{id:guid}/cancel", (Guid id, ObjectiveService s, CancellationToken ct) => s.CancelAsync(id, ct)).RequirePermission(Permissions.ProjectManage);

        api.MapGet("/approvals", (ApprovalStatus? status, Guid? workflowId, Guid? taskId, ApprovalService s, CancellationToken ct) =>
            s.ListAsync(status, workflowId, taskId, ct)).RequirePermission(Permissions.TaskRead);
        api.MapGet("/approvals/{id:guid}", (Guid id, ApprovalService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(Permissions.TaskRead);
        // Which permission a decision needs depends on the kind of request; the service checks it against the caller's real permissions.
        api.MapPost("/approvals/{id:guid}/decision", async (Guid id, DecideApprovalRequest r, ApprovalService s, PermissionService perms, ICurrentUser me, CancellationToken ct) =>
            await s.DecideAsync(id, r, await perms.GetPermissionsAsync(me.UserId!.Value, ct), ct)).RequirePermission(Permissions.TaskRead);
        api.MapGet("/tasks/{id:guid}/reviews", (Guid id, ApprovalService s, CancellationToken ct) => s.ListReviewsAsync(id, ct)).RequirePermission(Permissions.TaskRead);
    }

    private static void MapDelivery(RouteGroupBuilder api)
    {
        api.MapGet("/projects/{id:guid}/requirements", (Guid id, DeliveryService s, CancellationToken ct) => s.ListRequirementsAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/projects/{id:guid}/requirements", (Guid id, SaveRequirementRequest r, DeliveryService s, CancellationToken ct) => s.CreateRequirementAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapPut("/requirements/{id:guid}", (Guid id, SaveRequirementRequest r, DeliveryService s, CancellationToken ct) => s.UpdateRequirementAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapPost("/requirements/{id:guid}/status", (Guid id, SetReviewStateRequest r, DeliveryService s, CancellationToken ct) => s.SetRequirementStatusAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapGet("/projects/{id:guid}/decisions", (Guid id, DeliveryService s, CancellationToken ct) => s.ListDecisionsAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/decisions/{id:guid}/status", (Guid id, SetDecisionStatusRequest r, DeliveryService s, CancellationToken ct) => s.SetDecisionStatusAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapGet("/projects/{id:guid}/estimates", (Guid id, DeliveryService s, CancellationToken ct) => s.ListEstimatesAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/rate-card", (DeliveryService s, CancellationToken ct) => s.ListRatesAsync(ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPut("/rate-card/{roleDefinitionId:guid}", (Guid roleDefinitionId, SetRoleRateRequest r, DeliveryService s, CancellationToken ct) => s.SetRateAsync(roleDefinitionId, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapGet("/company-settings", (DeliveryService s, CancellationToken ct) => s.GetSettingsAsync(ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPut("/company-settings", (SaveSettingsRequest r, DeliveryService s, CancellationToken ct) => s.SaveSettingsAsync(r, ct)).RequirePermission(Permissions.ProjectManage);
    }

    private static void MapCode(RouteGroupBuilder api)
    {
        api.MapGet("/sandbox", (CodeService s) => s.SandboxInfo()).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/projects/{id:guid}/repositories", (Guid id, CodeService s, CancellationToken ct) => s.ListAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/projects/{id:guid}/repositories", (Guid id, CreateRepositoryRequest r, CodeService s, CancellationToken ct) => s.CreateAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapPut("/repositories/{id:guid}", (Guid id, UpdateRepositoryRequest r, CodeService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapGet("/repositories/{id:guid}/branches", (Guid id, CodeService s, CancellationToken ct) => s.BranchesAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/repositories/{id:guid}/files", (Guid id, string? branch, CodeService s, CancellationToken ct) => s.FilesAsync(id, branch, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/repositories/{id:guid}/file", async (Guid id, string? branch, string path, CodeService s, CancellationToken ct) =>
            Results.Text(await s.FileAsync(id, branch, path, ct), "text/plain; charset=utf-8")).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/repositories/{id:guid}/test-runs", (Guid id, Guid? taskId, CodeService s, CancellationToken ct) => s.TestRunsAsync(id, taskId, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/repositories/{id:guid}/test-runs", (Guid id, RunBranchTestsRequest? r, CodeService s, TimeProvider clock, CancellationToken ct) => s.RunTestsAsync(id, r?.Branch, clock, ct)).RequirePermission(Permissions.ProjectManage);
        api.MapGet("/projects/{id:guid}/merge-requests", (Guid id, CodeService s, CancellationToken ct) => s.MergeRequestsAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/merge-requests/{id:guid}", (Guid id, CodeService s, CancellationToken ct) => s.MergeRequestAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/reports/finance", (DateOnly? month, OfficeAI.Application.Reports.ReportsService s, CancellationToken ct) => s.FinanceAsync(month, ct)).RequirePermission(Permissions.FinanceRead);
        api.MapGet("/reports/performance", (DateOnly? month, OfficeAI.Application.Reports.ReportsService s, CancellationToken ct) => s.PerformanceAsync(month, ct)).RequirePermission(Permissions.FinanceRead);
        api.MapGet("/reports/quality-security", (int? days, OfficeAI.Application.Reports.ReportsService s, CancellationToken ct) => s.QualitySecurityAsync(days ?? 30, ct)).RequirePermission(Permissions.AuditRead);
        api.MapGet("/budgets", (DateOnly? month, OfficeAI.Application.Reports.ReportsService s, CancellationToken ct) => s.ListBudgetsAsync(month, ct)).RequirePermission(Permissions.FinanceRead);
        api.MapPut("/budgets", (OfficeAI.Application.Reports.SetBudgetRequest r, OfficeAI.Application.Reports.ReportsService s, CancellationToken ct) => s.SetBudgetAsync(r, ct)).RequirePermission(Permissions.FinanceManage);
        api.MapDelete("/budgets/{id:guid}", async (Guid id, OfficeAI.Application.Reports.ReportsService s, CancellationToken ct) =>
        {
            await s.DeleteBudgetAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.FinanceManage);
    }

    private static void MapKnowledge(RouteGroupBuilder api)
    {
        api.MapGet("/knowledge", (string? q, KnowledgeStatus? status, Guid? projectId, KnowledgeService s, CancellationToken ct) => s.ListAsync(q, status, projectId, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/knowledge", (SaveKnowledgeRequest r, KnowledgeService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequirePermission(Permissions.KnowledgeManage);
        api.MapPost("/knowledge/{id:guid}/status", (Guid id, SetKnowledgeStatusRequest r, KnowledgeService s, CancellationToken ct) => s.SetStatusAsync(id, r, ct)).RequirePermission(Permissions.KnowledgeManage);
        api.MapGet("/projects/{id:guid}/documents", (Guid id, KnowledgeService s, CancellationToken ct) => s.ListDocumentsAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/documents/{id:guid}", (Guid id, KnowledgeService s, CancellationToken ct) => s.GetDocumentAsync(id, ct)).RequirePermission(Permissions.ProjectRead);
        api.MapGet("/documents/{id:guid}/download", async (Guid id, KnowledgeService s, CancellationToken ct) =>
        {
            var d = await s.GetDocumentAsync(id, ct);
            var name = string.Concat(d.Title.Select(c => char.IsLetterOrDigit(c) ? c : '-')) + $"-v{d.Version}.md";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(d.Content ?? ""), "text/markdown; charset=utf-8", name);
        }).RequirePermission(Permissions.ProjectRead);
        api.MapPost("/documents/{id:guid}/finalize", (Guid id, KnowledgeService s, CancellationToken ct) => s.FinalizeDocumentAsync(id, ct)).RequirePermission(Permissions.KnowledgeManage);
    }

    private static void MapIntegrations(RouteGroupBuilder api)
    {
        api.MapGet("/integrations", (OfficeAI.Application.Integrations.IntegrationService s, CancellationToken ct) => s.ListAsync(ct)).RequirePermission(Permissions.SecurityManage);
        api.MapPost("/integrations", (OfficeAI.Application.Integrations.SaveIntegrationRequest r, OfficeAI.Application.Integrations.IntegrationService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequirePermission(Permissions.SecurityManage);
        api.MapPut("/integrations/{id:guid}", (Guid id, OfficeAI.Application.Integrations.SaveIntegrationRequest r, OfficeAI.Application.Integrations.IntegrationService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(Permissions.SecurityManage);
        api.MapGet("/integrations/{id:guid}/deliveries", (Guid id, OfficeAI.Application.Integrations.IntegrationService s, CancellationToken ct) => s.DeliveriesAsync(id, ct)).RequirePermission(Permissions.SecurityManage);
    }

    private static void MapSecurity(RouteGroupBuilder api)
    {
        var sec = api.MapGroup("/security");
        sec.MapGet("/permissions", () => Permissions.All.Select(p => new { code = p.Key, description = p.Value })).RequirePermission(Permissions.SecurityManage);
        sec.MapGet("/roles", (SecurityAdminService s, CancellationToken ct) => s.ListRolesAsync(ct)).RequirePermission(Permissions.SecurityManage);
        sec.MapPut("/roles/{id:guid}/permissions", (Guid id, SetRolePermissionsRequest r, SecurityAdminService s, CancellationToken ct) => s.SetRolePermissionsAsync(id, r, ct)).RequirePermission(Permissions.SecurityManage);
        sec.MapGet("/users", (SecurityAdminService s, CancellationToken ct) => s.ListUsersAsync(ct)).RequirePermission(Permissions.SecurityManage);
        sec.MapPost("/users", (CreateUserRequest r, SecurityAdminService s, CancellationToken ct) => s.CreateUserAsync(r, ct)).RequirePermission(Permissions.SecurityManage);
        sec.MapPut("/users/{id:guid}/roles", (Guid id, SetUserRolesRequest r, SecurityAdminService s, CancellationToken ct) => s.SetUserRolesAsync(id, r, ct)).RequirePermission(Permissions.SecurityManage);

        api.MapGet("/audit", (string? entityType, string? entityId, int? take, SecurityAdminService s, CancellationToken ct) =>
            s.ListAuditAsync(entityType, entityId, take ?? 100, ct)).RequirePermission(Permissions.AuditRead);
    }
}
