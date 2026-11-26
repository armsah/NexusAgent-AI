CREATE FUNCTION finalize_workflow_tool_execution()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF OLD.status = 'Executing' AND NEW.status <> 'Executing' THEN
        UPDATE workflows
        SET status = CASE
                WHEN NEW.status = 'Succeeded' THEN 'Completed'
                ELSE 'Failed'
            END,
            failure_reason = CASE
                WHEN NEW.status = 'Succeeded' THEN NULL
                WHEN NEW.status = 'Uncertain'
                    THEN 'Tool outcome uncertain; reconciliation required.'
                ELSE 'Approved tool execution failed.'
            END,
            updated_at_utc = GREATEST(
                updated_at_utc, NEW.finished_at)
        WHERE id = NEW.workflow_id
          AND tenant_id = NEW.tenant_id
          AND requester_id = NEW.requester_id
          AND status = 'Running';

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Workflow execution state conflict';
        END IF;
    END IF;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_finalize_workflow_tool_execution
AFTER UPDATE OF status ON workflow_tool_executions
FOR EACH ROW
EXECUTE FUNCTION finalize_workflow_tool_execution();

CREATE FUNCTION guard_workflow_tool_continuation()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF NEW.status <> OLD.status THEN
        IF EXISTS (
            SELECT 1
            FROM workflow_tool_executions e
            WHERE e.workflow_id = NEW.id
              AND e.status = 'Executing'
        ) THEN
            RAISE EXCEPTION 'Workflow tool execution is active';
        END IF;

        IF NEW.status = 'Completed' AND EXISTS (
            SELECT 1
            FROM agent_approvals a
            WHERE a."WorkflowId" = NEW.id
              AND a."Status" IN (0, 1)
        ) THEN
            RAISE EXCEPTION 'Workflow approval has not been consumed';
        END IF;
    END IF;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_guard_workflow_tool_continuation
BEFORE UPDATE OF status ON workflows
FOR EACH ROW
EXECUTE FUNCTION guard_workflow_tool_continuation();

CREATE FUNCTION recover_workflow_tool_executions(batch_size integer)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    item record;
    recovered integer := 0;
BEGIN
    IF batch_size < 1 OR batch_size > 1000 THEN
        RAISE EXCEPTION 'Invalid recovery batch size';
    END IF;

    FOR item IN
        SELECT approval_id
        FROM workflow_tool_executions
        WHERE status = 'Executing'
          AND started_at <= clock_timestamp() - interval '5 minutes'
        ORDER BY started_at, approval_id
        LIMIT batch_size
        FOR UPDATE SKIP LOCKED
    LOOP
        UPDATE workflow_tool_executions
        SET status = 'Uncertain',
            finished_at = clock_timestamp()
        WHERE approval_id = item.approval_id
          AND status = 'Executing';

        recovered := recovered + 1;
    END LOOP;

    RETURN recovered;
END;
$function$;
