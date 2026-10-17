namespace OfficeAI.Api.Infrastructure;

/// <summary>
/// Baseline HTTP hardening for a JSON API:
/// - response headers that stop sniffing, framing and referrer leaks;
/// - a CSRF guard: every state-changing /api call must carry X-Requested-With. Browsers cannot add that header
///   to a cross-site form post, and cross-origin scripts need a CORS preflight that only allowed origins pass.
/// </summary>
public sealed class SecurityMiddleware(RequestDelegate next)
{
    public const string CsrfHeader = "X-Requested-With";
    private static readonly HashSet<string> Safe = ["GET", "HEAD", "OPTIONS"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        if (ctx.Request.Path.StartsWithSegments("/api") && !Safe.Contains(ctx.Request.Method) && !ctx.Request.Headers.ContainsKey(CsrfHeader))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { title = $"Missing {CsrfHeader} header.", code = "csrf" });
            return;
        }
        await next(ctx);
    }
}
