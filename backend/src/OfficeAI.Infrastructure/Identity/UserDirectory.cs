using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Infrastructure.Identity;

/// <summary>Email and name of active people. AI service accounts are never returned.</summary>
public sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<Dictionary<Guid, (string Email, string Name)>> ContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive && !u.IsAgent && u.Email != null)
            .Select(u => new { u.Id, u.Email, u.DisplayName }).ToListAsync(ct))
        .ToDictionary(u => u.Id, u => (u.Email!, u.DisplayName));
}
