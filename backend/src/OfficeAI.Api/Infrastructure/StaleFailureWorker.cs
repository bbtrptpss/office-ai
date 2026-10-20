using OfficeAI.Application.Organization;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Sets AI employees whose failed work was cancelled or closed back to Available, at start-up and then every half minute. Disabled with Organization:ClearStaleFailures=false.</summary>
public sealed class StaleFailureWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<StaleFailureWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!config.GetValue("Organization:ClearStaleFailures", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, config.GetValue("Organization:StaleFailureIntervalSeconds", 30))));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var cleared = await scope.ServiceProvider.GetRequiredService<StaleFailureService>().ClearAsync(stopping);
                if (cleared > 0) log.LogInformation("{Count} employees back to Available: their failed work was cancelled or closed", cleared);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Clearing stale employee failures failed");
            }
        } while (await timer.WaitForNextTickAsync(stopping));
    }
}
