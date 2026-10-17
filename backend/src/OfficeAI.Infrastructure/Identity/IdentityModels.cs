using Microsoft.AspNetCore.Identity;

namespace OfficeAI.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    /// <summary>Service account of an AI employee. Such identities can never use human-only permissions.</summary>
    public bool IsAgent { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AppRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}

public class Permission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Code { get; set; }
    public string? Description { get; set; }
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}
