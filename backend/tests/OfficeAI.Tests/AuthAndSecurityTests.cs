using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Api.Endpoints;
using OfficeAI.Api.Infrastructure;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class AuthAndSecurityTests(ApiFixture fx)
{
    [Fact]
    public async Task Login_with_correct_password_succeeds_and_me_returns_permissions()
    {
        var ceo = await fx.CeoAsync();
        var me = await ceo.GetAsync<MeDto>("/api/auth/me");
        Assert.Equal(ApiFixture.CeoEmail, me.Email);
        Assert.Contains(Permissions.SecurityManage, me.Permissions);
        Assert.NotNull(me.EmployeeId);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        var res = await fx.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = ApiFixture.CeoEmail, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_five_consecutive_failures()
    {
        var ceo = await fx.CeoAsync();
        var email = $"lock-{Guid.NewGuid():N}@test.local";
        (await ceo.PostAsJsonAsync("/api/security/users", new { email, displayName = "Lock me", password = "Correct-Password-1", isAgent = false, roles = new[] { SystemRoles.Member } }))
            .EnsureSuccessStatusCode();

        var client = fx.Anonymous();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await client.PostAsJsonAsync("/api/auth/login", new { email, password = "bad" })).StatusCode);

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Unauthorized, 4), codes.Take(4));
        Assert.Equal((HttpStatusCode)423, codes[4]);
        // Even the right password is refused while locked.
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Correct-Password-1" });
        Assert.Equal((HttpStatusCode)423, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_get_401()
    {
        var client = fx.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/employees")).StatusCode);
    }

    public static TheoryData<string, string> MutatingEndpoints => new()
    {
        { "POST", "/api/departments" },
        { "PUT", $"/api/departments/{Guid.Empty}" },
        { "POST", "/api/role-definitions" },
        { "PUT", $"/api/role-definitions/{Guid.Empty}" },
        { "POST", "/api/employees" },
        { "PUT", $"/api/employees/{Guid.Empty}" },
        { "POST", "/api/projects" },
        { "PUT", $"/api/projects/{Guid.Empty}" },
        { "POST", $"/api/projects/{Guid.Empty}/milestones" },
        { "POST", $"/api/projects/{Guid.Empty}/tasks" },
        { "PUT", $"/api/tasks/{Guid.Empty}" },
        { "POST", $"/api/tasks/{Guid.Empty}/assign" },
        { "POST", $"/api/tasks/{Guid.Empty}/dependencies" },
        { "DELETE", $"/api/tasks/{Guid.Empty}/dependencies/{Guid.Empty}" },
        { "POST", "/api/security/users" },
        { "PUT", $"/api/security/users/{Guid.Empty}/roles" },
        { "PUT", $"/api/security/roles/{Guid.Empty}/permissions" },
        { "PUT", $"/api/ai/models/{Guid.Empty}" },
        { "PUT", $"/api/agents/{Guid.Empty}/config" },
        { "PUT", $"/api/agents/{Guid.Empty}/tools" },
        { "POST", $"/api/tasks/{Guid.Empty}/runs" },
        { "POST", $"/api/agent-runs/{Guid.Empty}/cancel" },
        { "POST", $"/api/projects/{Guid.Empty}/objectives" },
        { "POST", $"/api/workflows/{Guid.Empty}/cancel" },
        { "POST", $"/api/projects/{Guid.Empty}/requirements" },
        { "PUT", $"/api/requirements/{Guid.Empty}" },
        { "POST", $"/api/requirements/{Guid.Empty}/status" },
        { "POST", $"/api/decisions/{Guid.Empty}/status" },
        { "PUT", $"/api/rate-card/{Guid.Empty}" },
        { "PUT", "/api/company-settings" },
        { "POST", $"/api/projects/{Guid.Empty}/repositories" },
        { "PUT", $"/api/repositories/{Guid.Empty}" },
        { "POST", $"/api/repositories/{Guid.Empty}/test-runs" },
        { "PUT", "/api/budgets" },
        { "POST", "/api/integrations" },
        { "PUT", $"/api/integrations/{Guid.Empty}" },
        { "DELETE", $"/api/budgets/{Guid.Empty}" },
        { "POST", "/api/knowledge" },
        { "POST", $"/api/knowledge/{Guid.Empty}/status" },
        { "POST", $"/api/documents/{Guid.Empty}/finalize" },
    };

    [Theory]
    [MemberData(nameof(MutatingEndpoints))]
    public async Task Member_without_permission_gets_403_on_mutating_endpoints(string method, string url)
    {
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        var req = new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) };
        var res = await member.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Denied_request_is_audited()
    {
        var member = await fx.UserWithRolesAsync(SystemRoles.Member);
        var me = await member.GetAsync<MeDto>("/api/auth/me");
        await member.PostAsJsonAsync("/api/departments", new { code = "X", name = "X" });

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.ActorUserId == me.UserId && a.Action == AuditActions.AccessDenied && a.EntityId == "/api/departments"));
    }

    [Fact]
    public async Task AI_agent_cannot_use_security_manage_even_with_CEO_role_and_attempt_is_audited()
    {
        // The CEO (wrongly) gives an agent account the CEO role.
        var ceo = await fx.CeoAsync();
        var agent = await ceo.PostAsync<UserDto>("/api/security/users",
            new { email = $"agent-{Guid.NewGuid():N}@agents.local", displayName = "Rogue agent", isAgent = true, roles = new[] { SystemRoles.Ceo } });

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Effective permissions never include human-only ones for agents.
        var perms = await sp.GetRequiredService<PermissionService>().GetPermissionsAsync(agent.Id, default);
        Assert.DoesNotContain(Permissions.SecurityManage, perms);
        Assert.DoesNotContain(Permissions.TaskApprove, perms);
        Assert.DoesNotContain(Permissions.AgentManage, perms);
        Assert.Contains(Permissions.ProjectManage, perms);

        // The real authorization pipeline denies it and writes an audit record.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, agent.Id.ToString()), new Claim(AppClaims.IsAgent, "true"), new Claim(AppClaims.DisplayName, "Rogue agent")],
            "Test"));
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = principal,
            RequestServices = sp,
            Request = { Path = $"/api/security/roles/{Guid.Empty}/permissions", Method = "PUT" },
        };
        var result = await sp.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, PermissionPolicyProvider.Prefix + Permissions.SecurityManage);
        Assert.False(result.Succeeded);

        var db = sp.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.ActorUserId == agent.Id && a.Action == AuditActions.PermissionChangeDenied));
    }

    [Fact]
    public async Task Agent_accounts_cannot_log_in_with_a_password()
    {
        var ceo = await fx.CeoAsync();
        var email = $"agent-{Guid.NewGuid():N}@agents.local";
        await ceo.PostAsync<UserDto>("/api/security/users", new { email, displayName = "Agent", password = "Some-Password-123", isAgent = true, roles = Array.Empty<string>() });
        var res = await fx.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "Some-Password-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task User_cannot_change_own_roles()
    {
        var ceo = await fx.CeoAsync();
        var me = await ceo.GetAsync<MeDto>("/api/auth/me");
        var res = await ceo.PutAsJsonAsync($"/api/security/users/{me.UserId}/roles", new { roles = new[] { SystemRoles.Member } });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CEO_role_cannot_lose_security_manage()
    {
        var ceo = await fx.CeoAsync();
        var roles = await ceo.GetAsync<List<RoleDto>>("/api/security/roles");
        var ceoRole = roles.Single(r => r.Name == SystemRoles.Ceo);
        var res = await ceo.PutAsJsonAsync($"/api/security/roles/{ceoRole.Id}/permissions", new { permissions = new[] { Permissions.OrgRead } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
