using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Infrastructure.Persistence.Search;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class ProcessedDocumentIndexEventConfiguration
    : IEntityTypeConfiguration<ProcessedDocumentIndexEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedDocumentIndexEvent> builder)
    {
        builder.ToTable("processed_document_index_events");

        builder.HasKey(x => x.EventId);

        builder.Property(x => x.EventId)
            .HasColumnName("event_id")
            .ValueGeneratedNever();

        builder.Property(x => x.ProcessedAt)
            .HasColumnName("processed_at")
            .IsRequired();
    }
}