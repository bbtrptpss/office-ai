using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;

namespace OfficeAI.Infrastructure.Persistence;

internal class KnowledgeEntryConfig : IEntityTypeConfiguration<KnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<KnowledgeEntry> b)
    {
        EntityConfig.Base(b);
        b.ToTable("KnowledgeEntries");
        b.Property(k => k.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(k => k.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(k => k.Title).HasMaxLength(200);
        b.Property(k => k.Source).HasMaxLength(500);
        b.HasIndex(k => new { k.Status, k.ProjectId });
        b.HasOne<Project>().WithMany().HasForeignKey(k => k.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ProjectDocumentConfig : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> b)
    {
        EntityConfig.Base(b);
        b.ToTable("ProjectDocuments");
        b.Property(d => d.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(d => d.Title).HasMaxLength(200);
        b.HasIndex(d => new { d.ProjectId, d.Kind, d.Title, d.Revision }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
