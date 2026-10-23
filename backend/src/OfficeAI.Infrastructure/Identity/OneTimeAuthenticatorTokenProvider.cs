using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace OfficeAI.Infrastructure.Identity;

/// <summary>
/// Authenticator (TOTP, RFC 6238: SHA-1, 30-second steps, 6 digits) that accepts each code once: the last accepted step is
/// kept in the user's tokens, so a code seen over someone's shoulder or replayed from a request cannot be used again.
/// Accepts the previous, current and next step for clock drift. Replaces Identity's AuthenticatorTokenProvider, which allows reuse.
/// </summary>
public sealed class OneTimeAuthenticatorTokenProvider : IUserTwoFactorTokenProvider<AppUser>
{
    public const string LoginProvider = "OfficeAI";
    public const string LastStepToken = "TotpLastStep";
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user) =>
        !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));

    /// <summary>Codes come from the person's app, never from the server.</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user) => Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user)
    {
        if (token.Length != 6 || !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var code)) return false;
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key)) return false;
        var secret = Base32Decode(key);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)Step.TotalSeconds;
        long? matched = null;
        for (var s = now - 1; s <= now + 1; s++)
            if (Code(secret, s) == code) matched = s;
        if (matched is not { } step) return false;
        var last = await manager.GetAuthenticationTokenAsync(user, LoginProvider, LastStepToken);
        if (long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var used) && used >= step) return false; // already used
        await manager.SetAuthenticationTokenAsync(user, LoginProvider, LastStepToken, step.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    internal static int Code(byte[] secret, long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return binary % 1_000_000;
    }

    internal static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in input.ToUpperInvariant())
        {
            var v = alphabet.IndexOf(c);
            if (v < 0) continue; // padding, spaces
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
