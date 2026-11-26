BEGIN;

DO $test$
DECLARE
    eid uuid := gen_random_uuid();
    did uuid := gen_random_uuid();
    wire text;
    stored text;
    ack timestamptz;
    rejected boolean;
BEGIN
    wire := json_build_object(
        'schemaVersion', 1,
        'eventId', eid,
        'eventType', 'document.created',
        'tenantId', 'journal-acceptance',
        'documentId', did,
        'documentVersion', 1,
        'contentVersion', 1,
        'occurredAt', clock_timestamp()
    )::text;

    INSERT INTO public.kafka_document_publications (
        event_id, tenant_id, document_id,
        document_version, event_type,
        partition_key, wire_payload
    ) VALUES (
        eid, 'journal-acceptance', did,
        1, 'document.created',
        '18:journal-acceptance:' || did::text, wire
    );

    SELECT wire_payload, acknowledged_at
    INTO stored, ack
    FROM public.kafka_document_publications
    WHERE event_id = eid;

    IF stored IS DISTINCT FROM wire OR ack IS NOT NULL THEN
        RAISE EXCEPTION 'Prepared intent verification failed';
    END IF;

    rejected := false;

    BEGIN
        UPDATE public.kafka_document_publications
        SET wire_payload = wire_payload || ' '
        WHERE event_id = eid;
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM <> 'Publication envelope is immutable' THEN
            RAISE;
        END IF;
        rejected := true;
    END;

    IF NOT rejected THEN
        RAISE EXCEPTION 'Envelope mutation permitted';
    END IF;

    UPDATE public.kafka_document_publications
    SET acknowledged_at = clock_timestamp()
    WHERE event_id = eid;

    SELECT acknowledged_at INTO ack
    FROM public.kafka_document_publications
    WHERE event_id = eid;

    IF ack IS NULL THEN
        RAISE EXCEPTION 'Acknowledgement missing';
    END IF;

    rejected := false;

    BEGIN
        UPDATE public.kafka_document_publications
        SET acknowledged_at = NULL
        WHERE event_id = eid;
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM <> 'Publication acknowledgement is immutable' THEN
            RAISE;
        END IF;
        rejected := true;
    END;

    IF NOT rejected THEN
        RAISE EXCEPTION 'Acknowledgement reset permitted';
    END IF;

    rejected := false;

    BEGIN
        DELETE FROM public.kafka_document_publications
        WHERE event_id = eid;
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM <> 'Publication journal deletion prohibited' THEN
            RAISE;
        END IF;
        rejected := true;
    END;

    IF NOT rejected THEN
        RAISE EXCEPTION 'Journal deletion permitted';
    END IF;

    RAISE NOTICE 'JOURNAL_DATABASE_ACCEPTANCE_PASSED';
END;
$test$;

ROLLBACK;

SELECT count(*) AS retained_acceptance_records
FROM public.kafka_document_publications
WHERE tenant_id = 'journal-acceptance';
