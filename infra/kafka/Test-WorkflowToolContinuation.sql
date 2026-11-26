BEGIN;

DO $test$
DECLARE
    workflow_id_value uuid;
    approval_id_value uuid;
    scenario text;
    expected_status text;
    expected_receipt text;
    actual_status text;
    actual_receipt text;
    event_types text[];
    recovered integer;
    blocked boolean;
    stamp timestamptz;
BEGIN
    FOREACH scenario IN ARRAY ARRAY[
        'success', 'failure', 'interrupted', 'uncertain'
    ]
    LOOP
        workflow_id_value := gen_random_uuid();
        approval_id_value := gen_random_uuid();
        stamp := clock_timestamp();

        INSERT INTO workflows (
            id, tenant_id, requester_id, request,
            status, created_at_utc, updated_at_utc
        )
        VALUES (
            workflow_id_value, 'e7-continuation-acceptance',
            'requester', 'Approved action acceptance',
            'Created', stamp, stamp
        );

        UPDATE workflows
        SET status = 'Running',
            updated_at_utc = clock_timestamp()
        WHERE id = workflow_id_value;

        UPDATE workflows
        SET status = 'WaitingForApproval',
            updated_at_utc = clock_timestamp()
        WHERE id = workflow_id_value;

        INSERT INTO agent_approvals (
            "Id", "WorkflowId", "TenantId", "RequesterId",
            "ToolName", "PayloadDigest", "Risk", "Status",
            "CreatedAt", "ExpiresAt"
        )
        VALUES (
            approval_id_value, workflow_id_value,
            'e7-continuation-acceptance', 'requester',
            'acceptance-tool', repeat('a', 64),
            2, 0, stamp, stamp + interval '1 hour'
        );

        UPDATE agent_approvals
        SET "Status" = 1, "ApproverId" = 'approver'
        WHERE "Id" = approval_id_value;

        UPDATE workflows
        SET status = 'Running',
            updated_at_utc = clock_timestamp()
        WHERE id = workflow_id_value;

        blocked := false;

        BEGIN
            UPDATE workflows
            SET status = 'Completed'
            WHERE id = workflow_id_value;
        EXCEPTION WHEN raise_exception THEN
            blocked := true;
        END;

        IF NOT blocked THEN
            RAISE EXCEPTION 'Premature completion was permitted';
        END IF;

        UPDATE agent_approvals
        SET "Status" = 4
        WHERE "Id" = approval_id_value;

        SELECT status INTO actual_receipt
        FROM workflow_tool_executions
        WHERE approval_id = approval_id_value;

        IF actual_receipt IS DISTINCT FROM 'Executing' THEN
            RAISE EXCEPTION 'Atomic execution receipt missing';
        END IF;

        blocked := false;

        BEGIN
            UPDATE workflows
            SET status = 'Completed'
            WHERE id = workflow_id_value;
        EXCEPTION WHEN raise_exception THEN
            blocked := true;
        END;

        IF NOT blocked THEN
            RAISE EXCEPTION 'Active execution bypass was permitted';
        END IF;

        expected_status := CASE
            WHEN scenario = 'success' THEN 'Completed'
            ELSE 'Failed'
        END;

        expected_receipt := CASE scenario
            WHEN 'success' THEN 'Succeeded'
            WHEN 'failure' THEN 'Failed'
            ELSE 'Uncertain'
        END;

        IF scenario = 'interrupted' THEN
            UPDATE workflow_tool_executions
            SET started_at = clock_timestamp() - interval '6 minutes'
            WHERE approval_id = approval_id_value;

            recovered := recover_workflow_tool_executions(1000);

            IF recovered < 1 THEN
                RAISE EXCEPTION 'Interrupted execution not recovered';
            END IF;
        ELSE
            UPDATE workflow_tool_executions
            SET status = expected_receipt,
                finished_at = clock_timestamp()
            WHERE approval_id = approval_id_value
              AND status = 'Executing';
        END IF;

        SELECT status INTO actual_status
        FROM workflows
        WHERE id = workflow_id_value;

        SELECT status INTO actual_receipt
        FROM workflow_tool_executions
        WHERE approval_id = approval_id_value;

        IF actual_status IS DISTINCT FROM expected_status
           OR actual_receipt IS DISTINCT FROM expected_receipt THEN
            RAISE EXCEPTION 'Continuation outcome mismatch: %', scenario;
        END IF;

        SELECT array_agg(
            payload->>'eventType' ORDER BY sequence
        )
        INTO event_types
        FROM kafka_workflow_events
        WHERE workflow_id = workflow_id_value;

        IF event_types IS DISTINCT FROM ARRAY[
            'STARTED',
            'APPROVAL_REQUIRED',
            'APPROVED',
            CASE WHEN scenario = 'success'
                THEN 'COMPLETED' ELSE 'FAILED' END
        ] THEN
            RAISE EXCEPTION 'Lifecycle sequence mismatch: %', event_types;
        END IF;

        PERFORM recover_workflow_tool_executions(1000);

        UPDATE workflow_tool_executions
        SET status = 'Succeeded',
            finished_at = clock_timestamp()
        WHERE approval_id = approval_id_value
          AND status = 'Executing';

        IF FOUND THEN
            RAISE EXCEPTION 'Terminal execution accepted late completion';
        END IF;

        IF (
            SELECT count(*)
            FROM kafka_workflow_events
            WHERE workflow_id = workflow_id_value
        ) <> 4 THEN
            RAISE EXCEPTION 'Duplicate terminal lifecycle event';
        END IF;

        RAISE NOTICE 'PASS: %', scenario;
    END LOOP;
END;
$test$;

ROLLBACK;
