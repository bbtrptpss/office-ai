using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Audit;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Security;
using OfficeAI.Infrastructure.Identity;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Infrastructure.Security;

public record UserDto(Guid Id, string Email, string DisplayName, bool IsAgent, bool IsActive, List<string> Roles);
public record CreateUserRequest(string Email, string DisplayName, string? Password, bool IsAgent, List<string> Roles);
public record RoleDto(Guid Id, string Name, List<string> Permissions);
public record SetRolePermissionsRequest(List<string> Permissions);
public record SetUserRolesRequest(List<string> Roles);
public record AuditLogDto(long Id, DateTimeOffset OccurredAt, Guid? ActorUserId, string? ActorName, string Action,
    string EntityType, string? EntityId, string? OldValues, string? NewValues, string? CorrelationId);

/// <summary>Resolves what a user may do. Always evaluated server-side from the database.</summary>
public class PermissionService(AppDbContext db)
{
    public async Task<HashSet<string>> GetPermissionsAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.IsAgent, u.IsActive }).FirstOrDefaultAsync(ct);
        if (user is null || !user.IsActive) return [];

        var codes = await (
            from ur in db.UserRoles
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where ur.UserId == userId
            select p.Code).Distinct().ToListAsync(ct);

        var set = codes.ToHashSet();
        if (user.IsAgent) set.ExceptWith(Permissions.HumanOnly); // hard rule, independent of role configuration
        return set;
    }
}

public class SecurityAdminService(AppDbContext db, UserManager<AppUser> users, ICurrentUser current, TimeProvider clock)
{
    public async Task<List<UserDto>> ListUsersAsync(CancellationToken ct)
    {
        var list = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct);
        var roleMap = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name })
            .ToListAsync(ct);
        return list.Select(u => new UserDto(u.Id, u.Email!, u.DisplayName, u.IsAgent, u.IsActive,
            roleMap.Where(x => x.UserId == u.Id).Select(x => x.Name!).OrderBy(x => x).ToList())).ToList();
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email)) throw new DomainException("validation", "email is required.");
        if (string.IsNullOrWhiteSpace(req.DisplayName)) throw new DomainException("validation", "displayName is required.");
        await EnsureRolesExistAsync(req.Roles, ct);

        var user = new AppUser { UserName = req.Email, Email = req.Email, EmailConfirmed = true, DisplayName = req.DisplayName, IsAgent = req.IsAgent };
        // Agents never log in with a password; their credentials are issued by the runtime (Phase 2).
        var result = req.IsAgent || string.IsNullOrEmpty(req.Password)
            ? await users.CreateAsync(user)
            : await users.CreateAsync(user, req.Password);
        ThrowIfFailed(result);
        if (req.Roles.Count > 0) ThrowIfFailed(await users.AddToRolesAsync(user, req.Roles));

        await WriteAuditAsync(AuditActions.Created, "User", user.Id.ToString(), null,
            new { user.Email, user.DisplayName, user.IsAgent, req.Roles }, ct);
        return (await ListUsersAsync(ct)).First(u => u.Id == user.Id);
    }

    public async Task<UserDto> SetUserRolesAsync(Guid userId, SetUserRolesRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        if (userId == current.UserId) throw new ForbiddenException("You cannot change your own roles.");
        await EnsureRolesExistAsync(req.Roles, ct);

        var before = (await users.GetRolesAsync(user)).ToList();
        ThrowIfFailed(await users.RemoveFromRolesAsync(user, before.Except(req.Roles)));
        ThrowIfFailed(await users.AddToRolesAsync(user, req.Roles.Except(before)));
        await WriteAuditAsync(AuditActions.Updated, "UserRoles", userId.ToString(), new { Roles = before }, new { req.Roles }, ct);
        return (await ListUsersAsync(ct)).First(u => u.Id == userId);
    }

    public async Task<List<RoleDto>> ListRolesAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var perms = await (from rp in db.RolePermissions join p in db.Permissions on rp.PermissionId equals p.Id select new { rp.RoleId, p.Code })
            .ToListAsync(ct);
        return roles.Select(r => new RoleDto(r.Id, r.Name!, perms.Where(p => p.RoleId == r.Id).Select(p => p.Code).OrderBy(c => c).ToList())).ToList();
    }

    public async Task<RoleDto> SetRolePermissionsAsync(Guid roleId, SetRolePermissionsRequest req, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct) ?? throw new NotFoundException("Role", roleId);
        var unknown = req.Permissions.Except(Permissions.All.Keys).ToList();
        if (unknown.Count > 0) throw new DomainException("validation", $"Unknown permissions: {string.Join(", ", unknown)}.");
        if (role.Name == SystemRoles.Ceo && !req.Permissions.Contains(Permissions.SecurityManage))
            throw new DomainException("security.lockout", "The CEO role must keep security.manage.");

        var ids = await db.Permissions.Where(p => req.Permissions.Contains(p.Code)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        var before = await db.Permissions.Where(p => existing.Select(e => e.PermissionId).Contains(p.Id)).Select(p => p.Code).ToListAsync(ct);

        db.RolePermissions.RemoveRange(existing.Where(e => !ids.ContainsKey(e.PermissionId)));
        foreach (var id in ids.Keys.Where(id => existing.All(e => e.PermissionId != id)))
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = id });

        await WriteAuditAsync(AuditActions.Updated, "RolePermissions", roleId.ToString(),
            new { Permissions = before.Order() }, new { Permissions = req.Permissions.Order() }, ct);
        return (await ListRolesAsync(ct)).First(r => r.Id == roleId);
    }

    public async Task<List<AuditLogDto>> ListAuditAsync(string? entityType, string? entityId, int take, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) q = q.Where(a => a.EntityId == entityId);
        return await q.OrderByDescending(a => a.Id).Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.ActorUserId, a.ActorName, a.Action, a.EntityType, a.EntityId,
                a.OldValues, a.NewValues, a.CorrelationId))
            .ToListAsync(ct);
    }

    private async Task EnsureRolesExistAsync(List<string> roleNames, CancellationToken ct)
    {
        var known = await db.Roles.Select(r => r.Name!).ToListAsync(ct);
        var unknown = roleNames.Except(known).ToList();
        if (unknown.Count > 0) throw new DomainException("validation", $"Unknown roles: {string.Join(", ", unknown)}.");
    }

    private async Task WriteAuditAsync(string action, string entityType, string entityId, object? oldValues, object? newValues, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = clock.GetUtcNow(), ActorUserId = current.UserId, ActorName = current.Name, Action = action,
            EntityType = entityType, EntityId = entityId, CorrelationId = current.CorrelationId,
            OldValues = oldValues is null ? null : System.Text.Json.JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : System.Text.Json.JsonSerializer.Serialize(newValues),
        });
        await db.SaveChangesAsync(ct);
    }

    private static void ThrowIfFailed(IdentityResult r)
    {
        if (!r.Succeeded) throw new DomainException("validation", string.Join(" ", r.Errors.Select(e => e.Description)));
    }
}

/// <summary>Writes security events (logins, denials) outside of an entity change.</summary>
public class SecurityAuditWriter(AppDbContext db, ICurrentUser current, TimeProvider clock)
{
    public async Task WriteAsync(string action, string entityType, string? entityId, Guid? actorUserId, string? actorName, object? details, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = clock.GetUtcNow(),
            ActorUserId = actorUserId ?? current.UserId,
            ActorName = actorName ?? current.Name,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValues = details is null ? null : System.Text.Json.JsonSerializer.Serialize(details),
            CorrelationId = current.CorrelationId,
        });
        await db.SaveChangesAsync(ct);
    }
}
