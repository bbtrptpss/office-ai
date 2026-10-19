using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using Testcontainers.PostgreSql;

namespace OfficeAI.Tests;

/// <summary>One real PostgreSQL container + one API host shared by all tests.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string CeoEmail = "ceo@test.local";
    public const string CeoPassword = "Test-Ceo-Password-1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public ScriptedProvider Scripted { get; } = new();
    public FakeClaudeBridge Claude { get; } = new();
    public string RepoRoot { get; } = Path.Combine(Path.GetTempPath(), "officeai-tests", Guid.NewGuid().ToString("N"));
    public string ConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
            b.UseSetting("Database:MigrateOnStartup", "true");
            b.UseSetting("Seed:CeoEmail", CeoEmail);
            b.UseSetting("Seed:CeoPassword", CeoPassword);
            b.UseSetting("Seed:CeoName", "Test CEO");
            b.UseSetting("RateLimits:LoginPerMinute", "1000");
            b.UseSetting("AiProviders:Simulated:Enabled", "true");
            b.UseSetting("AiProviders:Anthropic:ApiKey", "");
            b.UseSetting("AiProviders:OpenAI:BaseUrl", "http://127.0.0.1:9/v1"); // key checks fail fast, never reach the internet
            b.UseSetting("Workflow:AutoTick", "false"); // tests drive the engine explicitly
            b.UseSetting("Repositories:Root", RepoRoot);
            b.UseSetting("Repositories:AllowLocalRemotes", "true"); // tests use local bare repositories as "GitHub"
            b.UseSetting("Sandbox:UseLocalDocker", "true");
            b.UseSetting("Sandbox:TestTimeoutSeconds", "8");
            b.UseSetting("Integrations:AutoDispatch", "false");
            b.UseSetting("Integrations:AllowHttp", "true");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IAiProvider>(Scripted);
                s.AddSingleton<IAgentTool, ApprovalRequiredTool>();
                s.AddSingleton<OfficeAI.Application.Communication.IClaudeBridge>(Claude);
            });
        });
        _ = Factory.Server; // start host: runs migrations + seed
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient Anonymous()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = Anonymous();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        return client;
    }

    public Task<HttpClient> CeoAsync() => LoginAsync(CeoEmail, CeoPassword);

    /// <summary>Creates a human user with the given roles (as the CEO) and returns a logged-in client.</summary>
    public async Task<HttpClient> UserWithRolesAsync(params string[] roles)
    {
        var ceo = await CeoAsync();
        var email = $"user-{Guid.NewGuid():N}@test.local";
        const string password = "Test-User-Password-1";
        var res = await ceo.PostAsJsonAsync("/api/security/users", new { email, displayName = "Test user", password, isAgent = false, roles });
        res.EnsureSuccessStatusCode();
        return await LoginAsync(email, password);
    }

    /// <summary>A person with a login, the given roles and an employee profile, added by hand to each listed project.</summary>
    public async Task<(HttpClient Client, Guid EmployeeId)> PersonAsync(string[] roles, params Guid[] projectIds)
    {
        var ceo = await CeoAsync();
        var client = await UserWithRolesAsync(roles);
        var me = await client.GetAsync<OfficeAI.Api.Endpoints.MeDto>("/api/auth/me");
        var role = (await ceo.GetAsync<List<OfficeAI.Application.Organization.RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var employee = await ceo.PostAsync<OfficeAI.Application.Organization.EmployeeDto>("/api/employees",
            new { displayName = $"Person {Guid.NewGuid():N}"[..20], kind = "Human", departmentId = role.DepartmentId, roleDefinitionId = role.Id, userId = me.UserId });
        foreach (var p in projectIds)
            (await ceo.PostAsJsonAsync($"/api/projects/{p}/members/{employee.Id}", new { })).EnsureSuccessStatusCode();
        return (client, employee.Id);
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)res.StatusCode} {res.ReasonPhrase}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public static async Task<T> GetAsync<T>(this HttpClient c, string url) => await (await c.GetAsync(url)).ReadAsync<T>();

    public static async Task<T> PostAsync<T>(this HttpClient c, string url, object body) =>
        await (await c.PostAsJsonAsync(url, body, ApiFixture.Json)).ReadAsync<T>();

    public static async Task<T> PutAsync<T>(this HttpClient c, string url, object body) =>
        await (await c.PutAsJsonAsync(url, body, ApiFixture.Json)).ReadAsync<T>();
}
