using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Integrations;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Posts notifications to configured webhooks every few seconds. Disabled with Integrations:AutoDispatch=false.</summary>
public sealed class IntegrationWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<IntegrationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!config.GetValue("Integrations:AutoDispatch", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, config.GetValue("Integrations:IntervalSeconds", 10))));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            try
            {
                using var acting = ActingAs.Use(new ActingIdentity(null, "Integrations", IsAgent: false, "integrations"));
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IntegrationDispatcher>().TickAsync(stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Integration dispatch failed");
            }
        }
    }
}
