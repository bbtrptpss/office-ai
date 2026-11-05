using Microsoft.Extensions.Configuration;
using Npgsql;

namespace OfficeAI.Infrastructure.Security;

/// <summary>
/// With Security:RequireStrongSecrets on (the production compose file), the server refuses to start on example values or very short
/// secrets. The compose file can only tell that a value is set. Messages name the setting and never show its value.
/// </summary>
public static class StrongSecrets
{
    private const int MinLength = 16;

    private static readonly string[] Placeholders = ["change-me", "changeme", "dev-runner-token", "officeai_dev", "changeme-dev-2026", "password", "secret"];

    public static IReadOnlyList<string> Problems(IConfiguration config)
    {
        var problems = new List<string>();
        Check("Sandbox:RunnerToken", config["Sandbox:RunnerToken"]);
        Check("Seed:CeoPassword", config["Seed:CeoPassword"]);
        var connection = config.GetConnectionString("Default");
        if (!string.IsNullOrEmpty(connection)) Check("the database password", new NpgsqlConnectionStringBuilder(connection).Password);
        return problems;

        void Check(string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return; // a missing setting has its own error where it is used
            if (Placeholders.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)) problems.Add($"{name} is still an example value.");
            else if (value.Length < MinLength) problems.Add($"{name} is shorter than {MinLength} characters.");
        }
    }

    public static void Enforce(IConfiguration config)
    {
        if (!config.GetValue("Security:RequireStrongSecrets", false)) return;
        var problems = Problems(config);
        if (problems.Count > 0) throw new InvalidOperationException("Refusing to start: " + string.Join(" ", problems));
    }
}
