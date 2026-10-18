using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8GitRemotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteError",
                table: "CodeRepositories",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemotePushedAt",
                table: "CodeRepositories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemotePushedSha",
                table: "CodeRepositories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteTokenEnvVar",
                table: "CodeRepositories",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteUrl",
                table: "CodeRepositories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteError",
                table: "CodeRepositories");

            migrationBuilder.DropColumn(
                name: "RemotePushedAt",
                table: "CodeRepositories");

            migrationBuilder.DropColumn(
                name: "RemotePushedSha",
                table: "CodeRepositories");

            migrationBuilder.DropColumn(
                name: "RemoteTokenEnvVar",
                table: "CodeRepositories");

            migrationBuilder.DropColumn(
                name: "RemoteUrl",
                table: "CodeRepositories");
        }
    }
}
