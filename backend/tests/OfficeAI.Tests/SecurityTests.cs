using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Domain.Agents;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class SecurityTests(ApiFixture fx)
{
    [Fact]
    public async Task State_changing_calls_without_the_CSRF_header_are_refused()
    {
        var ceo = await fx.CeoAsync();
        var raw = fx.Factory.CreateClient(new() { HandleCookies = false });
        var cookie = (await raw.PostAsJsonAsync("/api/auth/login", new { email = ApiFixture.CeoEmail, password = ApiFixture.CeoPassword })).StatusCode;
        Assert.Equal(HttpStatusCode.Forbidden, cookie); // even login needs it: a cross-site page cannot log you in to its own account

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/departments") { Content = JsonContent.Create(new { code = "CSRF", name = "x" }) };
        foreach (var h in ceo.DefaultRequestHeaders.Where(h => h.Key != "X-Requested-With")) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        var res = await fx.Factory.CreateClient(new() { HandleCookies = false }).SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);

        // Reads stay open to the authenticated client.
        Assert.Equal(HttpStatusCode.OK, (await ceo.GetAsync("/api/departments")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var res = await fx.Anonymous().GetAsync("/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Cross_origin_requests_from_unknown_sites_are_not_allowed()
    {
        var client = fx.Anonymous();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/departments");
        preflight.Headers.Add("Origin", "https://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-requested-with");
        var res = await client.SendAsync(preflight);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Agents_have_no_tool_that_touches_permissions_money_or_the_host()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<ToolRegistry>().All.Select(t => t.Descriptor.Id).ToList();
        string[] allowedPrefixes = ["task.", "project.", "org.", "deliverable.", "plan.", "review.", "requirements.", "adr.", "estimate.", "repo.",
            "sandbox.", "knowledge.", "document.", "test."];
        Assert.All(tools, id => Assert.Contains(allowedPrefixes, p => id.StartsWith(p, StringComparison.Ordinal)));
        Assert.DoesNotContain(tools, id => id.Contains("security") || id.Contains("permission") || id.Contains("budget") || id.Contains("shell") || id.Contains("exec"));
    }

    [Fact]
    public async Task Agent_tools_only_see_the_project_of_their_task()
    {
        var ceo = await fx.CeoAsync();
        var marker = $"secret-{Guid.NewGuid():N}";
        var other = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"OT-{Guid.NewGuid():N}"[..14], name = "Other client", status = "Active" });
        await ceo.PostAsync<TaskDto>($"/api/projects/{other.Id}/tasks", new { title = marker });
        await ceo.PostAsync<object>($"/api/projects/{other.Id}/requirements", new { kind = "Functional", title = marker, priority = "Must" });

        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var agent = await ceo.PostAsync<EmployeeDto>("/api/employees",
            new { displayName = $"Iso {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId });
            await db.SaveChangesAsync();
        }
        fx.Scripted.Register(modelId, ScriptedProvider.Sequence(
            ScriptedProvider.Call("project_list_tasks", new { }),
            ScriptedProvider.Call("project_read_requirements", new { }),
            ScriptedProvider.Call("org_find_employees", new { query = "backend" })));
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{agent.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{agent.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, c.MonthlyBudgetUsd, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();
        var mine = await ceo.PostAsync<ProjectDto>("/api/projects", new { code = $"MY-{Guid.NewGuid():N}"[..14], name = "My client", status = "Active" });
        var task = await ceo.PostAsync<TaskDto>($"/api/projects/{mine.Id}/tasks", new { title = "My task" });
        await ceo.PostAsync<TaskDto>($"/api/tasks/{task.Id}/assign", new { employeeId = agent.Id });

        var run = await ceo.PostAsync<AgentRunDto>($"/api/tasks/{task.Id}/runs", new { });
        AgentRunDetailDto detail;
        do
        {
            await Task.Delay(100);
            detail = await ceo.GetAsync<AgentRunDetailDto>($"/api/agent-runs/{run.Id}");
        } while (detail.Run.Status is AgentRunStatus.Queued or AgentRunStatus.Running);

        Assert.Equal(3, detail.ToolExecutions.Count);
        Assert.All(detail.ToolExecutions, x =>
        {
            Assert.Equal(ToolExecutionStatus.Succeeded, x.Status);
            Assert.DoesNotContain(marker, x.Output);
        });
        Assert.Contains("My task", detail.ToolExecutions[0].Output);
    }
}

[Collection(ApiCollection.Name)]
public class PasswordTests(ApiFixture fx)
{
    [Fact]
    public async Task Users_can_change_their_password_with_the_current_one()
    {
        var ceo = await fx.CeoAsync();
        var email = $"pw-{Guid.NewGuid():N}@test.local";
        (await ceo.PostAsJsonAsync("/api/security/users", new { email, displayName = "Pw", password = "Old-Password-123", isAgent = false, roles = new[] { "Member" } })).EnsureSuccessStatusCode();
        var user = await fx.LoginAsync(email, "Old-Password-123");

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "wrong", newPassword = "New-Password-456" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Old-Password-123", newPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Old-Password-123", newPassword = "New-Password-456" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = "Old-Password-123" })).StatusCode);
        await fx.LoginAsync(email, "New-Password-456");
    }
}
