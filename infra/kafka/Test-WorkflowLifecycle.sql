BEGIN;

INSERT INTO workflows
(id, requester_id, tenant_id, request, status,
 created_at_utc, updated_at_utc)
VALUES (
    'f7000000-0000-0000-0000-000000000010',
    'e7-requester',
    'e7-lifecycle-test',
    'Lifecycle acceptance',
    'Created',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z'
);

UPDATE workflows
SET status = 'Running',
    updated_at_utc = '2026-01-01T00:00:01Z'
WHERE id = 'f7000000-0000-0000-0000-000000000010';

UPDATE workflows
SET status = 'Running',
    updated_at_utc = '2026-01-01T00:00:02Z'
WHERE id = 'f7000000-0000-0000-0000-000000000010';

UPDATE workflows
SET status = 'WaitingForApproval',
    updated_at_utc = '2026-01-01T00:00:03Z'
WHERE id = 'f7000000-0000-0000-0000-000000000010';

UPDATE workflows
SET status = 'Running',
    updated_at_utc = '2026-01-01T00:00:04Z'
WHERE id = 'f7000000-0000-0000-0000-000000000010';

UPDATE workflows
SET status = 'Completed',
    updated_at_utc = '2026-01-01T00:00:05Z'
WHERE id = 'f7000000-0000-0000-0000-000000000010';

DO $$
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
    WHERE tenant_id = 'e7-lifecycle-test';

    IF kinds IS DISTINCT FROM ARRAY[
        'STARTED','APPROVAL_REQUIRED','APPROVED','COMPLETED'
    ] THEN
        RAISE EXCEPTION 'Lifecycle event mismatch: %', kinds;
    END IF;

    IF sequences IS DISTINCT FROM ARRAY[1,2,3,4]::bigint[] THEN
        RAISE EXCEPTION 'Sequence mismatch';
    END IF;

    SELECT (payload->>'durationMillis')::bigint
    INTO duration
    FROM kafka_workflow_events
    WHERE tenant_id = 'e7-lifecycle-test'
      AND sequence = 4;

    IF duration IS DISTINCT FROM 4000 THEN
        RAISE EXCEPTION 'Duration mismatch';
    END IF;

    RAISE NOTICE 'Lifecycle sequence, duplicate suppression and duration passed';
END $$;

ROLLBACK;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM kafka_workflow_events
        WHERE tenant_id = 'e7-lifecycle-test'
    ) OR EXISTS (
        SELECT 1 FROM workflows
        WHERE tenant_id = 'e7-lifecycle-test'
    ) THEN
        RAISE EXCEPTION 'Atomic rollback failed';
    END IF;

    RAISE NOTICE 'Atomic workflow and event rollback passed';
END $$;