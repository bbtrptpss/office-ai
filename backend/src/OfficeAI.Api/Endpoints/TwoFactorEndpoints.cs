using Microsoft.AspNetCore.Identity;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Communication;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Security;
using OfficeAI.Api.Infrastructure;
using OfficeAI.Infrastructure.Identity;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Api.Endpoints;

public record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft);
/// <param name="SharedKey">Base32 key in groups of four, for typing into an authenticator app.</param>
/// <param name="OtpauthUri">The same key as an otpauth:// link (what a QR code holds).</param>
public record TwoFactorSetupDto(string SharedKey, string OtpauthUri);
public record TwoFactorCodeRequest(string? Code);
public record TwoFactorSetupRequest(string? Password);
/// <param name="Code">An authenticator code, or one of the recovery codes.</param>
public record TwoFactorDisableRequest(string? Password, string? Code);
public record RecoveryCodesDto(List<string> Codes);
/// <param name="RecoveryCode">Used instead of <paramref name="Code"/> when the authenticator is lost; each works once.</param>
public record LoginTwoFactorRequest(string? Code, string? RecoveryCode);
public record LoginResultDto(bool RequiresTwoFactor);
public record TerminalStatusDto(bool TwoFactorEnabled, DateTimeOffset? OpenUntil);

/// <summary>
/// Two-factor sign-in with an authenticator app (TOTP, ASP.NET Identity), and the step-up check that opens the
/// dashboard terminal. Keys and recovery codes live in Identity's UserTokens table and are never written to the audit log.
/// </summary>
public static class TwoFactorEndpoints
{
    public const string Issuer = "Office AI";
    public const int RecoveryCodeCount = 10;

    public static string Normalize(string? code) => (code ?? "").Replace(" ", "").Replace("-", "").Trim();

    /// <summary>Recovery codes are stored with their dash (XXXXX-XXXXX); only spaces and case are forgiven.</summary>
    public static string NormalizeRecovery(string? code) => (code ?? "").Replace(" ", "").Trim().ToUpperInvariant();

    private static Task<bool> VerifyCodeAsync(UserManager<AppUser> users, AppUser user, string? code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Normalize(code));

    private static async Task<AppUser> MeAsync(ICurrentUser me, UserManager<AppUser> users) =>
        (me.UserId is { } id ? await users.FindByIdAsync(id.ToString()) : null) is { IsAgent: false } user
            ? user : throw new ForbiddenException("Only a person can manage two-factor sign-in.");

    private static string Grouped(string key) =>
        string.Join(" ", Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant();

    public static void Map(RouteGroupBuilder auth)
    {
        // Second step of a sign-in whose password was right: the password step left a short-lived two-factor cookie.
        auth.MapPost("/login/2fa", async (LoginTwoFactorRequest req, SignInManager<AppUser> signIn, SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await signIn.GetTwoFactorAuthenticationUserAsync();
            if (user is null) return Results.Problem(statusCode: 401, title: "Sign in with your password first.");
            var recovery = !string.IsNullOrWhiteSpace(req.RecoveryCode);
            var result = recovery
                ? await signIn.TwoFactorRecoveryCodeSignInAsync(NormalizeRecovery(req.RecoveryCode))
                : await signIn.TwoFactorAuthenticatorSignInAsync(Normalize(req.Code), isPersistent: false, rememberClient: false);
            if (result.Succeeded)
            {
                await audit.WriteAsync(AuditActions.LoginSucceeded, "User", user.Id.ToString(), user.Id, user.DisplayName, new { TwoFactor = recovery ? "recovery_code" : "authenticator" }, ct);
                return Results.NoContent();
            }
            if (result.IsLockedOut)
            {
                await audit.WriteAsync(AuditActions.LockedOut, "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
                return Results.Problem(statusCode: 423, title: "Account is locked. Try again later.");
            }
            await audit.WriteAsync(AuditActions.LoginFailed, "User", user.Id.ToString(), user.Id, user.DisplayName, new { Reason = "bad_2fa_code" }, ct);
            return Results.Problem(statusCode: 401, title: "The code is not correct.");
        }).AllowAnonymous().RequireRateLimiting("login");

        var tf = auth.MapGroup("/2fa").RequireAuthorization();

        tf.MapGet("", async (ICurrentUser me, UserManager<AppUser> users) =>
        {
            var user = await MeAsync(me, users);
            return new TwoFactorStatusDto(user.TwoFactorEnabled, user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0);
        });

        // A new key each time, until a code from it is confirmed: a half-finished setup never leaves a working key behind.
        // Needs the password: the key opens the terminal, so a stolen session alone must not be able to enroll its own authenticator.
        tf.MapPost("/setup", async (TwoFactorSetupRequest req, ICurrentUser me, UserManager<AppUser> users, SignInManager<AppUser> signIn, SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await MeAsync(me, users);
            if (user.TwoFactorEnabled) throw new DomainException("auth.2fa_enabled", "Two-factor sign-in is already on. Turn it off first to use a new key.");
            var check = await signIn.CheckPasswordSignInAsync(user, req.Password ?? "", lockoutOnFailure: true);
            if (check.IsLockedOut)
            {
                await audit.WriteAsync(AuditActions.LockedOut, "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
                return Results.Problem(statusCode: 423, title: "Account is locked. Try again later.");
            }
            if (!check.Succeeded)
            {
                await audit.WriteAsync("TwoFactorSetupFailed", "User", user.Id.ToString(), user.Id, user.DisplayName, new { Reason = "bad_password" }, ct);
                throw new DomainException("auth.bad_password", "The password is not correct.");
            }
            await users.ResetAuthenticatorKeyAsync(user);
            var key = (await users.GetAuthenticatorKeyAsync(user))!;
            var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
            return Results.Ok(new TwoFactorSetupDto(Grouped(key), $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6"));
        }).RequireRateLimiting("login");

        tf.MapPost("/enable", async (TwoFactorCodeRequest req, ICurrentUser me, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await MeAsync(me, users);
            if (user.TwoFactorEnabled) throw new DomainException("auth.2fa_enabled", "Two-factor sign-in is already on.");
            if (await users.GetAuthenticatorKeyAsync(user) is null) throw new DomainException("auth.2fa_no_key", "Start the setup first.");
            if (!await VerifyCodeAsync(users, user, req.Code)) throw new DomainException("auth.2fa_bad_code", "The code is not correct. Check the phone's clock and try the newest code.");
            await users.SetTwoFactorEnabledAsync(user, true);
            var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
            await signIn.RefreshSignInAsync(user); // the security stamp changed: this session stays, others sign in again
            await audit.WriteAsync("TwoFactorEnabled", "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
            return new RecoveryCodesDto(codes);
        });

        tf.MapPost("/disable", async (TwoFactorDisableRequest req, ICurrentUser me, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            TerminalGate gate, SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await MeAsync(me, users);
            if (!user.TwoFactorEnabled) return Results.NoContent();
            if (!await users.CheckPasswordAsync(user, req.Password ?? ""))
                throw new DomainException("auth.bad_password", "The password is not correct.");
            var ok = await VerifyCodeAsync(users, user, req.Code)
                || (await users.RedeemTwoFactorRecoveryCodeAsync(user, NormalizeRecovery(req.Code))).Succeeded;
            if (!ok) throw new DomainException("auth.2fa_bad_code", "The code is not correct.");
            await users.SetTwoFactorEnabledAsync(user, false);
            await users.ResetAuthenticatorKeyAsync(user); // the old key in the phone stops working
            gate.Lock(user.Id);
            await signIn.RefreshSignInAsync(user);
            await audit.WriteAsync("TwoFactorDisabled", "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
            return Results.NoContent();
        });

        tf.MapPost("/recovery-codes", async (TwoFactorCodeRequest req, ICurrentUser me, UserManager<AppUser> users, SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await MeAsync(me, users);
            if (!user.TwoFactorEnabled) throw new DomainException("auth.2fa_off", "Turn on two-factor sign-in first.");
            if (!await VerifyCodeAsync(users, user, req.Code)) throw new DomainException("auth.2fa_bad_code", "The code is not correct.");
            var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
            await audit.WriteAsync("TwoFactorRecoveryCodesReset", "User", user.Id.ToString(), user.Id, user.DisplayName, null, ct);
            return new RecoveryCodesDto(codes);
        });
    }

    /// <summary>The CEO (never an AI identity) for the terminal endpoints.</summary>
    public static async Task<AppUser> TerminalUserAsync(ICurrentUser me, UserManager<AppUser> users)
    {
        var user = me.UserId is { } id ? await users.FindByIdAsync(id.ToString()) : null;
        if (user is null || user.IsAgent || !await users.IsInRoleAsync(user, SystemRoles.Ceo))
            throw new ForbiddenException("The terminal is for the CEO only.");
        return user;
    }

    public static void MapTerminal(RouteGroupBuilder api)
    {
        api.MapGet("/assistant/terminal/status", async (ICurrentUser me, UserManager<AppUser> users, TerminalGate gate) =>
        {
            var user = await TerminalUserAsync(me, users);
            return new TerminalStatusDto(user.TwoFactorEnabled, user.TwoFactorEnabled ? gate.OpenUntil(user.Id, user.SecurityStamp!) : null);
        }).RequirePermission(Permissions.AgentManage);

        // Step-up: a fresh authenticator code opens the terminal; wrong codes count towards the account lockout.
        api.MapPost("/assistant/terminal/unlock", async (TwoFactorCodeRequest req, ICurrentUser me, UserManager<AppUser> users, TerminalGate gate,
            SecurityAuditWriter audit, CancellationToken ct) =>
        {
            var user = await TerminalUserAsync(me, users);
            if (!user.TwoFactorEnabled)
                throw new DomainException("terminal.2fa_required", "Turn on two-factor sign-in (My settings) before using the terminal.");
            if (await users.IsLockedOutAsync(user)) return Results.Problem(statusCode: 423, title: "Account is locked. Try again later.");
            if (!await VerifyCodeAsync(users, user, req.Code))
            {
                await users.AccessFailedAsync(user);
                await audit.WriteAsync("TerminalUnlockFailed", "Terminal", null, user.Id, user.DisplayName, null, ct);
                throw new DomainException("terminal.bad_code", "The code is not correct.");
            }
            await users.ResetAccessFailedCountAsync(user);
            var until = gate.Unlock(user.Id, user.SecurityStamp!);
            await audit.WriteAsync("TerminalUnlocked", "Terminal", null, user.Id, user.DisplayName, null, ct);
            return Results.Ok(new TerminalStatusDto(true, until));
        }).RequirePermission(Permissions.AgentManage).RequireRateLimiting("login");

        api.MapPost("/assistant/terminal/lock", async (ICurrentUser me, UserManager<AppUser> users, TerminalGate gate) =>
        {
            gate.Lock((await TerminalUserAsync(me, users)).Id);
            return Results.NoContent();
        }).RequirePermission(Permissions.AgentManage);

        // Any PowerShell command in the project folder on the host: the CEO only, with two-factor sign-in on and the terminal unlocked.
        api.MapPost("/assistant/terminal", async (TerminalRequest r, TerminalService s, ICurrentUser me, UserManager<AppUser> users, TerminalGate gate, CancellationToken ct) =>
        {
            var user = await TerminalUserAsync(me, users);
            if (!user.TwoFactorEnabled)
                throw new DomainException("terminal.2fa_required", "Turn on two-factor sign-in (My settings) before using the terminal.");
            if (!gate.Use(user.Id, user.SecurityStamp!))
                throw new DomainException("terminal.locked", "Enter the code from your authenticator app to open the terminal.");
            return await s.RunAsync(r, ct);
        }).RequirePermission(Permissions.AgentManage);
    }
}
