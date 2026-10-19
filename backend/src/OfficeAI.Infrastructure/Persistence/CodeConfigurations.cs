using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Code;
using OfficeAI.Domain.Work;

namespace OfficeAI.Infrastructure.Persistence;

internal class CodeRepositoryConfig : IEntityTypeConfiguration<CodeRepository>
{
    public void Configure(EntityTypeBuilder<CodeRepository> b)
    {
        EntityConfig.Base(b);
        b.ToTable("CodeRepositories");
        b.Property(r => r.RemoteUrl).HasMaxLength(500);
        b.Property(r => r.RemoteTokenEnvVar).HasMaxLength(80);
        b.Property(r => r.RemotePushedSha).HasMaxLength(64);
        b.Property(r => r.RemoteError).HasMaxLength(2000);
        b.Property(r => r.Name).HasMaxLength(63);
        b.Property(r => r.DefaultBranch).HasMaxLength(63);
        b.Property(r => r.SandboxImage).HasMaxLength(200);
        b.Property(r => r.TestCommand).HasMaxLength(1000);
        b.Property(r => r.LintCommand).HasMaxLength(1000);
        b.HasIndex(r => new { r.ProjectId, r.Name }).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class TestRunConfig : IEntityTypeConfiguration<TestRun>
{
    public void Configure(EntityTypeBuilder<TestRun> b)
    {
        b.ToTable("TestRuns");
        b.HasKey(t => t.Id);
        b.Property(t => t.Branch).HasMaxLength(100);
        b.Property(t => t.CommitSha).HasMaxLength(64);
        b.Property(t => t.Image).HasMaxLength(200);
        b.Property(t => t.Command).HasMaxLength(1000);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(t => new { t.RepositoryId, t.Branch, t.StartedAt });
        b.HasIndex(t => t.TaskId);
        b.HasOne<CodeRepository>().WithMany().HasForeignKey(t => t.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class MergeRequestConfig : IEntityTypeConfiguration<MergeRequest>
{
    public void Configure(EntityTypeBuilder<MergeRequest> b)
    {
        EntityConfig.Base(b);
        b.ToTable("MergeRequests");
        b.Property(m => m.Title).HasMaxLength(200);
        b.Property(m => m.SourceBranch).HasMaxLength(100);
        b.Property(m => m.TargetBranch).HasMaxLength(100);
        b.Property(m => m.HeadSha).HasMaxLength(64);
        b.Property(m => m.MergedSha).HasMaxLength(64);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(m => new { m.RepositoryId, m.Number }).IsUnique();
        b.HasIndex(m => m.ProjectId);
        b.HasOne<CodeRepository>().WithMany().HasForeignKey(m => m.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
