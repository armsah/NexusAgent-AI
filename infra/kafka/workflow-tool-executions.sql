CREATE TABLE workflow_tool_executions (
    approval_id uuid PRIMARY KEY REFERENCES agent_approvals("Id") ON DELETE CASCADE,
    workflow_id uuid NOT NULL REFERENCES workflows(id) ON DELETE CASCADE,
    tenant_id varchar(256) NOT NULL,
    requester_id varchar(256) NOT NULL,
    tool_name varchar(256) NOT NULL,
    payload_digest varchar(64) NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'Executing',
    started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    finished_at timestamptz,
    CONSTRAINT ck_workflow_tool_execution_status
        CHECK (status IN ('Executing', 'Succeeded', 'Failed', 'Uncertain')),
    CONSTRAINT ck_workflow_tool_execution_finished
        CHECK (
            (status = 'Executing' AND finished_at IS NULL)
            OR
            (status <> 'Executing' AND finished_at IS NOT NULL)
        )
);

CREATE INDEX ix_workflow_tool_execution_pending
    ON workflow_tool_executions(started_at)
    WHERE status = 'Executing';

CREATE FUNCTION capture_workflow_tool_execution()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF NEW."WorkflowId" IS NOT NULL
       AND OLD."Status" = 1
       AND NEW."Status" = 4 THEN
        INSERT INTO workflow_tool_executions (
            approval_id, workflow_id, tenant_id,
            requester_id, tool_name, payload_digest
        )
        VALUES (
            NEW."Id", NEW."WorkflowId", NEW."TenantId",
            NEW."RequesterId", NEW."ToolName", NEW."PayloadDigest"
        );
    END IF;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_workflow_tool_execution
AFTER UPDATE OF "Status" ON agent_approvals
FOR EACH ROW
EXECUTE FUNCTION capture_workflow_tool_execution();
