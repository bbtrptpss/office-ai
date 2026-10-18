using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8RunCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConversationJson",
                table: "AgentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResumeCount",
                table: "AgentRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversationJson",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "ResumeCount",
                table: "AgentRuns");
        }
    }
}
