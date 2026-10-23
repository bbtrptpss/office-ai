using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Application.Communication;
using OfficeAI.Application.Knowledge;
using OfficeAI.Application.Organization;
using OfficeAI.Domain.Agents;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>Knowledge search by meaning (embeddings) on top of the keyword search, and the fallback when embeddings are off or fail.</summary>
[Collection(ApiCollection.Name)]
public class KnowledgeSearchTests(ApiFixture fx)
{
    private async Task<int> IndexAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<KnowledgeEmbeddingIndexer>().IndexPendingAsync(CancellationToken.None);
    }

    /// <summary>Embeddings are shared by every test in the collection: on only inside <paramref name="body"/>.</summary>
    private async Task WithEmbeddingsAsync(Func<Task> body)
    {
        fx.Embeddings.Enabled = true;
        try { await body(); }
        finally { (fx.Embeddings.Enabled, fx.Embeddings.Fail, fx.Embeddings.ModelName) = (false, false, "fake-embed"); }
    }

    private static async Task<List<KnowledgeDto>> SearchAsync(HttpClient c, string q) =>
        await c.GetAsync<List<KnowledgeDto>>($"/api/knowledge?q={Uri.EscapeDataString(q)}");

    [Fact]
    public async Task A_question_in_other_words_finds_the_entry_by_meaning_and_an_unrelated_one_does_not()
    {
        var ceo = await fx.CeoAsync();
        var title = $"สิทธิวันหยุดประจำปี {Guid.NewGuid():N}"[..30];
        var entry = await ceo.PostAsync<KnowledgeDto>("/api/knowledge", new { kind = "Policy", title, body = "พนักงานที่ทำงานครบ 1 ปีได้วันหยุดประจำปี 10 วัน" });
        const string question = "ไปเที่ยวได้กี่วันต่อปีครับ"; // shares no word with the entry

        Assert.DoesNotContain(await SearchAsync(ceo, question), k => k.Id == entry.Id); // keywords alone miss it
        Assert.False((await ceo.GetAsync<KnowledgeSearchStatusDto>("/api/knowledge/search-status")).ByMeaning);
        await WithEmbeddingsAsync(async () =>
        {
            Assert.True(await IndexAsync() >= 1);
            Assert.Equal(0, await IndexAsync()); // nothing changed: nothing re-embedded
            var status = await ceo.GetAsync<KnowledgeSearchStatusDto>("/api/knowledge/search-status");
            Assert.True(status.ByMeaning);
            Assert.Equal(("fake-embed", status.Entries), (status.Model, status.Indexed)); // every non-archived entry has a vector
            Assert.Contains(await SearchAsync(ceo, question), k => k.Id == entry.Id);
            Assert.DoesNotContain(await SearchAsync(ceo, "ค่าส่งพัสดุไปต่างจังหวัดเท่าไร"), k => k.Id == entry.Id); // below the similarity floor

            // Keyword hits still come first-class: searching the title finds it with or without meaning.
            Assert.Equal(entry.Id, (await SearchAsync(ceo, title)).First().Id);

            // A failing embedding call falls back to keywords instead of failing the search.
            fx.Embeddings.Fail = true;
            Assert.DoesNotContain(await SearchAsync(ceo, question), k => k.Id == entry.Id);
            Assert.Equal(entry.Id, (await SearchAsync(ceo, title)).First().Id);
            fx.Embeddings.Fail = false;

            // A new model re-embeds everything; archiving drops the entry's vector.
            fx.Embeddings.ModelName = "fake-embed-2";
            Assert.True(await IndexAsync() >= 1);
            (await ceo.PostAsJsonAsync($"/api/knowledge/{entry.Id}/status", new { status = "Archived" }, ApiFixture.Json)).EnsureSuccessStatusCode();
            await IndexAsync();
            await using var scope = fx.Factory.Services.CreateAsyncScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().KnowledgeEmbeddings.AnyAsync(v => v.KnowledgeId == entry.Id));
        });
    }

    [Fact]
    public async Task Chat_answers_get_knowledge_found_by_meaning()
    {
        var ceo = await fx.CeoAsync();
        var body = $"เงินเดือนออกทุกวันที่ 25 ของเดือน ({Guid.NewGuid():N})";
        await ceo.PostAsync<KnowledgeDto>("/api/knowledge", new { kind = "Policy", title = "รอบการจ่าย payroll", body });

        var role = (await ceo.GetAsync<List<RoleDefinitionDto>>("/api/role-definitions")).First(r => r.Code == "BACKEND");
        var ai = await ceo.PostAsync<EmployeeDto>("/api/employees", new { displayName = $"Pay {Guid.NewGuid():N}"[..20], kind = "AI", departmentId = role.DepartmentId, roleDefinitionId = role.Id });
        var modelId = $"script-{Guid.NewGuid():N}";
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiModels.Add(new AiModel { Provider = ScriptedProvider.ProviderName, ModelId = modelId, DisplayName = modelId, InputUsdPerMTok = 1m, OutputUsdPerMTok = 1m });
            await db.SaveChangesAsync();
        }
        AiRequest? seen = null;
        fx.Scripted.Register(modelId, (req, _) => { seen = req; return Task.FromResult(ScriptedProvider.Text("วันที่ 25 ครับ")); });
        var c = await ceo.GetAsync<AgentConfigDto>($"/api/agents/{ai.Id}/config");
        (await ceo.PutAsJsonAsync($"/api/agents/{ai.Id}/config", new
        {
            provider = ScriptedProvider.ProviderName, modelId, effort = "Medium", c.MaxTokensPerCall, c.MaxTurnsPerRun, c.MaxToolCallsPerRun,
            c.MaxTokensPerRun, c.TimeoutSeconds, monthlyBudgetUsd = 5m, isEnabled = true,
        }, ApiFixture.Json)).EnsureSuccessStatusCode();

        await WithEmbeddingsAsync(async () =>
        {
            await IndexAsync();
            var general = (await ceo.GetAsync<List<ChannelDto>>("/api/chat/channels")).Single(x => x.Name == ChatService.GeneralChannel);
            await ceo.PostAsync<MessageDto>($"/api/chat/channels/{general.Id}/messages",
                new { body = $"@{ai.DisplayName} ค่าจ้างโอนเข้าบัญชีวันไหน", mentionedEmployeeIds = new[] { ai.Id } }); // "ค่าจ้าง", not "เงินเดือน"
            for (var i = 0; i < 100 && seen is null; i++) await Task.Delay(100);
            Assert.Contains(body, seen!.SystemPrompt);
        });
    }
}
