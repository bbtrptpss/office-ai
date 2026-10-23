using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Knowledge;

namespace OfficeAI.Application.Knowledge;

/// <summary>Turns text into vectors whose cosine similarity means "about the same thing" (e.g. Ollama with bge-m3, which handles Thai).</summary>
public interface IEmbeddingProvider
{
    /// <summary>Configured (a model is set). It may still fail at call time, e.g. when the model is not pulled yet.</summary>
    bool IsAvailable { get; }

    /// <summary>Stored with each vector, so changing the model re-embeds everything.</summary>
    string Model { get; }

    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>
/// Knowledge search by keyword and, when embeddings are available, by meaning. The two rankings are fused (reciprocal rank),
/// and a meaning-only hit must clear <see cref="MinSimilarity"/>, so an unrelated question still finds nothing and the AI
/// says it has no information instead of answering from the closest unrelated policy. Without embeddings (or when the
/// embedding call fails) it is exactly the keyword search.
/// </summary>
public sealed class KnowledgeSearch(IAppDbContext db, IEmbeddingProvider embeddings, ILogger<KnowledgeSearch> log)
{
    public const double MinSimilarity = 0.55;
    private const int Rrf = 60;

    public async Task<List<KnowledgeEntry>> SearchAsync(string query, Guid? projectId, KnowledgeStatus? status, int take, CancellationToken ct)
    {
        var keyword = KnowledgeService.SearchQuery(db, query, projectId);
        if (status is { } s) keyword = keyword.Where(k => k.Status == s);
        var byKeyword = await keyword.Take(Math.Max(take, 20)).ToListAsync(ct);

        var byMeaning = await SemanticAsync(query, projectId, status, ct);
        if (byMeaning.Count == 0) return byKeyword.Take(take).ToList();

        var score = new Dictionary<Guid, double>();
        for (var i = 0; i < byKeyword.Count; i++) score[byKeyword[i].Id] = 1.0 / (Rrf + i + 1);
        for (var i = 0; i < byMeaning.Count; i++) score[byMeaning[i].Entry.Id] = score.GetValueOrDefault(byMeaning[i].Entry.Id) + 1.0 / (Rrf + i + 1);
        var entries = byKeyword.Concat(byMeaning.Select(m => m.Entry)).DistinctBy(e => e.Id);
        return entries.OrderByDescending(e => score[e.Id]).ThenByDescending(e => e.ValidatedAt).Take(take).ToList();
    }

    private async Task<List<(KnowledgeEntry Entry, double Similarity)>> SemanticAsync(string query, Guid? projectId, KnowledgeStatus? status, CancellationToken ct)
    {
        if (!embeddings.IsAvailable) return [];
        var stored = db.KnowledgeEmbeddings.AsNoTracking().Where(v => v.Model == embeddings.Model);
        var candidates = await db.KnowledgeEntries.AsNoTracking()
            .Where(k => (k.ProjectId == null || k.ProjectId == projectId) && (status == null || k.Status == status))
            .Join(stored, k => k.Id, v => v.KnowledgeId, (k, v) => new { k, v.Vector })
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];
        float[] q;
        try
        {
            q = (await embeddings.EmbedAsync([query], ct))[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("Knowledge search by meaning unavailable ({Error}); using keywords only", ex.Message);
            return [];
        }
        return candidates.Select(c => (c.k, Similarity: Cosine(q, c.Vector)))
            .Where(x => x.Similarity >= MinSimilarity).OrderByDescending(x => x.Similarity).Take(20).ToList();
    }

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }

    /// <summary>What gets embedded for an entry; its hash tells the indexer when an entry changed.</summary>
    public static string Text(KnowledgeEntry k) => $"{k.Title}\n{string.Join(", ", k.Tags)}\n{(k.Body.Length > 4000 ? k.Body[..4000] : k.Body)}";

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
}

public record KnowledgeSearchStatusDto(bool ByMeaning, string? Model, int Indexed, int Entries);

/// <summary>Keeps every non-archived entry's vector current: new or edited entries, or a changed model. Run by a background worker.</summary>
public sealed class KnowledgeEmbeddingIndexer(IAppDbContext db, IEmbeddingProvider embeddings, TimeProvider clock, ILogger<KnowledgeEmbeddingIndexer> log)
{
    public const int Batch = 16;

    /// <summary>Whether search by meaning is on, and how many non-archived entries already have a vector for the current model.</summary>
    public async Task<KnowledgeSearchStatusDto> StatusAsync(CancellationToken ct)
    {
        var entries = db.KnowledgeEntries.AsNoTracking().Where(k => k.Status != KnowledgeStatus.Archived);
        var total = await entries.CountAsync(ct);
        if (!embeddings.IsAvailable) return new KnowledgeSearchStatusDto(false, null, 0, total);
        var indexed = await entries.CountAsync(k => db.KnowledgeEmbeddings.Any(v => v.KnowledgeId == k.Id && v.Model == embeddings.Model), ct);
        return new KnowledgeSearchStatusDto(true, embeddings.Model, indexed, total);
    }

    /// <summary>Embeds what is missing or stale; returns how many entries were (re)embedded. Archived entries lose their vector.</summary>
    public async Task<int> IndexPendingAsync(CancellationToken ct)
    {
        if (!embeddings.IsAvailable) return 0;
        var entries = await db.KnowledgeEntries.AsNoTracking().Where(k => k.Status != KnowledgeStatus.Archived).ToListAsync(ct);
        var stored = await db.KnowledgeEmbeddings.ToDictionaryAsync(v => v.KnowledgeId, ct);
        var live = entries.Select(e => e.Id).ToHashSet();
        db.KnowledgeEmbeddings.RemoveRange(stored.Values.Where(v => !live.Contains(v.KnowledgeId)));

        var pending = entries.Select(e => (Entry: e, Text: KnowledgeSearch.Text(e)))
            .Select(x => (x.Entry, x.Text, Hash: KnowledgeSearch.Hash(x.Text)))
            .Where(x => !stored.TryGetValue(x.Entry.Id, out var v) || v.Model != embeddings.Model || v.ContentHash != x.Hash)
            .ToList();
        var done = 0;
        foreach (var chunk in pending.Chunk(Batch))
        {
            float[][] vectors;
            try
            {
                vectors = await embeddings.EmbedAsync(chunk.Select(x => x.Text).ToList(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Embedding knowledge failed ({Error}); will retry", ex.Message);
                break;
            }
            for (var i = 0; i < chunk.Length; i++)
            {
                var (entry, _, hash) = chunk[i];
                if (!stored.TryGetValue(entry.Id, out var row)) db.KnowledgeEmbeddings.Add(row = new KnowledgeEmbedding { KnowledgeId = entry.Id, Model = "", ContentHash = "", Vector = [] });
                (row.Model, row.ContentHash, row.Vector, row.EmbeddedAt) = (embeddings.Model, hash, vectors[i], clock.GetUtcNow());
            }
            done += chunk.Length;
        }
        await db.SaveChangesAsync(ct);
        return done;
    }
}
