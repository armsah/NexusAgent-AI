using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Infrastructure.Persistence.Outbox;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(message => message.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.DocumentId)
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(message => message.DocumentVersion)
            .HasColumnName("document_version")
            .IsRequired();

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(message => message.ProcessedAt)
            .HasColumnName("processed_at");

        builder.Property(message => message.ClaimToken)
            .HasColumnName("claim_token");

        builder.Property(message => message.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");
        builder.Property(message => message.FailedAt).HasColumnName("failed_at");
        builder.Property(message => message.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(message => message.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(message => message.LastError)
            .HasColumnName("last_error");

        builder.HasIndex(message => new
            {
                message.ProcessedAt,
                message.CreatedAt
            })
            .HasDatabaseName("ix_outbox_messages_pending");

        builder.HasIndex(message => new
            {
                message.TenantId,
                message.DocumentId,
                message.DocumentVersion
            })
            .HasDatabaseName("ix_outbox_messages_document_history");
    }
}