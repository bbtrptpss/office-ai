using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OfficeAI.Domain.Communication;
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

internal class UserPreferenceConfig : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("UserPreferences");
        b.HasKey(p => p.UserId);
        b.Property(p => p.CalendarTokenHash).HasMaxLength(64);
        b.HasIndex(p => p.CalendarTokenHash).IsUnique();
        b.HasOne<OfficeAI.Infrastructure.Identity.AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal class EmailDeliveryConfig : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> b)
    {
        b.ToTable("EmailDeliveries");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).UseIdentityAlwaysColumn();
        b.Property(d => d.Subject).HasMaxLength(300);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(d => d.Error).HasMaxLength(500);
        b.HasIndex(d => d.NotificationId).IsUnique();
        b.HasIndex(d => new { d.UserId, d.Id });
        b.HasOne<Notification>().WithMany().HasForeignKey(d => d.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}
