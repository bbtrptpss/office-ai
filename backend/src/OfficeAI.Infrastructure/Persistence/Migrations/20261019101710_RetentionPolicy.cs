using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunTranscriptDays = table.Column<int>(type: "integer", nullable: true),
                    TestOutputDays = table.Column<int>(type: "integer", nullable: true),
                    NotificationDays = table.Column<int>(type: "integer", nullable: true),
                    IntegrationDeliveryDays = table.Column<int>(type: "integer", nullable: true),
                    ChatMessageDays = table.Column<int>(type: "integer", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastResultJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionPolicies");
        }
    }
}
