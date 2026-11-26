BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

WITH publications AS (
    SELECT
        'document' AS family,
        event_id,
        tenant_id,
        document_id AS aggregate_id,
        document_version::bigint AS sequence,
        event_type,
        payload,
        CASE
            WHEN event_type NOT IN (
                'document.created',
                'document.updated',
                'document.deleted'
            ) THEN 'NON_STREAM'
            WHEN payload ? 'schemaVersion'
            THEN 'WIRE_ENVELOPE'
            ELSE 'UNRESOLVED'
        END AS classification
    FROM public.published_document_events

    UNION ALL

    SELECT
        'workflow',
        event_id,
        tenant_id,
        workflow_id,
        sequence,
        payload ->> 'eventType',
        payload,
        'WIRE_ENVELOPE'
    FROM public.kafka_workflow_events
    WHERE published_at IS NOT NULL
)
SELECT json_build_object(
    'totalPublications', count(*),
    'nonStreamPublications',
        count(*) FILTER (
            WHERE classification = 'NON_STREAM'
        ),
    'unresolvedEvents',
        count(*) FILTER (
            WHERE classification = 'UNRESOLVED'
        ),
    'events',
        COALESCE(
            json_agg(
                json_build_object(
                    'family', family,
                    'eventId', event_id,
                    'tenantId', tenant_id,
                    'aggregateId', aggregate_id,
                    'sequence', sequence,
                    'eventType', event_type,
                    'payload', payload
                )
                ORDER BY family, tenant_id, aggregate_id, sequence
            ) FILTER (
                WHERE classification = 'WIRE_ENVELOPE'
            ),
            '[]'::json
        )
)
FROM publications;

COMMIT;
