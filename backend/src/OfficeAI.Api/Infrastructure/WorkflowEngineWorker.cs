using OfficeAI.Application.Workflows;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Ticks the workflow engine on a fixed interval. Disabled with Workflow:AutoTick=false (tests tick manually).</summary>
public sealed class WorkflowEngineWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<WorkflowEngineWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!config.GetValue("Workflow:AutoTick", true)) return;
        var interval = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Workflow:TickSeconds", 3)));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stopping))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().TickAsync(stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Workflow engine tick failed");
            }
        }
    }
}
