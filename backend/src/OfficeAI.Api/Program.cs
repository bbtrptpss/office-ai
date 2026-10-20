using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Api.Endpoints;
using OfficeAI.Api.Infrastructure;
using OfficeAI.Application;
using OfficeAI.Application.Abstractions;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OfficeAI.Infrastructure;
using OfficeAI.Infrastructure.Identity;
using OfficeAI.Infrastructure.Persistence;

// Server-written text (documents, emails, calendar files, AI prompts) uses fixed formats; the web app localizes for display.
// Without this a Thai-culture host writes Buddhist-era years (2569) into dates.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Observability ----------
if (config.GetValue("Logging:Json", false))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true); // scopes carry the correlation id
}
var otlp = config["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(config["OTEL_SERVICE_NAME"] ?? "officeai-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation().AddNpgsql().AddSource(Telemetry.Name);
        if (!string.IsNullOrEmpty(otlp)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(Telemetry.Name);
        if (!string.IsNullOrEmpty(otlp)) m.AddOtlpExporter();
    });
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<AgentRunQueue>();
builder.Services.AddSingleton<IAgentRunQueue>(sp => sp.GetRequiredService<AgentRunQueue>());
builder.Services.AddHostedService<AgentRunWorker>();
builder.Services.AddSingleton<ChatReplyQueue>();
builder.Services.AddSingleton<IChatReplyQueue>(sp => sp.GetRequiredService<ChatReplyQueue>());
builder.Services.AddHostedService<ChatReplyWorker>();
builder.Services.AddHostedService<WorkflowEngineWorker>();
builder.Services.AddHostedService<IntegrationWorker>();
builder.Services.AddHostedService<EmailWorker>();
builder.Services.AddHostedService<StaleFailureWorker>();
builder.Services.AddHostedService<RetentionWorker>();

// ---------- Identity + cookie auth ----------
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppClaimsFactory>();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "officeai.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    // Behind HTTPS in production the cookie must never travel over plain http.
    o.Cookie.SecurePolicy = config.GetValue("Security:SecureCookies", false) ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    // API: answer with status codes, never redirect to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:LoginPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("calendar", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:CalendarPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .WithHeaders("Content-Type", SecurityMiddleware.CsrfHeader, CorrelationIdMiddleware.Header, "X-SignalR-User-Agent").AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders(CorrelationIdMiddleware.Header, "Content-Disposition")));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
if (!app.Environment.IsDevelopment() && config.GetValue("Security:Hsts", false)) app.UseHsts();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseMiddleware<SecurityMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ProjectScopeMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapAppEndpoints();
app.MapHealthChecks("/health/ready"); // liveness stays at /health

if (config.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Seeder>().SeedAsync();
}

app.Run();

public partial class Program;
