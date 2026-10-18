using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Communication;
using OfficeAI.Domain.Organization;
using OfficeAI.Domain.Work;

namespace OfficeAI.Infrastructure.Persistence;

internal class ChatChannelConfig : IEntityTypeConfiguration<ChatChannel>
{
    public void Configure(EntityTypeBuilder<ChatChannel> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ChatChannels");
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Name).HasMaxLength(100);
        b.Property(c => c.Description).HasMaxLength(500);
        // One channel per department / project; a concurrent "open channel" race ends in a unique violation, not a duplicate.
        b.HasIndex(c => c.DepartmentId).IsUnique().HasFilter("\"DepartmentId\" IS NOT NULL");
        b.HasIndex(c => c.ProjectId).IsUnique().HasFilter("\"ProjectId\" IS NOT NULL");
        b.HasOne<Department>().WithMany().HasForeignKey(c => c.DepartmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ChatMessageConfig : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ChatMessages");
        b.Property(m => m.AuthorName).HasMaxLength(200);
        b.Property(m => m.Body).HasMaxLength(4000);
        b.HasIndex(m => new { m.ChannelId, m.CreatedAt });
        b.HasOne<ChatChannel>().WithMany().HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ChannelReadStateConfig : IEntityTypeConfiguration<ChannelReadState>
{
    public void Configure(EntityTypeBuilder<ChannelReadState> b)
    {
        b.ToTable("ChannelReadStates");
        b.HasKey(r => new { r.ChannelId, r.UserId });
        b.HasOne<ChatChannel>().WithMany().HasForeignKey(r => r.ChannelId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class AnnouncementConfig : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Announcements");
        b.Property(a => a.Title).HasMaxLength(200);
        b.Property(a => a.Body).HasMaxLength(20_000);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Priority).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(a => new { a.Status, a.PublishedAt });
        b.HasOne<Department>().WithMany().HasForeignKey(a => a.DepartmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class AnnouncementReadConfig : IEntityTypeConfiguration<AnnouncementRead>
{
    public void Configure(EntityTypeBuilder<AnnouncementRead> b)
    {
        b.ToTable("AnnouncementReads");
        b.HasKey(r => new { r.AnnouncementId, r.UserId });
        b.HasOne<Announcement>().WithMany().HasForeignKey(r => r.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class MeetingConfig : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> b)
    {
        EntityConfig.Base(b);
        b.ToTable("Meetings");
        b.Property(m => m.Title).HasMaxLength(200);
        b.Property(m => m.Purpose).HasMaxLength(2000);
        b.Property(m => m.Location).HasMaxLength(300);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.MinutesStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Minutes).HasMaxLength(100_000);
        b.HasIndex(m => m.ScheduledAt);
        b.HasIndex(m => m.ProjectId);
        b.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Department>().WithMany().HasForeignKey(m => m.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(m => m.AgendaItems).WithOne().HasForeignKey(i => i.MeetingId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(m => m.Participants).WithOne().HasForeignKey(p => p.MeetingId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(m => m.ActionItems).WithOne().HasForeignKey(a => a.MeetingId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class MeetingAgendaItemConfig : IEntityTypeConfiguration<MeetingAgendaItem>
{
    public void Configure(EntityTypeBuilder<MeetingAgendaItem> b)
    {
        EntityConfig.Base(b);
        b.ToTable("MeetingAgendaItems");
        b.Property(i => i.Title).HasMaxLength(300);
        b.Property(i => i.Notes).HasMaxLength(4000);
    }
}

internal class MeetingParticipantConfig : IEntityTypeConfiguration<MeetingParticipant>
{
    public void Configure(EntityTypeBuilder<MeetingParticipant> b)
    {
        EntityConfig.Base(b);
        b.ToTable("MeetingParticipants");
        b.Property(p => p.Attendance).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(p => new { p.MeetingId, p.EmployeeId }).IsUnique();
        b.HasOne<EmployeeProfile>().WithMany().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class MeetingActionItemConfig : IEntityTypeConfiguration<MeetingActionItem>
{
    public void Configure(EntityTypeBuilder<MeetingActionItem> b)
    {
        EntityConfig.Base(b);
        b.ToTable("MeetingActionItems");
        b.Property(a => a.Title).HasMaxLength(300);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.SetNull);
    }
}
