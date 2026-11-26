BEGIN;

INSERT INTO workflows (
    id, tenant_id, requester_id, request, status,
    created_at_utc, updated_at_utc
)
VALUES (
    'f7000000-0000-0000-0000-000000000020',
    'e7-rejection-test',
    'e7-requester',
    'Rejection acceptance',
    'Created',
    '2026-11-26T00:00:00Z',
    '2026-11-26T00:00:00Z'
);

UPDATE workflows
SET status = 'Running',
    updated_at_utc = '2026-11-26T00:00:01Z'
WHERE id = 'f7000000-0000-0000-0000-000000000020';

DO $test$
DECLARE
    rejected boolean := false;
BEGIN
    BEGIN
        UPDATE workflows
        SET status = 'Rejected'
        WHERE id = 'f7000000-0000-0000-0000-000000000020';
    EXCEPTION WHEN raise_exception THEN
        rejected := true;
    END;

    IF NOT rejected THEN
        RAISE EXCEPTION 'Invalid rejection was permitted';
    END IF;
END;
$test$;

UPDATE workflows
SET status = 'WaitingForApproval',
    updated_at_utc = '2026-11-26T00:00:02Z'
WHERE id = 'f7000000-0000-0000-0000-000000000020';

UPDATE workflows
SET status = 'Rejected',
    updated_at_utc = '2026-11-26T00:00:05Z'
WHERE id = 'f7000000-0000-0000-0000-000000000020';

UPDATE workflows
SET status = 'Rejected'
WHERE id = 'f7000000-0000-0000-0000-000000000020';

DO $test$
DECLARE
    kinds text[];
    sequences bigint[];
    duration bigint;
BEGIN
    SELECT
        array_agg(payload->>'eventType' ORDER BY sequence),
        array_agg(sequence ORDER BY sequence)
    INTO kinds, sequences
    FROM kafka_workflow_events
    WHERE workflow_id = 'f7000000-0000-0000-0000-000000000020';

    IF kinds IS DISTINCT FROM
        ARRAY['STARTED','APPROVAL_REQUIRED','REJECTED']
    THEN
        RAISE EXCEPTION 'Unexpected event history: %', kinds;
    END IF;

    IF sequences IS DISTINCT FROM ARRAY[1,2,3]::bigint[] THEN
        RAISE EXCEPTION 'Invalid event sequence';
    END IF;

    SELECT (payload->>'durationMillis')::bigint
    INTO duration
    FROM kafka_workflow_events
    WHERE workflow_id = 'f7000000-0000-0000-0000-000000000020'
      AND sequence = 3;

    IF duration IS DISTINCT FROM 4000::bigint THEN
        RAISE EXCEPTION 'Invalid rejection duration: %', duration;
    END IF;

    RAISE NOTICE 'Rejection lifecycle acceptance passed';
END;
$test$;

ROLLBACK;

DO $test$
BEGIN
    IF EXISTS (
        SELECT 1 FROM workflows
        WHERE id = 'f7000000-0000-0000-0000-000000000020'
    ) OR EXISTS (
        SELECT 1 FROM kafka_workflow_events
        WHERE workflow_id = 'f7000000-0000-0000-0000-000000000020'
    ) THEN
        RAISE EXCEPTION 'Atomic rollback failed';
    END IF;

    RAISE NOTICE 'Atomic rollback passed';
END;
$test$;
