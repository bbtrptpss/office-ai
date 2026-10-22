using Microsoft.Extensions.Options;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Reports;

namespace OfficeAI.Api.Infrastructure;

/// <summary>
/// Posts the previous day's report once the day is over (after midnight in Reports:Daily:TimeZone). Checks every minute and
/// posts each day only once, so a restart or a machine that was off at midnight still gets the report as soon as it is up.
/// </summary>
public sealed class DailyReportWorker(IServiceScopeFactory scopes, IOptions<DailyReportOptions> options, ILogger<DailyReportWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var acting = ActingAs.Use(new ActingIdentity(null, "Daily report", IsAgent: false, "daily-report"));
                await using var scope = scopes.CreateAsyncScope();
                var reports = scope.ServiceProvider.GetRequiredService<DailyReportService>();
                if (await reports.PublishAsync(reports.Yesterday, stopping)) log.LogInformation("Daily report for {Day} posted", reports.Yesterday);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Daily report failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stopping));
    }
}
