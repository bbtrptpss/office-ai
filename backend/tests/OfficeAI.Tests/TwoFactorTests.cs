using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using OfficeAI.Api.Endpoints;
using OfficeAI.Application.Communication;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Tests;

/// <summary>Two-factor sign-in with an authenticator app, and the terminal that needs it.</summary>
[Collection(ApiCollection.Name)]
public class TwoFactorTests(ApiFixture fx)
{
    private const string Password = "Test-User-Password-1";

    /// <summary>RFC 6238 code (SHA-1, 30 s, 6 digits) for a base32 key, as an authenticator app computes it.</summary>
    public static string Totp(string sharedKey, DateTimeOffset? at = null)
    {
        var key = Base32(sharedKey.Replace(" ", "").ToUpperInvariant());
        var step = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return [.. bytes];
    }

    /// <summary>A new person with the given roles, signed in; returns the client and the email.</summary>
    private async Task<(HttpClient Client, string Email)> PersonAsync(params string[] roles)
    {
        var ceo = await fx.CeoAsync();
        var email = $"tf-{Guid.NewGuid():N}@test.local";
        (await ceo.PostAsJsonAsync("/api/security/users", new { email, displayName = "Two factor", password = Password, isAgent = false, roles })).EnsureSuccessStatusCode();
        return (await fx.LoginAsync(email, Password), email);
    }

    /// <summary>Turns two-factor sign-in on; returns the shared key and the recovery codes.</summary>
    private static async Task<(string Key, List<string> RecoveryCodes)> EnableAsync(HttpClient c)
    {
        var setup = await c.PostAsync<TwoFactorSetupDto>("/api/auth/2fa/setup", new { password = Password });
        Assert.StartsWith("otpauth://totp/Office%20AI%3A", setup.OtpauthUri);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/2fa/enable", new { code = "000000" })).StatusCode);
        var codes = await c.PostAsync<RecoveryCodesDto>("/api/auth/2fa/enable", new { code = Totp(setup.SharedKey, DateTimeOffset.UtcNow.AddSeconds(-30)) });
        return (setup.SharedKey, codes.Codes);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;

    [Fact]
    public async Task With_two_factor_on_the_password_alone_does_not_sign_in()
    {
        var (person, email) = await PersonAsync(SystemRoles.Member);
        Assert.False((await person.GetAsync<TwoFactorStatusDto>("/api/auth/2fa")).Enabled);
        var (key, recovery) = await EnableAsync(person);
        Assert.Equal(TwoFactorEndpoints.RecoveryCodeCount, recovery.Count);
        Assert.Equal(new TwoFactorStatusDto(true, 10), await person.GetAsync<TwoFactorStatusDto>("/api/auth/2fa"));
        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/api/auth/me")).StatusCode); // the session that turned it on stays
        Assert.Equal(HttpStatusCode.BadRequest, (await person.PostAsJsonAsync("/api/auth/2fa/setup", new { password = Password })).StatusCode); // no silent key swap

        var fresh = fx.Anonymous();
        var step1 = await fresh.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.True((await step1.ReadAsync<LoginResultDto>()).RequiresTwoFactor);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync("/api/auth/me")).StatusCode);
        var bad = await fresh.PostAsJsonAsync("/api/auth/login/2fa", new { code = "123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Contains("not correct", await bad.Content.ReadAsStringAsync());
        var loginCode = Totp(key); // computed once: the replay below must be the very same code, even across a 30-second boundary
        Assert.Equal(HttpStatusCode.NoContent, (await fresh.PostAsJsonAsync("/api/auth/login/2fa", new { code = loginCode })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/me")).StatusCode);

        // The same code a second time (e.g. seen over a shoulder) is refused.
        var replay = fx.Anonymous();
        await replay.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsJsonAsync("/api/auth/login/2fa", new { code = loginCode })).StatusCode);

        // A code step without the password step first is refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Anonymous().PostAsJsonAsync("/api/auth/login/2fa", new { code = Totp(key) })).StatusCode);

        // A recovery code works once.
        foreach (var expected in new[] { HttpStatusCode.NoContent, HttpStatusCode.Unauthorized })
        {
            var c = fx.Anonymous();
            await c.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            Assert.Equal(expected, (await c.PostAsJsonAsync("/api/auth/login/2fa", new { recoveryCode = recovery[0].ToLowerInvariant() })).StatusCode);
        }

        var ceo = await fx.CeoAsync();
        var audit = await ceo.GetAsync<List<AuditLogDto>>("/api/audit?take=50");
        Assert.Contains(audit, a => a.Action == "TwoFactorEnabled");
        Assert.Contains(audit, a => a.Action == "LoginFailed" && (a.NewValues ?? "").Contains("bad_2fa_code"));
        Assert.DoesNotContain(audit, a => (a.NewValues ?? "").Contains(key.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

        // Turning it off needs the password and a code; then the password signs in again.
        Assert.Equal(HttpStatusCode.BadRequest, (await person.PostAsJsonAsync("/api/auth/2fa/disable", new { password = "wrong-password-1", code = Totp(key, DateTimeOffset.UtcNow.AddSeconds(30)) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await person.PostAsJsonAsync("/api/auth/2fa/disable", new { password = Password, code = Totp(key, DateTimeOffset.UtcNow.AddSeconds(30)) })).StatusCode);
        await fx.LoginAsync(email, Password);
    }

    [Fact]
    public async Task A_session_without_the_password_cannot_start_two_factor_setup()
    {
        var (person, _) = await PersonAsync(SystemRoles.Ceo);
        foreach (var body in new object[] { new { }, new { password = "wrong-password-1" } })
        {
            var refused = await person.PostAsJsonAsync("/api/auth/2fa/setup", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("auth.bad_password", await CodeOf(refused));
            Assert.DoesNotContain("otpauth", await refused.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Anonymous().PostAsJsonAsync("/api/auth/2fa/setup", new { password = Password })).StatusCode);

        // Wrong passwords count towards the lockout, so a stolen session cannot guess its way in.
        var (lockedOut, _) = await PersonAsync(SystemRoles.Member);
        HttpResponseMessage last = new(HttpStatusCode.OK);
        for (var i = 0; i < 5; i++) last = await lockedOut.PostAsJsonAsync("/api/auth/2fa/setup", new { password = "wrong-password-1" });
        Assert.Equal((HttpStatusCode)423, last.StatusCode);
        Assert.Equal((HttpStatusCode)423, (await lockedOut.PostAsJsonAsync("/api/auth/2fa/setup", new { password = Password })).StatusCode);

        // The owner, with the password, carries on as before.
        var (key, _) = await EnableAsync(person);
        Assert.False(string.IsNullOrEmpty(key));
        Assert.Contains(await (await fx.CeoAsync()).GetAsync<List<AuditLogDto>>("/api/audit?take=100"), a => a.Action == "TwoFactorSetupFailed");
    }

    [Fact]
    public async Task The_terminal_needs_the_CEO_role_two_factor_sign_in_and_a_fresh_code()
    {
        var (ceo, _) = await PersonAsync(SystemRoles.Ceo);
        var off = await ceo.PostAsJsonAsync("/api/assistant/terminal", new { command = "git status" });
        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.Equal("terminal.2fa_required", await CodeOf(off));

        var (key, _) = await EnableAsync(ceo);
        var locked = await ceo.PostAsJsonAsync("/api/assistant/terminal", new { command = "git status" });
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Equal("terminal.locked", await CodeOf(locked));
        Assert.Null((await ceo.GetAsync<TerminalStatusDto>("/api/assistant/terminal/status")).OpenUntil);

        var wrong = await ceo.PostAsJsonAsync("/api/assistant/terminal/unlock", new { code = "000000" });
        Assert.Equal("terminal.bad_code", await CodeOf(wrong));
        var unlockCode = Totp(key);
        var open = await ceo.PostAsync<TerminalStatusDto>("/api/assistant/terminal/unlock", new { code = unlockCode });
        Assert.True(open.OpenUntil > DateTimeOffset.UtcNow.AddMinutes(14));
        Assert.Equal("terminal.bad_code", await CodeOf(await ceo.PostAsJsonAsync("/api/assistant/terminal/unlock", new { code = unlockCode }))); // once only

        var result = await ceo.PostAsync<TerminalResultDto>("/api/assistant/terminal", new { command = "git status" });
        Assert.Equal("ran: git status", result.Output);
        Assert.Equal("git status", fx.Claude.Commands.Last());
        var audit = await (await fx.CeoAsync()).GetAsync<List<AuditLogDto>>("/api/audit?entityType=Terminal&take=10");
        Assert.Contains(audit, a => a.Action == "TerminalCommand" && (a.NewValues ?? "").Contains("git status"));
        Assert.Contains(audit, a => a.Action == "TerminalUnlocked");
        Assert.Contains(audit, a => a.Action == "TerminalUnlockFailed");

        // Locking (or signing out) closes it again.
        (await ceo.PostAsJsonAsync("/api/assistant/terminal/lock", new { })).EnsureSuccessStatusCode();
        Assert.Equal("terminal.locked", await CodeOf(await ceo.PostAsJsonAsync("/api/assistant/terminal", new { command = "dir" })));

        // Other roles never get it, two-factor or not.
        var manager = await fx.UserWithRolesAsync(SystemRoles.Manager, SystemRoles.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/assistant/terminal", new { command = "dir" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/assistant/terminal/unlock", new { code = "000000" })).StatusCode);
        Assert.Equal("git status", fx.Claude.Commands.Last());
    }

    [Fact]
    public void The_terminal_closes_after_being_idle_and_when_the_security_stamp_changes()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var gate = new TerminalGate(clock);
        var user = Guid.NewGuid();
        gate.Unlock(user, "stamp-1");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(gate.Use(user, "stamp-1")); // using it restarts the idle timer
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(gate.Use(user, "stamp-1"));
        Assert.False(gate.Use(user, "stamp-2")); // password changed or two-factor turned off
        gate.Unlock(user, "stamp-2");
        clock.Advance(TerminalGate.IdleTimeout + TimeSpan.FromSeconds(1));
        Assert.False(gate.Use(user, "stamp-2"));
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
