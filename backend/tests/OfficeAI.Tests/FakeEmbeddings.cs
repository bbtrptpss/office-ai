using OfficeAI.Application.Knowledge;

namespace OfficeAI.Tests;

/// <summary>
/// Test embeddings: one dimension per concept, counting the concept's words in the text, so texts that share no words can
/// still mean the same thing. Off by default (tests that use it turn it on and back off: the fixture is shared).
/// </summary>
public sealed class FakeEmbeddings : IEmbeddingProvider
{
    private static readonly string[][] Concepts =
    [
        ["พักร้อน", "เที่ยว", "วันหยุดประจำปี", "annual leave", "vacation"],
        ["เงินเดือน", "ค่าจ้าง", "salary", "payroll"],
        ["server", "เซิร์ฟเวอร์", "deploy", "ล่ม"],
    ];

    public bool Enabled { get; set; }
    public bool Fail { get; set; }
    public string ModelName { get; set; } = "fake-embed";
    public int Calls;

    public bool IsAvailable => Enabled;
    public string Model => ModelName;

    public Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (Fail) throw new HttpRequestException("model not found");
        return Task.FromResult(texts.Select(t => Concepts.Select(words => (float)words.Count(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray()).ToArray());
    }
}
