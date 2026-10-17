using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Code : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CodeRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    SandboxImage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TestCommand = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeRepositories_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MergeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SourceBranch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetBranch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HeadSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LatestTestRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    MergedSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MergedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MergeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MergeRequests_CodeRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "CodeRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Image = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Command = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    Output = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestRuns_CodeRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "CodeRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRepositories_ProjectId_Name",
                table: "CodeRepositories",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_ProjectId",
                table: "MergeRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_RepositoryId_Number",
                table: "MergeRequests",
                columns: new[] { "RepositoryId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_RepositoryId_Branch_StartedAt",
                table: "TestRuns",
                columns: new[] { "RepositoryId", "Branch", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_TaskId",
                table: "TestRuns",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MergeRequests");

            migrationBuilder.DropTable(
                name: "TestRuns");

            migrationBuilder.DropTable(
                name: "CodeRepositories");
        }
    }
}
