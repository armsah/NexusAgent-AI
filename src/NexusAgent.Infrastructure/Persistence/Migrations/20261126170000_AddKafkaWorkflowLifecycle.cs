using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusAgent.Infrastructure.Persistence.Migrations;

[DbContext(typeof(NexusAgentDbContext))]
[Migration("20261126170000_AddKafkaWorkflowLifecycle")]
public sealed class AddKafkaWorkflowLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """

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

            CREATE OR REPLACE FUNCTION capture_kafka_workflow_event()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            DECLARE
                event_kind text;
                event_sequence bigint;
                event_identifier uuid;
                event_duration bigint;
                started_at timestamptz;
            BEGIN
                IF TG_OP = 'UPDATE' THEN
                    IF NEW.status = OLD.status THEN
                        RETURN NEW;
                    END IF;
                END IF;

                IF NEW.status = 'Created' THEN
                    RETURN NEW;
                END IF;

                event_kind := CASE NEW.status
                    WHEN 'Running' THEN
                        CASE WHEN TG_OP = 'UPDATE'
                            AND OLD.status = 'WaitingForApproval'
                            THEN 'APPROVED'
                            ELSE 'STARTED'
                        END
                    WHEN 'WaitingForApproval' THEN 'APPROVAL_REQUIRED'
                    WHEN 'Completed' THEN 'COMPLETED'
                    WHEN 'Failed' THEN 'FAILED'
                    WHEN 'Cancelled' THEN 'CANCELLED'
                    ELSE NULL
                END;

                IF event_kind IS NULL THEN
                    RAISE EXCEPTION 'Unsupported workflow status';
                END IF;

                SELECT COALESCE(MAX(sequence), 0) + 1
                INTO event_sequence
                FROM kafka_workflow_events
                WHERE tenant_id = NEW.tenant_id
                  AND workflow_id = NEW.id;

                -- Do not invent historical lifecycle events for pre-existing workflows.
                IF event_sequence = 1 AND event_kind <> 'STARTED' THEN
                    RETURN NEW;
                END IF;

                event_identifier := gen_random_uuid();

                IF event_kind IN ('COMPLETED', 'FAILED', 'CANCELLED') THEN
                    SELECT (payload->>'occurredAt')::timestamptz
                    INTO started_at
                    FROM kafka_workflow_events
                    WHERE tenant_id = NEW.tenant_id
                      AND workflow_id = NEW.id
                      AND sequence = 1;

                    event_duration := GREATEST(
                        0,
                        FLOOR(EXTRACT(EPOCH FROM
                            (NEW.updated_at_utc - started_at)) * 1000)
                    )::bigint;
                END IF;

                INSERT INTO kafka_workflow_events
                    (event_id, tenant_id, workflow_id, sequence, payload)
                VALUES (
                    event_identifier,
                    NEW.tenant_id,
                    NEW.id,
                    event_sequence,
                    jsonb_build_object(
                        'schemaVersion', 1,
                        'eventId', event_identifier,
                        'tenantId', NEW.tenant_id,
                        'workflowId', NEW.id,
                        'sequence', event_sequence,
                        'eventType', event_kind,
                        'occurredAt', to_char(
                            NEW.updated_at_utc AT TIME ZONE 'UTC',
                            'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'
                        ),
                        'durationMillis', event_duration
                    )
                );

                RETURN NEW;
            END;
            $function$;

            DROP TRIGGER IF EXISTS trg_kafka_workflow_lifecycle ON workflows;

            CREATE TRIGGER trg_kafka_workflow_lifecycle
            AFTER INSERT OR UPDATE OF status ON workflows
            FOR EACH ROW
            EXECUTE FUNCTION capture_kafka_workflow_event();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS trg_kafka_workflow_lifecycle ON workflows;
            DROP FUNCTION IF EXISTS capture_kafka_workflow_event();
            DROP TABLE IF EXISTS kafka_workflow_events;
            """);
    }
}