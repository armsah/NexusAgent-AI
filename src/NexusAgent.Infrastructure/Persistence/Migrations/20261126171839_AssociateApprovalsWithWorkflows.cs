using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssociateApprovalsWithWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowId",
                table: "agent_approvals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_approvals_WorkflowId",
                table: "agent_approvals",
                column: "WorkflowId",
                unique: true,
                filter: "\"WorkflowId\" IS NOT NULL AND \"Status\" = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_approvals_workflows_WorkflowId",
                table: "agent_approvals",
                column: "WorkflowId",
                principalTable: "workflows",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_approvals_workflows_WorkflowId",
                table: "agent_approvals");

            migrationBuilder.DropIndex(
                name: "IX_agent_approvals_WorkflowId",
                table: "agent_approvals");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                table: "agent_approvals");
        }
    }
}
