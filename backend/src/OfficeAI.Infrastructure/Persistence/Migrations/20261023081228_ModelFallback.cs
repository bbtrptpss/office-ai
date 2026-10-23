using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FallbackRank",
                table: "AiModels",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FallbackRank",
                table: "AiModels");
        }
    }
}
