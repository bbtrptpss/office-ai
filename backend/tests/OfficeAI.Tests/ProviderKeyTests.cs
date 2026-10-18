using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>Provider keys entered on the AI settings page: encrypted, write-only, people only, effective without a restart.</summary>
[Collection(ApiCollection.Name)]
public class ProviderKeyTests(ApiFixture fx)
{
    private const string Key = "sk-test-0123456789abcdefWXYZ";

    private static ProviderDto OpenAi(List<ProviderDto> all) => all.Single(p => p.Name == "openai");

    [Fact]
    public async Task A_key_saved_in_settings_enables_the_provider_and_is_never_revealed()
    {
        var ceo = await fx.CeoAsync();
        try
        {
            var before = OpenAi(await ceo.GetAsync<List<ProviderDto>>("/api/ai/providers"));
            Assert.False(before.IsAvailable);

            var saved = await ceo.PutAsync<ProviderDto>("/api/ai/providers/openai/key", new { apiKey = $"  {Key}  " });
            Assert.True(saved.IsAvailable);
            Assert.Equal("settings", saved.KeySource);
            Assert.Equal("WXYZ", saved.KeyLast4);

            // Never readable again: not in any API answer, not in the database, not in the audit log.
            var listing = await (await ceo.GetAsync("/api/ai/providers")).Content.ReadAsStringAsync();
            Assert.DoesNotContain(Key, listing);
            await using (var scope = fx.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var row = await db.AiProviderCredentials.SingleAsync(c => c.Provider == "openai");
                Assert.DoesNotContain(Key, row.EncryptedSecret);
                var audit = await db.AuditLogs.Where(a => a.EntityType == "AiProviderCredential").ToListAsync();
                Assert.NotEmpty(audit);
                Assert.All(audit, a => Assert.DoesNotContain(Key, (a.OldValues ?? "") + (a.NewValues ?? "")));
                Assert.All(audit, a => Assert.DoesNotContain("EncryptedSecret", (a.OldValues ?? "") + (a.NewValues ?? "")));
            }

            // The check reports problems instead of failing (the test endpoint is unreachable on purpose).
            var check = await ceo.PostAsync<VerifyProviderDto>("/api/ai/providers/openai/verify", new { });
            Assert.False(check.Ok);
            Assert.NotNull(check.Problem);
            Assert.DoesNotContain(Key, check.Problem);

            var removed = await (await ceo.DeleteAsync("/api/ai/providers/openai/key")).ReadAsync<ProviderDto>();
            Assert.False(removed.IsAvailable);
            Assert.Null(removed.KeySource);
        }
        finally
        {
            await ceo.DeleteAsync("/api/ai/providers/openai/key");
        }
    }

    [Fact]
    public async Task Only_people_with_agent_manage_can_set_keys_and_bad_input_is_refused()
    {
        var ceo = await fx.CeoAsync();
        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync("/api/ai/providers/openai/key", new { apiKey = Key })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync("/api/ai/providers/openai/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/ai/providers/openai/key", new { apiKey = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/ai/providers/openai/key", new { apiKey = "has a space inside it" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/ai/providers/simulated/key", new { apiKey = Key })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PutAsJsonAsync("/api/ai/providers/nobody/key", new { apiKey = Key })).StatusCode);
    }

    [Fact]
    public async Task People_add_models_with_prices_for_any_provider()
    {
        var ceo = await fx.CeoAsync();
        var id = $"gpt-test-{Guid.NewGuid():N}"[..20];
        var model = await ceo.PostAsync<AiModelDto>("/api/ai/models",
            new { provider = "openai", modelId = id, displayName = "GPT test", tier = "Standard", inputUsdPerMTok = 1.25m, outputUsdPerMTok = 10m });
        Assert.Equal(1.25m, model.InputUsdPerMTok);
        Assert.Contains(await ceo.GetAsync<List<AiModelDto>>("/api/ai/models"), m => m.ModelId == id);

        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/ai/models",
            new { provider = "openai", modelId = id, displayName = "dup", tier = "Standard", inputUsdPerMTok = 1m, outputUsdPerMTok = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/ai/models",
            new { provider = "openai", modelId = "free-model", displayName = "free", tier = "Standard", inputUsdPerMTok = 0m, outputUsdPerMTok = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ceo.PostAsJsonAsync("/api/ai/models",
            new { provider = "nobody", modelId = "x", displayName = "x", tier = "Standard", inputUsdPerMTok = 1m, outputUsdPerMTok = 1m })).StatusCode);
        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/ai/models",
            new { provider = "openai", modelId = "m2", displayName = "x", tier = "Standard", inputUsdPerMTok = 1m, outputUsdPerMTok = 1m })).StatusCode);
    }
}
