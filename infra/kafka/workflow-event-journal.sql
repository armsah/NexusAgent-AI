BEGIN;

CREATE TABLE IF NOT EXISTS kafka_workflow_events (
    event_id uuid PRIMARY KEY,
    tenant_id varchar(200) NOT NULL,
    workflow_id uuid NOT NULL,
    sequence bigint NOT NULL CHECK (sequence > 0),
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    published_at timestamptz NULL,
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_error varchar(200) NULL,

    CONSTRAINT uq_kafka_workflow_sequence
        UNIQUE (tenant_id, workflow_id, sequence),

    CONSTRAINT ck_kafka_workflow_identity CHECK (
        payload ? 'eventId'
        AND payload ? 'tenantId'
        AND payload ? 'workflowId'
        AND payload ? 'sequence'
        AND payload ? 'schemaVersion'
        AND (payload->>'eventId')::uuid = event_id
        AND payload->>'tenantId' = tenant_id
        AND (payload->>'workflowId')::uuid = workflow_id
        AND (payload->>'sequence')::bigint = sequence
        AND (payload->>'schemaVersion')::integer = 1
    )
);

CREATE INDEX IF NOT EXISTS ix_kafka_workflow_pending
ON kafka_workflow_events(next_attempt_at, created_at)
WHERE published_at IS NULL;

COMMIT;