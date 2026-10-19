using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Communication;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Api.Infrastructure;

/// <summary>
/// Narrows <see cref="ProjectScope"/> for signed-in callers without project.all to the projects they belong to.
/// Runs after authentication, once per request; the database query filters do the rest.
/// </summary>
public sealed class ProjectScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, ICurrentUser current, PermissionService permissions, ProjectScope scope, AppDbContext db)
    {
        if (current.UserId is { } userId)
        {
            var granted = await permissions.GetPermissionsAsync(userId, ctx.RequestAborted);
            if (!granted.Contains(Permissions.ProjectAll))
            {
                var employeeId = await db.Employees.Where(e => e.UserId == userId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ctx.RequestAborted);
                scope.Restrict(employeeId is { } e ? await ProjectMembership.ProjectsOf(db, e).ToListAsync(ctx.RequestAborted) : []);
            }
        }
        await next(ctx);
    }
}
