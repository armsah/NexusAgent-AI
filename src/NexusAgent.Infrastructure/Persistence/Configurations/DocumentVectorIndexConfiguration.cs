using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence.Search;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class DocumentVectorIndexConfiguration
    : IEntityTypeConfiguration<DocumentVectorIndex>
{
    public void Configure(EntityTypeBuilder<DocumentVectorIndex> builder)
    {
        builder.ToTable("document_vector_indexes");

        builder.HasKey(x => x.DocumentId);

        builder.Property(x => x.DocumentId)
            .HasColumnName("document_id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ContentVersion)
            .HasColumnName("content_version");

        builder.Property(x => x.EmbeddingModel)
            .HasColumnName("embedding_model")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Embedding)
            .HasColumnName("embedding")
            .HasColumnType("vector(1536)")
            .IsRequired();

        builder.Property(x => x.IndexedAt)
            .HasColumnName("indexed_at");

        builder.HasIndex(x => x.TenantId);

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}