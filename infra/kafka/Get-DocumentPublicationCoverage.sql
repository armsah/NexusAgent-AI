BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

SELECT json_build_object(
    'preparedDocumentPublications',
        (SELECT count(*) FROM public.kafka_document_publications),
    'acknowledgedDocumentPublications',
        (SELECT count(*) FROM public.kafka_document_publications
         WHERE acknowledged_at IS NOT NULL),
    'pendingDocumentPublications',
        (SELECT count(*) FROM public.kafka_document_publications
         WHERE acknowledged_at IS NULL),
    'legacyUnresolvedPublications',
        (SELECT count(*) FROM public.published_document_events
         WHERE event_type IN (
             'document.created',
             'document.updated',
             'document.deleted'
         )
         AND NOT (payload ? 'schemaVersion')),
    'acknowledgedWorkflowPublications',
        (SELECT count(*) FROM public.kafka_workflow_events
         WHERE published_at IS NOT NULL)
);

COMMIT;
