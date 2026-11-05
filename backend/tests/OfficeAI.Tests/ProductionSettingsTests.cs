using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class ProductionSettingsTests(ApiFixture fx)
{
    private static IConfiguration Config(string token, string ceo, string db) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Security:RequireStrongSecrets"] = "true",
        ["Sandbox:RunnerToken"] = token,
        ["Seed:CeoPassword"] = ceo,
        ["ConnectionStrings:Default"] = $"Host=db;Username=officeai;Password={db}",
    }).Build();

    [Theory]
    [InlineData("change-me", "Good-Long-Password-1", "a-long-database-password", "Sandbox:RunnerToken")]
    [InlineData("a-long-runner-token-value", "ChangeMe-Dev-2026", "a-long-database-password", "Seed:CeoPassword")]
    [InlineData("a-long-runner-token-value", "Good-Long-Password-1", "officeai_dev", "the database password")]
    [InlineData("x7Qp2", "Good-Long-Password-1", "a-long-database-password", "Sandbox:RunnerToken")]
    public void Example_values_and_short_secrets_stop_the_server(string token, string ceo, string db, string named)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => StrongSecrets.Enforce(Config(token, ceo, db)));
        Assert.Contains(named, refused.Message);
        foreach (var value in new[] { token, ceo, db }.Where(v => v.Length < 16 || v.Contains("ChangeMe") || v.Contains("officeai_dev") || v == "change-me"))
            Assert.DoesNotContain(value, refused.Message); // the message never shows a secret
    }

    [Fact]
    public void Strong_values_pass_and_the_check_only_runs_when_asked_for()
    {
        StrongSecrets.Enforce(Config("a-long-runner-token-value", "Good-Long-Password-1", "a-long-database-password"));
        StrongSecrets.Enforce(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sandbox:RunnerToken"] = "change-me" }).Build());
    }

    private WebApplicationFactory<Program> BehindProxy(bool trustProxy) => fx.Factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Security:TrustProxy", trustProxy ? "true" : "false");
        b.UseSetting("Security:Hsts", "true");
        b.UseSetting("RateLimits:LoginPerMinute", "2");
        b.UseSetting("Database:MigrateOnStartup", "false");
    });

    private static async Task<HttpStatusCode> LoginFromAsync(HttpClient client, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email = "nobody@test.local", password = "wrong-password-1" }) };
        request.Headers.Add("X-Requested-With", "tests");
        request.Headers.Add("X-Forwarded-For", ip);
        request.Headers.Add("X-Forwarded-Proto", "https");
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Behind_the_proxy_each_visitor_has_their_own_login_limit_and_HTTPS_is_recognised()
    {
        await using var app = BehindProxy(trustProxy: true);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Host = "api.example.com";

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.2")); // someone else is not affected

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");
        Assert.True((await client.SendAsync(request)).Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Without_the_setting_a_visitor_cannot_fake_where_they_come_from()
    {
        await using var app = BehindProxy(trustProxy: false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Host = "api.example.com";

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFromAsync(client, "203.0.113.3")); // made-up addresses do not reset the limit

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");
        Assert.False((await client.SendAsync(request)).Headers.Contains("Strict-Transport-Security"));
    }
}
