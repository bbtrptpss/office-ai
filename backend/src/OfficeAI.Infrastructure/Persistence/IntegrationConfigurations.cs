using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Integrations;

namespace OfficeAI.Infrastructure.Persistence;

internal class IntegrationConfigurationConfig : IEntityTypeConfiguration<IntegrationConfiguration>
{
    public void Configure(EntityTypeBuilder<IntegrationConfiguration> b)
    {
        EntityConfig.Base(b);
        b.ToTable("IntegrationConfigurations");
        b.Property(i => i.Name).HasMaxLength(100);
        b.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.SecretEnvVar).HasMaxLength(80);
    }
}

internal class IntegrationDeliveryConfig : IEntityTypeConfiguration<IntegrationDelivery>
{
    public void Configure(EntityTypeBuilder<IntegrationDelivery> b)
    {
        b.ToTable("IntegrationDeliveries");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).UseIdentityAlwaysColumn();
        b.Property(d => d.Event).HasMaxLength(50);
        b.Property(d => d.Title).HasMaxLength(300);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(d => new { d.IntegrationId, d.Id });
        b.HasOne<IntegrationConfiguration>().WithMany().HasForeignKey(d => d.IntegrationId).OnDelete(DeleteBehavior.Cascade);
    }
}
