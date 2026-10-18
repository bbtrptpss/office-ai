using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Communication;

namespace OfficeAI.Application.Communication;

public record ChannelDto(Guid Id, ChannelKind Kind, string Name, string? Description, Guid? DepartmentId, Guid? ProjectId, bool IsArchived,
    int Unread, DateTimeOffset? LastMessageAt);
public record CreateChannelRequest(string Name, string? Description);
public record OpenChannelRequest(Guid? ProjectId, Guid? DepartmentId);
public record MessageDto(Guid Id, Guid ChannelId, string AuthorName, Guid? AuthorEmployeeId, bool IsAgent, bool IsSimulated, string? Body,
    bool IsDeleted, DateTimeOffset CreatedAt, bool CanDelete);
public record PostMessageRequest(string Body);

/// <summary>Company chat. Every signed-in person can read channels; posting needs comms.post; moderation needs comms.manage.</summary>
public class ChatService(IAppDbContext db, ICurrentUser current, IRealtimeNotifier notifier, TimeProvider clock)
{
    public const string GeneralChannel = "General";
    public const int MaxBody = 4000;

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

    public async Task<List<ChannelDto>> ListChannelsAsync(bool includeArchived, CancellationToken ct)
    {
        var me = current.UserId;
        var channels = await db.ChatChannels.AsNoTracking().Where(c => includeArchived || !c.IsArchived)
            .OrderBy(c => c.Kind).ThenBy(c => c.Name).ToListAsync(ct);
        var reads = await db.ChannelReadStates.AsNoTracking().Where(r => r.UserId == me).ToDictionaryAsync(r => r.ChannelId, r => r.LastReadAt, ct);
        var stats = await db.ChatMessages.AsNoTracking().Where(m => m.DeletedAt == null)
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

    public async Task<ChannelDto> OpenChannelAsync(OpenChannelRequest req, CancellationToken ct)
    {
        var c = await GetOrCreateChannelAsync(db, req.ProjectId, req.DepartmentId, ct);
        return (await ListChannelsAsync(true, ct)).First(x => x.Id == c.Id);
    }

    public async Task<ChannelDto> SetArchivedAsync(Guid id, bool archived, CancellationToken ct)
    {
        var c = await db.ChatChannels.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Channel", id);
        if (c.Kind == ChannelKind.Company && c.Name == GeneralChannel && archived) throw new DomainException("validation", "The General channel cannot be archived.");
        c.IsArchived = archived;
        await db.SaveChangesAsync(ct);
        return (await ListChannelsAsync(true, ct)).First(x => x.Id == c.Id);
    }

    /// <summary>The newest <paramref name="limit"/> messages before <paramref name="before"/>, oldest first.</summary>
    public async Task<List<MessageDto>> ListMessagesAsync(Guid channelId, DateTimeOffset? before, int limit, bool canModerate, CancellationToken ct)
    {
        if (!await db.ChatChannels.AnyAsync(c => c.Id == channelId, ct)) throw new NotFoundException("Channel", channelId);
        limit = Math.Clamp(limit, 1, 200);
        var rows = await db.ChatMessages.AsNoTracking().Where(m => m.ChannelId == channelId && (before == null || m.CreatedAt < before))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(limit).ToListAsync(ct);
        rows.Reverse();
        return rows.Select(m => ToDto(m, canModerate)).ToList();
    }

    private MessageDto ToDto(ChatMessage m, bool canModerate) => new(m.Id, m.ChannelId, m.AuthorName, m.AuthorEmployeeId, m.IsAgent, m.IsSimulated,
        m.DeletedAt is null ? m.Body : null, m.DeletedAt is not null, m.CreatedAt,
        m.DeletedAt is null && (canModerate || (m.AuthorUserId is { } a && a == current.UserId)));

    public async Task<MessageDto> PostAsync(Guid channelId, PostMessageRequest req, CancellationToken ct)
    {
        var body = (req.Body ?? "").Trim();
        if (body.Length == 0) throw new DomainException("validation", "body is required.");
        if (body.Length > MaxBody) throw new DomainException("validation", $"body must be at most {MaxBody} characters.");
        var channel = await db.ChatChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Id == channelId, ct) ?? throw new NotFoundException("Channel", channelId);
        if (channel.IsArchived) throw new DomainException("validation", "This channel is archived.");
        var employee = await db.Employees.AsNoTracking().Where(e => e.UserId == current.UserId).Select(e => new { e.Id, e.DisplayName }).FirstOrDefaultAsync(ct);
        var m = new ChatMessage
        {
            ChannelId = channelId, AuthorUserId = current.UserId, AuthorEmployeeId = employee?.Id,
            AuthorName = employee?.DisplayName ?? current.Name ?? "Unknown", IsAgent = current.IsAgent, Body = body,
        };
        db.ChatMessages.Add(m);
        await db.SaveChangesAsync(ct);
        await notifier.ChatMessageAsync(channelId, m.Id, ct);
        await TryMarkReadAsync(channelId, m.CreatedAt, ct);
        return ToDto(m, false);
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

    public async Task MarkReadAsync(Guid channelId, CancellationToken ct)
    {
        if (!await db.ChatChannels.AnyAsync(c => c.Id == channelId, ct)) throw new NotFoundException("Channel", channelId);
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
}
