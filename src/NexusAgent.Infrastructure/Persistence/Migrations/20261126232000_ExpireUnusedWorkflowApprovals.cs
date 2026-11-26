using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusAgent.Infrastructure.Persistence.Migrations;

[DbContext(typeof(NexusAgentDbContext))]
[Migration("20261126232000_ExpireUnusedWorkflowApprovals")]
public sealed class ExpireUnusedWorkflowApprovals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION expire_unused_workflow_approvals(batch_size integer)
            RETURNS integer
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                item record;
                expired integer := 0;
                cutoff timestamptz := clock_timestamp();
            BEGIN
                IF batch_size < 1 OR batch_size > 1000 THEN
                    RAISE EXCEPTION 'Invalid expiration batch size';
                END IF;

                FOR item IN
                    SELECT a."Id", a."WorkflowId", a."TenantId", a."RequesterId"
                    FROM agent_approvals a
                    WHERE a."WorkflowId" IS NOT NULL
                      AND a."Status" IN (1, 3)
                      AND a."ExpiresAt" <= cutoff
                      AND EXISTS (
                          SELECT 1 FROM workflows w
                          WHERE w.id = a."WorkflowId"
                            AND w.status = 'Running'
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM agent_approvals newer
                          WHERE newer."WorkflowId" = a."WorkflowId"
                            AND newer."Id" <> a."Id"
                            AND newer."CreatedAt" >= a."CreatedAt"
                      )
                    ORDER BY a."ExpiresAt", a."Id"
                    LIMIT batch_size
                    FOR UPDATE OF a SKIP LOCKED
                LOOP
                    UPDATE workflows
                    SET status = 'Cancelled',
                        updated_at_utc = GREATEST(updated_at_utc, cutoff)
                    WHERE id = item."WorkflowId"
                      AND tenant_id = item."TenantId"
                      AND requester_id = item."RequesterId"
                      AND status = 'Running'
                      AND NOT EXISTS (
                          SELECT 1 FROM workflow_tool_executions e
                          WHERE e.workflow_id = item."WorkflowId"
                            AND e.status = 'Executing'
                      )
                      AND NOT EXISTS (
                          SELECT 1 FROM agent_approvals newer
                          JOIN agent_approvals current_approval
                            ON current_approval."Id" = item."Id"
                          WHERE newer."WorkflowId" = item."WorkflowId"
                            AND newer."Id" <> item."Id"
                            AND newer."CreatedAt" >= current_approval."CreatedAt"
                      );

                    IF FOUND THEN
                        UPDATE agent_approvals
                        SET "Status" = 3
                        WHERE "Id" = item."Id"
                          AND "Status" IN (1, 3);

                        expired := expired + 1;
                    END IF;
                END LOOP;

                RETURN expired;
            END;
            $function$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP FUNCTION IF EXISTS expire_unused_workflow_approvals(integer);");
    }
}
