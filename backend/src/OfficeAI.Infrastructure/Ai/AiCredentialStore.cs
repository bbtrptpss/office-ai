using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Application.Agents;
using OfficeAI.Domain.Agents;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Infrastructure.Ai;

/// <summary>
/// AI provider keys entered on the AI settings page. Encrypted with DataProtection before they reach the database
/// (DataProtection keys are themselves certificate-protected in production) and never returned to clients.
/// Decrypted keys are cached briefly so several API instances pick up a change within <see cref="CacheFor"/>.
/// </summary>
public sealed class AiCredentialStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, TimeProvider clock) : IAiCredentials
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly IDataProtector _protector = protection.CreateProtector("OfficeAI.AiProviderKeys.v1");
    private readonly Lock _lock = new();
    private Dictionary<string, (string Key, string Last4)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public string? GetKey(string provider) => Snapshot().TryGetValue(provider, out var e) ? e.Key : null;

    public string? Last4(string provider) => Snapshot().TryGetValue(provider, out var e) ? e.Last4 : null;

    private Dictionary<string, (string Key, string Last4)> Snapshot()
    {
        lock (_lock)
        {
            if (clock.GetUtcNow() - _loadedAt < CacheFor) return _cache;
            using var scope = scopes.CreateScope();
            var rows = scope.ServiceProvider.GetRequiredService<AppDbContext>().AiProviderCredentials.AsNoTracking().ToList();
            var next = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                try
                {
                    next[r.Provider] = (_protector.Unprotect(r.EncryptedSecret), r.Last4);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // Encrypted with keys this server no longer has: treat as not set, so the environment fallback applies.
                }
            }
            (_cache, _loadedAt) = (next, clock.GetUtcNow());
            return _cache;
        }
    }

    public async Task SetAsync(string provider, string? key, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AiProviderCredentials.FirstOrDefaultAsync(c => c.Provider == provider, ct);
        if (key is null)
        {
            if (row is not null) db.AiProviderCredentials.Remove(row);
        }
        else
        {
            row ??= db.AiProviderCredentials.Add(new AiProviderCredential { Provider = provider, EncryptedSecret = "", Last4 = "" }).Entity;
            row.EncryptedSecret = _protector.Protect(key);
            row.Last4 = key.Length >= 4 ? key[^4..] : key;
        }
        await db.SaveChangesAsync(ct);
        lock (_lock) _loadedAt = DateTimeOffset.MinValue; // reload on next read
    }
}
