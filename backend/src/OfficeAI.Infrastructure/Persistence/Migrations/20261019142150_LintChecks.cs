using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LintChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "TestRuns",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Test");

            migrationBuilder.AddColumn<string>(
                name: "LintCommand",
                table: "CodeRepositories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "LintCommand",
                table: "CodeRepositories");
        }
    }
}
