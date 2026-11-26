using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableAgentExecutionAndApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequesterId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Risk = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApproverId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_approvals", x => x.Id);
                    table.CheckConstraint("ck_agent_approval_expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("ck_agent_approval_risk", "\"Risk\" BETWEEN 2 AND 3");
                    table.CheckConstraint("ck_agent_approval_status", "\"Status\" BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "agent_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequesterId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlanVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NextStage = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinalAnswer = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_executions", x => x.Id);
                    table.CheckConstraint("ck_agent_execution_attempt", "\"AttemptCount\" BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_agent_execution_deadline", "\"DeadlineAt\" > \"CreatedAt\"");
                    table.CheckConstraint("ck_agent_execution_lease", "(\"Status\" = 'Running' AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL) OR (\"Status\" <> 'Running' AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
                    table.CheckConstraint("ck_agent_execution_stage", "\"NextStage\" BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_agent_execution_status", "\"Status\" IN ('Pending','Running','Completed','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_agent_executions_workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_execution_checkpoints",
                columns: table => new
                {
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageNumber = table.Column<int>(type: "integer", nullable: false),
                    InvocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Output = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_execution_checkpoints", x => new { x.ExecutionId, x.StageNumber });
                    table.CheckConstraint("ck_agent_checkpoint_stage", "\"StageNumber\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_agent_execution_checkpoints_agent_executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "agent_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_approvals_TenantId_Status_ExpiresAt",
                table: "agent_approvals",
                columns: new[] { "TenantId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_execution_checkpoints_InvocationId",
                table: "agent_execution_checkpoints",
                column: "InvocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_DeadlineAt",
                table: "agent_executions",
                column: "DeadlineAt");

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_Status_LeaseExpiresAt",
                table: "agent_executions",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_TenantId_RequesterId_WorkflowId",
                table: "agent_executions",
                columns: new[] { "TenantId", "RequesterId", "WorkflowId" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_executions_WorkflowId",
                table: "agent_executions",
                column: "WorkflowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_approvals");

            migrationBuilder.DropTable(
                name: "agent_execution_checkpoints");

            migrationBuilder.DropTable(
                name: "agent_executions");
        }
    }
}
