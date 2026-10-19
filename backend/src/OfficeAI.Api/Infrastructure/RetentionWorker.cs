using OfficeAI.Application.Abstractions;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Applies the data retention policy once a day. Does nothing while the policy keeps everything (the default).</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<RetentionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!config.GetValue("Retention:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, config.GetValue("Retention:IntervalHours", 24))));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            try
            {
                using var acting = ActingAs.Use(new ActingIdentity(null, "Retention", IsAgent: false, "retention"));
                await using var scope = scopes.CreateAsyncScope();
                var counts = await scope.ServiceProvider.GetRequiredService<RetentionService>().ApplyAsync("schedule", stopping);
                if (counts is not null) log.LogInformation("Retention removed {Total} items: {@Counts}", counts.Total, counts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Retention clean-up failed");
            }
        }
    }
}
