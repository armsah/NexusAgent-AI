using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusAgent.Infrastructure.Persistence.Migrations;

[DbContext(typeof(NexusAgentDbContext))]
[Migration("20261126220000_AddApprovalExpiration")]
public sealed class AddApprovalExpiration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION expire_workflow_approvals(batch_size integer)
            RETURNS integer
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                item record;
                resolved integer := 0;
                cutoff timestamptz := clock_timestamp();
            BEGIN
                IF batch_size IS NULL OR batch_size < 1 OR batch_size > 1000 THEN
                    RAISE EXCEPTION 'Invalid approval expiration batch size';
                END IF;

                FOR item IN
                    SELECT a."Id", a."WorkflowId", a."TenantId", a."RequesterId"
                    FROM agent_approvals a
                    WHERE a."Status" = 0
                      AND a."ExpiresAt" <= cutoff
                    ORDER BY a."ExpiresAt", a."Id"
                    LIMIT batch_size
                    FOR UPDATE OF a SKIP LOCKED
                LOOP
                    IF item."WorkflowId" IS NOT NULL THEN
                        UPDATE workflows
                        SET status = 'Cancelled',
                            updated_at_utc = GREATEST(updated_at_utc, cutoff)
                        WHERE id = item."WorkflowId"
                          AND tenant_id = item."TenantId"
                          AND requester_id = item."RequesterId"
                          AND status = 'WaitingForApproval';
                    END IF;

                    UPDATE agent_approvals
                    SET "Status" = 3
                    WHERE "Id" = item."Id"
                      AND "Status" = 0;

                    resolved := resolved + 1;
                END LOOP;

                RETURN resolved;
            END;
            $function$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP FUNCTION IF EXISTS expire_workflow_approvals(integer);");
    }
}
