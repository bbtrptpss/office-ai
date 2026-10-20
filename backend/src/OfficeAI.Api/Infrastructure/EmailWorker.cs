using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Integrations;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Emails copies of personal notifications to people who opted in. Disabled with Email:AutoDispatch=false.</summary>
public sealed class EmailWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<EmailWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!config.GetValue("Email:AutoDispatch", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, config.GetValue("Email:IntervalSeconds", 20))));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            try
            {
                using var acting = ActingAs.Use(new ActingIdentity(null, "Email", IsAgent: false, "email"));
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().TickAsync(stopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Email dispatch failed");
            }
        }
    }
}
