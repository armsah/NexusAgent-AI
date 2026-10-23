using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Infrastructure.Persistence.Search;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class DocumentSearchIndexConfiguration
    : IEntityTypeConfiguration<DocumentSearchIndex>
{
    public void Configure(EntityTypeBuilder<DocumentSearchIndex> builder)
    {
        builder.ToTable("document_search_indexes");

        builder.HasKey(x => x.DocumentId);

        builder.Property(x => x.DocumentId)
            .HasColumnName("document_id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ContentVersion)
            .HasColumnName("content_version");

        builder.Property(x => x.Content)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.IndexedAt)
            .HasColumnName("indexed_at");

        builder.HasIndex(x => x.TenantId);

        builder.HasIndex(x => x.Content)
            .HasMethod("GIN")
            .IsTsVectorExpressionIndex("english");

        builder.HasOne<NexusAgent.Domain.Documents.KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}