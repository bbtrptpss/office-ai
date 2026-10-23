using OfficeAI.Application.Knowledge;

namespace OfficeAI.Api.Infrastructure;

/// <summary>Keeps knowledge vectors current every half minute while an embedding model is configured. Disabled with Knowledge:EmbeddingWorker=false.</summary>
public sealed class KnowledgeEmbeddingWorker(IServiceScopeFactory scopes, IEmbeddingProvider embeddings, IConfiguration config, ILogger<KnowledgeEmbeddingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!embeddings.IsAvailable || !config.GetValue("Knowledge:EmbeddingWorker", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, config.GetValue("Knowledge:EmbeddingIntervalSeconds", 30))));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var done = await scope.ServiceProvider.GetRequiredService<KnowledgeEmbeddingIndexer>().IndexPendingAsync(stopping);
                if (done > 0) log.LogInformation("Embedded {Count} knowledge entries with {Model}", done, embeddings.Model);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Embedding knowledge failed");
            }
        } while (await timer.WaitForNextTickAsync(stopping));
    }
}
