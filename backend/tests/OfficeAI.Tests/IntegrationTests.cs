using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Integrations;
using OfficeAI.Application.Workflows;
using OfficeAI.Domain.Integrations;
using OfficeAI.Domain.Workflows;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

[Collection(ApiCollection.Name)]
public class IntegrationTests(ApiFixture fx)
{
    /// <summary>A real HTTP endpoint on a random local port that records what it receives.</summary>
    private static async Task<(WebApplication App, string Url, ConcurrentQueue<JsonElement> Received)> ReceiverAsync()
    {
        var received = new ConcurrentQueue<JsonElement>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/hook", async (HttpRequest req) =>
        {
            received.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(req.Body));
            return Results.Ok();
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, $"{address}/hook", received);
    }

    private async Task<int> DispatchAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IntegrationDispatcher>().TickAsync(default);
    }

    [Fact]
    public async Task Approval_requests_are_posted_once_to_the_configured_webhook()
    {
        var (app, url, received) = await ReceiverAsync();
        await using var _ = app;
        var env = $"INTEGRATION_TEST_{Guid.NewGuid():N}".ToUpperInvariant();
        Environment.SetEnvironmentVariable(env, url);

        var ceo = await fx.CeoAsync();
        var integration = await ceo.PostAsync<IntegrationDto>("/api/integrations", new { name = "Team chat", secretEnvVar = env, events = new[] { "approval.requested" } });
        Assert.True(integration.SecretPresent);
        await Task.Delay(20);

        var title = $"Approve something {Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Escalation, title, "details", new { x = 1 }, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await DispatchAsync();
        await DispatchAsync(); // nothing new: no duplicate
        var mine = received.Where(r => r.GetProperty("title").GetString() == title).ToList();
        var message = Assert.Single(mine);
        Assert.Equal("approval.requested", message.GetProperty("event").GetString());

        var deliveries = await ceo.GetAsync<List<DeliveryDto>>($"/api/integrations/{integration.Id}/deliveries");
        Assert.Contains(deliveries, d => d.Title == title && d.Status == DeliveryStatus.Sent && d.StatusCode == 200);

        // Turning it off stops deliveries.
        await ceo.PutAsync<IntegrationDto>($"/api/integrations/{integration.Id}", new { name = "Team chat", secretEnvVar = env, events = new[] { "approval.requested" }, isEnabled = false });
    }

    [Fact]
    public async Task Only_INTEGRATION_variables_can_be_used_as_secrets()
    {
        var ceo = await fx.CeoAsync();
        foreach (var name in new[] { "ConnectionStrings__Default", "ANTHROPIC_API_KEY", "INTEGRATION_lower", "PATH" })
        {
            var res = await ceo.PostAsJsonAsync("/api/integrations", new { name = "x", secretEnvVar = name, events = new[] { "run.failed" } });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        var bad = await ceo.PostAsJsonAsync("/api/integrations", new { name = "x", secretEnvVar = "INTEGRATION_OK", events = new[] { "everything" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Null(IntegrationService.ReadSecret("ANTHROPIC_API_KEY"));
    }

    [Fact]
    public async Task Missing_secret_is_recorded_as_a_failed_delivery()
    {
        var ceo = await fx.CeoAsync();
        var env = $"INTEGRATION_MISSING_{Guid.NewGuid():N}".ToUpperInvariant();
        var integration = await ceo.PostAsync<IntegrationDto>("/api/integrations", new { name = "Broken", secretEnvVar = env, events = new[] { "approval.requested" } });
        Assert.False(integration.SecretPresent);
        await Task.Delay(20);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ApprovalRequests.Add(Approvals.Create(ApprovalKind.Escalation, "Needs a webhook", null, new { y = 1 }, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        await DispatchAsync();
        var d = Assert.Single(await ceo.GetAsync<List<DeliveryDto>>($"/api/integrations/{integration.Id}/deliveries"), x => x.Title == "Needs a webhook");
        Assert.Equal(DeliveryStatus.Failed, d.Status);
        Assert.Contains("not set", d.Error);
        await ceo.PutAsync<IntegrationDto>($"/api/integrations/{integration.Id}", new { name = "Broken", secretEnvVar = env, events = new[] { "approval.requested" }, isEnabled = false });
    }
}
