using OfficeAI.Domain.Common;

namespace OfficeAI.Domain.Communication;

public enum ChannelKind
{
    Company = 1,
    Department = 2,
    Project = 3,
}

/// <summary>A company chat channel. Department and project channels are created on demand, one per department or project.</summary>
public class ChatChannel : Entity, IAuditable
{
    public ChannelKind Kind { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ProjectId { get; set; }
    public bool IsArchived { get; set; }
}

/// <summary>A chat message. Not audited row by row (too chatty); author and AI/simulated flags are kept on the message itself.</summary>
public class ChatMessage : Entity
{
    public Guid ChannelId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public Guid? AuthorEmployeeId { get; set; }
    public required string AuthorName { get; set; }
    public bool IsAgent { get; set; }
    public bool IsSimulated { get; set; }
    public required string Body { get; set; }

    /// <summary>Employees @mentioned in the message (picked in the composer, checked by the server).</summary>
    public List<Guid> MentionedEmployeeIds { get; set; } = [];

    /// <summary>Set when an AI employee posted it during a run.</summary>
    public Guid? RunId { get; set; }

    /// <summary>Removed by its author or a manager. The text is kept for accountability but never shown again.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

/// <summary>How far a user has read a channel, for unread counts.</summary>
public class ChannelReadState
{
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset LastReadAt { get; set; }
}

public enum AnnouncementStatus
{
    /// <summary>Written (possibly by an AI employee) and not yet visible to the company.</summary>
    Draft = 1,
    Published = 2,
    Archived = 3,
}

public enum AnnouncementPriority
{
    Normal = 1,
    Important = 2,
}

/// <summary>An internal announcement. Only a person publishes it; AI employees can only draft.</summary>
public class Announcement : Entity, IAuditable
{
    public required string Title { get; set; }
    public required string Body { get; set; }
    public AnnouncementPriority Priority { get; set; } = AnnouncementPriority.Normal;
    public AnnouncementStatus Status { get; set; } = AnnouncementStatus.Draft;

    /// <summary>Null = the whole company.</summary>
    public Guid? DepartmentId { get; set; }
    public bool IsPinned { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public Guid? AuthorEmployeeId { get; set; }
    public bool IsSimulated { get; set; }
}

/// <summary>A user acknowledged reading an announcement.</summary>
public class AnnouncementRead
{
    public Guid AnnouncementId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadAt { get; set; }
}

public enum MeetingStatus
{
    Scheduled = 1,
    Held = 2,
    Cancelled = 3,
}

public enum MinutesStatus
{
    None = 1,
    Draft = 2,
    Approved = 3,
}

/// <summary>A meeting with an agenda, participants, minutes and action items.</summary>
public class Meeting : Entity, IAuditable
{
    public required string Title { get; set; }
    public string? Purpose { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string? Location { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;
    public Guid? OrganizerEmployeeId { get; set; }

    public List<MeetingAgendaItem> AgendaItems { get; set; } = [];
    public List<MeetingParticipant> Participants { get; set; } = [];
    public List<MeetingActionItem> ActionItems { get; set; } = [];

    /// <summary>Markdown. Draft until a person approves it; approved minutes of a project meeting are also filed as a project document.</summary>
    public string? Minutes { get; set; }
    public MinutesStatus MinutesStatus { get; set; } = MinutesStatus.None;
    public Guid? MinutesAuthorEmployeeId { get; set; }
    public bool MinutesIsSimulated { get; set; }
    public Guid? MinutesApprovedByUserId { get; set; }
    public DateTimeOffset? MinutesApprovedAt { get; set; }
    public Guid? MinutesDocumentId { get; set; }
}

public class MeetingAgendaItem : Entity, IAuditable
{
    public Guid MeetingId { get; set; }
    public int SortOrder { get; set; }
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public Guid? OwnerEmployeeId { get; set; }
    public int? DurationMinutes { get; set; }
}

public enum Attendance
{
    Invited = 1,
    Attended = 2,
    Absent = 3,
}

public class MeetingParticipant : Entity, IAuditable
{
    public Guid MeetingId { get; set; }
    public Guid EmployeeId { get; set; }
    public Attendance Attendance { get; set; } = Attendance.Invited;
}

/// <summary>A follow-up agreed in a meeting. For project meetings a person can turn it into a task.</summary>
public class MeetingActionItem : Entity, IAuditable
{
    public Guid MeetingId { get; set; }
    public required string Title { get; set; }
    public Guid? OwnerEmployeeId { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsDone { get; set; }
    public Guid? TaskId { get; set; }
}

/// <summary>Someone added to a project by hand. Owners and task assignees are members without a row here.</summary>
public class ProjectMember : Entity, IAuditable
{
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }
}

public static class NotificationKinds
{
    public const string Mention = "mention";
    public const string MeetingInvite = "meeting_invite";
    public const string ActionItem = "action_item";
}

/// <summary>A personal notification shown under the bell. Only ever for people (AI employees are never notified).</summary>
public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }

    /// <summary>App path to open, e.g. "/chat?channel=…".</summary>
    public string? Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
