using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeEmbeddings",
                columns: table => new
                {
                    KnowledgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vector = table.Column<float[]>(type: "real[]", nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeEmbeddings", x => x.KnowledgeId);
                    table.ForeignKey(
                        name: "FK_KnowledgeEmbeddings_KnowledgeEntries_KnowledgeId",
                        column: x => x.KnowledgeId,
                        principalTable: "KnowledgeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeEmbeddings");
        }
    }
}
