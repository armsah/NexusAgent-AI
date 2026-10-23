using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration
    : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(document => document.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(document => document.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(document => document.Classification)
            .HasColumnName("classification")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(document => document.OwnerId)
            .HasColumnName("owner_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(document => document.AllowedPrincipalIds)
            .HasColumnName("allowed_principal_ids")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(document => document.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(document => document.ContentVersion)
            .HasColumnName("content_version")
            .IsRequired();

        builder.Property(document => document.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(document => document.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(document => document.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(document => new
        {
            document.TenantId,
            document.Status
        });
    }
}
