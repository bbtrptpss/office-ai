using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Application.Organization;
using OfficeAI.Application.Work;
using OfficeAI.Infrastructure.Persistence;
using OfficeAI.Infrastructure.Security;

namespace OfficeAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        // Auth cookie keys survive restarts and are shared by every API instance.
        services.AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("OfficeAI");
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.Configure<SeedOptions>(config.GetSection("Seed"));
        services.AddScoped<Seeder>();

        services.AddScoped<OrganizationService>();
        services.AddScoped<WorkService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<SecurityAdminService>();
        services.AddScoped<SecurityAuditWriter>();
        return services;
    }
}
