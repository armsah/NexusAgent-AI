using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Infrastructure.Persistence.Outbox;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class PublishedDocumentEventConfiguration
    : IEntityTypeConfiguration<PublishedDocumentEvent>
{
    public void Configure(
        EntityTypeBuilder<PublishedDocumentEvent> builder)
    {
        builder.ToTable("published_document_events");

        builder.HasKey(e => e.EventId);

        builder.Property(e => e.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.DocumentId)
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(e => e.DocumentVersion)
            .HasColumnName("document_version")
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.PublishedAt)
            .HasColumnName("published_at")
            .IsRequired();

        builder.HasIndex(e => new
            {
                e.TenantId,
                e.DocumentId,
                e.DocumentVersion
            })
            .HasDatabaseName("ix_published_document_events_document");
    }
}