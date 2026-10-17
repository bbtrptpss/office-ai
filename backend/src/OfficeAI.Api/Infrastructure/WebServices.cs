using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Identity;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Api.Infrastructure;

public static class AppClaims
{
    public const string DisplayName = "display_name";
    public const string IsAgent = "is_agent";
}

/// <summary>Adds display name and agent flag to the auth cookie.</summary>
public class AppClaimsFactory(UserManager<AppUser> users, RoleManager<AppRole> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(AppClaims.DisplayName, user.DisplayName));
        id.AddClaim(new Claim(AppClaims.IsAgent, user.IsAgent ? "true" : "false"));
        return id;
    }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Name => User?.FindFirstValue(AppClaims.DisplayName) ?? User?.Identity?.Name;
    public bool IsAgent => User?.FindFirstValue(AppClaims.IsAgent) == "true";
    public string? CorrelationId => accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] as string;
}

public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) id = Guid.CreateVersion7().ToString("N");
        ctx.Items[ItemKey] = id;
        ctx.Response.Headers[Header] = id;
        using (ctx.RequestServices.GetRequiredService<ILogger<CorrelationIdMiddleware>>().BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(ctx);
    }
}

// ---------- Permission-based authorization ("perm:<code>" policies) ----------

public record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
            .Build();
    }
}

public class PermissionHandler(PermissionService permissions, SecurityAuditWriter audit, ICurrentUser current, IHttpContextAccessor http)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (current.UserId is not { } userId) return;
        var ct = http.HttpContext?.RequestAborted ?? default;
        var granted = await permissions.GetPermissionsAsync(userId, ct);
        if (granted.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        var action = current.IsAgent && Permissions.HumanOnly.Contains(requirement.Permission)
            ? AuditActions.PermissionChangeDenied
            : AuditActions.AccessDenied;
        await audit.WriteAsync(action, "Endpoint", http.HttpContext?.Request.Path, null, null,
            new { requirement.Permission, Method = http.HttpContext?.Request.Method, current.IsAgent }, ct);
    }
}

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}

// ---------- Real-time ----------

[Authorize]
public class EventsHub : Hub;

public class SignalRNotifier(IHubContext<EventsHub> hub) : IRealtimeNotifier
{
    public Task TaskChangedAsync(Guid projectId, Guid taskId, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("taskChanged", new { projectId, taskId }, ct);

    public Task OrganizationChangedAsync(CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("organizationChanged", ct);
}
