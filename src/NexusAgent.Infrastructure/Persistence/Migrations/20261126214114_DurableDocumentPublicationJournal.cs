using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Infrastructure.Persistence.Migrations;

[DbContext(typeof(NexusAgentDbContext))]
[Migration("20261126214114_DurableDocumentPublicationJournal")]
public sealed class DurableDocumentPublicationJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS public.kafka_document_publications (
                event_id uuid PRIMARY KEY,
                tenant_id varchar(256) NOT NULL,
                document_id uuid NOT NULL,
                document_version integer NOT NULL CHECK (document_version > 0),
                event_type varchar(200) NOT NULL,
                partition_key text NOT NULL,
                wire_payload text NOT NULL,
                prepared_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                acknowledged_at timestamptz NULL,
                CONSTRAINT ck_document_publication_payload
                    CHECK (
                        octet_length(wire_payload) BETWEEN 1 AND 16384
                        AND (wire_payload::jsonb ->> 'schemaVersion') = '1'
                        AND (wire_payload::jsonb ->> 'eventId')::uuid = event_id
                        AND (wire_payload::jsonb ->> 'tenantId') = tenant_id
                        AND (wire_payload::jsonb ->> 'documentId')::uuid = document_id
                        AND (wire_payload::jsonb ->> 'documentVersion')::integer = document_version
                        AND (wire_payload::jsonb ->> 'eventType') = event_type
                    )
            );

            CREATE INDEX IF NOT EXISTS ix_kafka_document_publications_pending
            ON public.kafka_document_publications (prepared_at)
            WHERE acknowledged_at IS NULL;

            CREATE OR REPLACE FUNCTION public.protect_document_publication()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $function$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Publication journal deletion prohibited';
                END IF;

                IF ROW(
                    NEW.event_id,
                    NEW.tenant_id,
                    NEW.document_id,
                    NEW.document_version,
                    NEW.event_type,
                    NEW.partition_key,
                    NEW.wire_payload,
                    NEW.prepared_at
                ) IS DISTINCT FROM ROW(
                    OLD.event_id,
                    OLD.tenant_id,
                    OLD.document_id,
                    OLD.document_version,
                    OLD.event_type,
                    OLD.partition_key,
                    OLD.wire_payload,
                    OLD.prepared_at
                ) THEN
                    RAISE EXCEPTION 'Publication envelope is immutable';
                END IF;

                IF OLD.acknowledged_at IS NOT NULL
                   AND NEW.acknowledged_at IS DISTINCT FROM OLD.acknowledged_at THEN
                    RAISE EXCEPTION 'Publication acknowledgement is immutable';
                END IF;

                RETURN NEW;
            END;
            $function$;

            DROP TRIGGER IF EXISTS protect_document_publication
            ON public.kafka_document_publications;

            CREATE TRIGGER protect_document_publication
            BEFORE UPDATE OR DELETE
            ON public.kafka_document_publications
            FOR EACH ROW
            EXECUTE FUNCTION public.protect_document_publication();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new global::System.InvalidOperationException(
            "Publication journal rollback requires an explicit archival procedure.");
    }
}
