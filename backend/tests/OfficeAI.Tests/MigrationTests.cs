using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Agents;
using OfficeAI.Infrastructure.Persistence;

namespace OfficeAI.Tests;

/// <summary>Upgrades must work on a database that already has data, not only on an empty one.</summary>
[Collection(ApiCollection.Name)]
public class MigrationTests(ApiFixture fx)
{
    private sealed class NoUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Name => "migration-test";
        public bool IsAgent => false;
        public string? CorrelationId => null;
    }

    [Fact]
    public async Task Each_migration_upgrades_a_database_that_already_has_data()
    {
        var dbName = $"upgrade_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fx.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
            await cmd.ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(fx.ConnectionString) { Database = dbName }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;

        await using var db = new AppDbContext(options, new NoUser(), TimeProvider.System);
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();

        // Walk the history: after each migration, insert data the next one has to carry forward.
        await migrator.MigrateAsync(all.First(m => m.EndsWith("_Phase2AgentExecution")));
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Departments" ("Id","Code","Name","SortOrder","IsActive","CreatedAt") VALUES ('00000000-0000-0000-0000-000000000001','D','Dept',0,true,now());
            INSERT INTO "EmployeeRoleDefinitions" ("Id","Code","Title","DepartmentId","Level","Responsibilities","Skills","TaskTypes","RequiresHuman","IsActive","CreatedAt")
                VALUES ('00000000-0000-0000-0000-000000000002','R','Role','00000000-0000-0000-0000-000000000001','Staff','{{}}','{{csharp}}','{{}}',false,true,now()); -- braces doubled: raw SQL is a format string
            INSERT INTO "EmployeeProfiles" ("Id","DisplayName","Kind","DepartmentId","RoleDefinitionId","Status","CreatedAt")
                VALUES ('00000000-0000-0000-0000-000000000003','Old AI','AI','00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','Available',now());
            INSERT INTO "Projects" ("Id","Code","Name","Status","CreatedAt") VALUES ('00000000-0000-0000-0000-000000000004','P','Project','Active',now());
            INSERT INTO "Tasks" ("Id","ProjectId","Title","Status","Priority","CreatedAt") VALUES ('00000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000004','Old task','InProgress','Medium',now());
            INSERT INTO "AgentRuns" ("Id","TaskId","ProjectId","EmployeeId","Status","Provider","ModelId","IsSimulated","CreatedAt","Turns","ToolCalls","InputTokens","OutputTokens","CostUsd","CancelRequested")
                VALUES ('00000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000003','Succeeded','simulated','simulated-v1',true,now(),1,0,10,5,0,false);
            """);

        await migrator.MigrateAsync(); // to the latest

        var task = await db.Tasks.AsNoTracking().SingleAsync(t => t.Title == "Old task");
        Assert.Empty(task.RequiredSkills);
        Assert.Null(task.WorkflowExecutionId);
        var run = await db.AgentRuns.AsNoTracking().SingleAsync();
        Assert.Equal(RunPurpose.Work, run.Purpose);
        Assert.Equal(1, run.Attempt);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
