ALTER TABLE kafka_workflow_events
    ALTER COLUMN tenant_id TYPE varchar(256);

ALTER TABLE kafka_workflow_events
    ADD COLUMN IF NOT EXISTS permanently_failed_at timestamptz;

CREATE INDEX IF NOT EXISTS ix_kafka_workflow_failed
    ON kafka_workflow_events (tenant_id, workflow_id)
    WHERE permanently_failed_at IS NOT NULL;

CREATE OR REPLACE FUNCTION enforce_document_publication_order()
RETURNS trigger
LANGUAGE plpgsql
AS $function$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM outbox_messages earlier
        WHERE earlier.tenant_id = NEW.tenant_id
          AND earlier.document_id = NEW.document_id
          AND earlier.document_version < NEW.document_version
          AND NOT EXISTS (
              SELECT 1
              FROM published_document_events published
              WHERE published.event_id = earlier.id
          )
    ) THEN
        RAISE EXCEPTION 'Earlier document event remains unpublished';
    END IF;

    RETURN NEW;
END;
$function$;

DROP TRIGGER IF EXISTS trg_document_publication_order
    ON published_document_events;

CREATE TRIGGER trg_document_publication_order
BEFORE INSERT ON published_document_events
FOR EACH ROW
EXECUTE FUNCTION enforce_document_publication_order();
