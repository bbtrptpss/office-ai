using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OfficeAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8ObjectiveDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "WorkflowExecutions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_DepartmentId",
                table: "WorkflowExecutions",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowExecutions_Departments_DepartmentId",
                table: "WorkflowExecutions",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowExecutions_Departments_DepartmentId",
                table: "WorkflowExecutions");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowExecutions_DepartmentId",
                table: "WorkflowExecutions");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "WorkflowExecutions");
        }
    }
}
