using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Organization;

namespace OfficeAI.Application.Communication;

public record ChannelDto(Guid Id, ChannelKind Kind, string Name, string? Description, Guid? DepartmentId, Guid? ProjectId, bool IsArchived,
    int Unread, DateTimeOffset? LastMessageAt);
public record CreateChannelRequest(string Name, string? Description);
public record OpenChannelRequest(Guid? ProjectId, Guid? DepartmentId);
public record MessageDto(Guid Id, Guid ChannelId, string AuthorName, Guid? AuthorEmployeeId, bool IsAgent, bool IsSimulated, string? Body,
    bool IsDeleted, DateTimeOffset CreatedAt, bool CanDelete, List<Guid> Mentions);
public record PostMessageRequest(string Body, List<Guid>? MentionedEmployeeIds = null);
public record ChannelMemberDto(Guid EmployeeId, string DisplayName, EmployeeKind Kind, string JobTitle, bool IsExplicit);

/// <summary>What the caller may do in chat, resolved from their permissions by the API.</summary>
/// <param name="CanModerate">comms.manage: delete any message, archive channels.</param>
/// <param name="SeesAllProjects">comms.manage or project.manage: read every project channel without being a member.</param>
public record ChatAccess(bool CanModerate, bool SeesAllProjects);

/// <summary>
/// Company chat. Company and department channels are open to every signed-in person; a project channel only to the project's
/// members (see <see cref="ProjectMembership"/>) and to people who manage projects or communication. Posting needs comms.post.
/// </summary>
public class ChatService(IAppDbContext db, ICurrentUser current, IRealtimeNotifier notifier, TimeProvider clock)
{
    public const string GeneralChannel = "General";
    public const int MaxBody = 4000;
    public const int MaxMentions = 20;

    /// <summary>Returns the channel of a project or department, creating it the first time. Safe under concurrent calls.</summary>
    public static async Task<ChatChannel> GetOrCreateChannelAsync(IAppDbContext db, Guid? projectId, Guid? departmentId, CancellationToken ct)
    {
        if ((projectId is null) == (departmentId is null)) throw new DomainException("validation", "Give exactly one of projectId or departmentId.");
        var existing = await db.ChatChannels.FirstOrDefaultAsync(c => projectId != null ? c.ProjectId == projectId : c.DepartmentId == departmentId, ct);
        if (existing is not null) return existing;

        ChatChannel channel;
        if (projectId is { } p)
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p, ct) ?? throw new NotFoundException("Project", p);
            channel = new ChatChannel { Kind = ChannelKind.Project, Name = $"{project.Code} · {project.Name}", ProjectId = p };
        }
        else
        {
            var dept = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == departmentId, ct) ?? throw new NotFoundException("Department", departmentId!.Value);
            channel = new ChatChannel { Kind = ChannelKind.Department, Name = dept.Name, DepartmentId = dept.Id };
        }
        db.ChatChannels.Add(channel);
        try
        {
            await db.SaveChangesAsync(ct);
            return channel;
        }
        catch (DbUpdateException)
        {
            // Someone else created it at the same moment: use theirs.
            db.ResetTracking();
            return await db.ChatChannels.FirstAsync(c => projectId != null ? c.ProjectId == projectId : c.DepartmentId == departmentId, ct);
        }
    }

    private Task<Guid?> MyEmployeeIdAsync(CancellationToken ct) =>
        db.Employees.Where(e => e.UserId == current.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);

    /// <summary>Projects the employee belongs to (owner, assignee or added by hand).</summary>
    private IQueryable<Guid> ProjectsOf(Guid employeeId) =>
        db.ProjectMembers.Where(m => m.EmployeeId == employeeId).Select(m => m.ProjectId)
            .Union(db.Projects.Where(p => p.OwnerEmployeeId == employeeId).Select(p => p.Id))
            .Union(db.TaskAssignments.Where(a => a.EmployeeId == employeeId).Join(db.Tasks, a => a.TaskId, t => t.Id, (a, t) => t.ProjectId));

    /// <summary>Loads a channel the caller may read. Channels of projects they are not part of look like they do not exist.</summary>
    private async Task<ChatChannel> VisibleChannelAsync(Guid channelId, ChatAccess access, CancellationToken ct)
    {
        var c = await db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == channelId, ct) ?? throw new NotFoundException("Channel", channelId);
        if (c.ProjectId is { } p && !access.SeesAllProjects && !await ProjectMembership.IsMemberAsync(db, p, await MyEmployeeIdAsync(ct), ct))
            throw new NotFoundException("Channel", channelId);
        return c;
    }

    public async Task<List<ChannelDto>> ListChannelsAsync(ChatAccess access, bool includeArchived, CancellationToken ct)
    {
        var me = current.UserId;
        var q = db.ChatChannels.AsNoTracking().Where(c => includeArchived || !c.IsArchived);
        if (!access.SeesAllProjects)
        {
            var mine = await MyEmployeeIdAsync(ct) is { } e ? await ProjectsOf(e).ToListAsync(ct) : [];
            q = q.Where(c => c.ProjectId == null || mine.Contains(c.ProjectId.Value));
        }
        var channels = await q.OrderBy(c => c.Kind).ThenBy(c => c.Name).ToListAsync(ct);
        var ids = channels.Select(c => c.Id).ToList();
        var reads = await db.ChannelReadStates.AsNoTracking().Where(r => r.UserId == me).ToDictionaryAsync(r => r.ChannelId, r => r.LastReadAt, ct);
        var stats = await db.ChatMessages.AsNoTracking().Where(m => m.DeletedAt == null && ids.Contains(m.ChannelId))
            .GroupBy(m => m.ChannelId).Select(g => new { ChannelId = g.Key, Last = g.Max(m => m.CreatedAt) }).ToDictionaryAsync(x => x.ChannelId, x => x.Last, ct);

        // Unread = other people's messages after my read mark (capped so the query stays cheap).
        var unread = new Dictionary<Guid, int>();
        foreach (var c in channels.Where(c => stats.TryGetValue(c.Id, out var last) && last > reads.GetValueOrDefault(c.Id)))
        {
            var since = reads.GetValueOrDefault(c.Id);
            unread[c.Id] = await db.ChatMessages.Where(m => m.ChannelId == c.Id && m.DeletedAt == null && m.CreatedAt > since && m.AuthorUserId != me)
                .Take(100).CountAsync(ct);
        }
        return channels.Select(c => new ChannelDto(c.Id, c.Kind, c.Name, c.Description, c.DepartmentId, c.ProjectId, c.IsArchived,
            unread.GetValueOrDefault(c.Id), stats.TryGetValue(c.Id, out var l) ? l : null)).ToList();
    }

    public async Task<ChannelDto> CreateChannelAsync(CreateChannelRequest req, CancellationToken ct)
    {
        Validate.Required(req.Name, "name");
        var name = req.Name.Trim();
        if (name.Length > 100) throw new DomainException("validation", "name must be at most 100 characters.");
        if (await db.ChatChannels.AnyAsync(c => c.Kind == ChannelKind.Company && c.Name == name, ct))
            throw new DomainException("validation", "A company channel with this name already exists.");
        var c = new ChatChannel { Kind = ChannelKind.Company, Name = name, Description = req.Description?.Trim() };
        db.ChatChannels.Add(c);
        await db.SaveChangesAsync(ct);
        return new ChannelDto(c.Id, c.Kind, c.Name, c.Description, null, null, false, 0, null);
    }

    public async Task<ChannelDto> OpenChannelAsync(OpenChannelRequest req, ChatAccess access, CancellationToken ct)
    {
        if (req.ProjectId is { } p && !access.SeesAllProjects && !await ProjectMembership.IsMemberAsync(db, p, await MyEmployeeIdAsync(ct), ct))
            throw new ForbiddenException("Only members of this project can open its chat.");
        var c = await GetOrCreateChannelAsync(db, req.ProjectId, req.DepartmentId, ct);
        return (await ListChannelsAsync(access, true, ct)).First(x => x.Id == c.Id);
    }

    /// <summary>People who can be @mentioned: the project's members in a project channel, every active employee otherwise.</summary>
    public async Task<List<ChannelMemberDto>> MembersAsync(Guid channelId, ChatAccess access, CancellationToken ct)
    {
        var c = await VisibleChannelAsync(channelId, access, ct);
        var employees = db.Employees.AsNoTracking().Where(e => e.Status != EmployeeStatus.Inactive);
        List<Guid> explicitIds = [];
        if (c.ProjectId is { } p)
        {
            var ids = await ProjectMembership.MemberIds(db, p).ToListAsync(ct);
            explicitIds = await db.ProjectMembers.Where(m => m.ProjectId == p).Select(m => m.EmployeeId).ToListAsync(ct);
            employees = employees.Where(e => ids.Contains(e.Id));
        }
        var rows = await employees.OrderBy(e => e.DisplayName)
            .Select(e => new { e.Id, e.DisplayName, e.Kind, Title = e.RoleDefinition!.Title }).ToListAsync(ct);
        return rows.Select(e => new ChannelMemberDto(e.Id, e.DisplayName, e.Kind, e.Title, explicitIds.Contains(e.Id))).ToList();
    }

    public async Task<ChannelDto> SetArchivedAsync(Guid id, bool archived, CancellationToken ct)
    {
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Channel", id);
        if (c.Kind == ChannelKind.Company && c.Name == GeneralChannel && archived) throw new DomainException("validation", "The General channel cannot be archived.");
        c.IsArchived = archived;
        await db.SaveChangesAsync(ct);
        return (await ListChannelsAsync(new ChatAccess(true, true), true, ct)).First(x => x.Id == c.Id);
    }

    /// <summary>The newest <paramref name="limit"/> messages before <paramref name="before"/>, oldest first.</summary>
    public async Task<List<MessageDto>> ListMessagesAsync(Guid channelId, DateTimeOffset? before, int limit, ChatAccess access, CancellationToken ct)
    {
        await VisibleChannelAsync(channelId, access, ct);
        limit = Math.Clamp(limit, 1, 200);
        var rows = await db.ChatMessages.AsNoTracking().Where(m => m.ChannelId == channelId && (before == null || m.CreatedAt < before))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(limit).ToListAsync(ct);
        rows.Reverse();
        return rows.Select(m => ToDto(m, access.CanModerate)).ToList();
    }

    private MessageDto ToDto(ChatMessage m, bool canModerate) => new(m.Id, m.ChannelId, m.AuthorName, m.AuthorEmployeeId, m.IsAgent, m.IsSimulated,
        m.DeletedAt is null ? m.Body : null, m.DeletedAt is not null, m.CreatedAt,
        m.DeletedAt is null && (canModerate || (m.AuthorUserId is { } a && a == current.UserId)),
        m.DeletedAt is null ? m.MentionedEmployeeIds : []);

    public async Task<MessageDto> PostAsync(Guid channelId, PostMessageRequest req, ChatAccess access, CancellationToken ct)
    {
        var body = (req.Body ?? "").Trim();
        if (body.Length == 0) throw new DomainException("validation", "body is required.");
        if (body.Length > MaxBody) throw new DomainException("validation", $"body must be at most {MaxBody} characters.");
        var channel = await VisibleChannelAsync(channelId, access, ct);
        if (channel.IsArchived) throw new DomainException("validation", "This channel is archived.");

        // Only people who can read the channel can be mentioned (in a project channel: its members).
        var mentions = (req.MentionedEmployeeIds ?? []).Distinct().ToList();
        if (mentions.Count > MaxMentions) throw new DomainException("validation", $"A message can mention at most {MaxMentions} people.");
        if (mentions.Count > 0)
        {
            var allowed = channel.ProjectId is { } p
                ? await ProjectMembership.MemberIds(db, p).Where(id => mentions.Contains(id)).ToListAsync(ct)
                : await db.Employees.Where(e => mentions.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
            if (mentions.Except(allowed).Any()) throw new DomainException("validation", "You can only mention people who can read this channel.");
        }

        var employee = await db.Employees.AsNoTracking().Where(e => e.UserId == current.UserId).Select(e => new { e.Id, e.DisplayName }).FirstOrDefaultAsync(ct);
        var m = new ChatMessage
        {
            ChannelId = channelId, AuthorUserId = current.UserId, AuthorEmployeeId = employee?.Id,
            AuthorName = employee?.DisplayName ?? current.Name ?? "Unknown", IsAgent = current.IsAgent, Body = body, MentionedEmployeeIds = mentions,
        };
        db.ChatMessages.Add(m);
        var notified = await NotificationService.AddForEmployeesAsync(db, mentions, current.UserId, NotificationKinds.Mention,
            $"{m.AuthorName} · #{channel.Name}", body, $"/chat?channel={channelId}", clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        await notifier.ChatMessageAsync(channelId, m.Id, ct);
        if (notified.Count > 0) await notifier.NotificationsChangedAsync(notified, ct);
        await TryMarkReadAsync(channelId, m.CreatedAt, ct);
        return ToDto(m, access.CanModerate);
    }

    public async Task DeleteAsync(Guid messageId, bool canModerate, CancellationToken ct)
    {
        var m = await db.ChatMessages.FirstOrDefaultAsync(x => x.Id == messageId, ct) ?? throw new NotFoundException("Message", messageId);
        if (m.DeletedAt is not null) return;
        if (!canModerate && m.AuthorUserId != current.UserId) throw new ForbiddenException("Only the author or a moderator can delete this message.");
        m.DeletedAt = clock.GetUtcNow();
        m.DeletedBy = current.UserId;
        await db.SaveChangesAsync(ct);
        await notifier.ChatMessageAsync(m.ChannelId, m.Id, ct);
    }

    public async Task MarkReadAsync(Guid channelId, ChatAccess access, CancellationToken ct)
    {
        await VisibleChannelAsync(channelId, access, ct);
        await TryMarkReadAsync(channelId, clock.GetUtcNow(), ct);
    }

    /// <summary>Moves the read mark forward. Two tabs racing to create it is harmless: the loser just keeps the winner's mark.</summary>
    private async Task TryMarkReadAsync(Guid channelId, DateTimeOffset at, CancellationToken ct)
    {
        if (current.UserId is not { } me) return;
        var state = await db.ChannelReadStates.FirstOrDefaultAsync(r => r.ChannelId == channelId && r.UserId == me, ct);
        if (state is null) db.ChannelReadStates.Add(new ChannelReadState { ChannelId = channelId, UserId = me, LastReadAt = at });
        else if (state.LastReadAt < at) state.LastReadAt = at;
        else return;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is DbUpdateException or ConcurrencyException)
        {
            db.ResetTracking();
        }
    }

    // ---------------- project members ----------------

    public async Task<List<ChannelMemberDto>> ProjectMembersAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        var channel = await GetOrCreateChannelAsync(db, projectId, null, ct);
        return await MembersAsync(channel.Id, new ChatAccess(false, true), ct);
    }

    /// <summary>Adds someone to a project by hand (e.g. a person who has no task there yet). Idempotent.</summary>
    public async Task<List<ChannelMemberDto>> AddProjectMemberAsync(Guid projectId, Guid employeeId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw new NotFoundException("Employee", employeeId);
        if (!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.EmployeeId == employeeId, ct))
        {
            db.ProjectMembers.Add(new ProjectMember { ProjectId = projectId, EmployeeId = employeeId });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ResetTracking(); // added concurrently: same result
            }
        }
        return await ProjectMembersAsync(projectId, ct);
    }

    /// <summary>Removes a hand-added member. Owners and task assignees stay members through their role in the project.</summary>
    public async Task<List<ChannelMemberDto>> RemoveProjectMemberAsync(Guid projectId, Guid employeeId, CancellationToken ct)
    {
        var row = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.EmployeeId == employeeId, ct);
        if (row is not null)
        {
            db.ProjectMembers.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return await ProjectMembersAsync(projectId, ct);
    }
}
