using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class DocumentVersionConfiguration
    : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");

        builder.HasKey(version => version.Id);

        builder.Property(version => version.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(version => version.DocumentId)
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(version => version.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(version => version.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(version => version.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(version => version.Classification)
            .HasColumnName("classification")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(version => version.OwnerId)
            .HasColumnName("owner_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(version => version.AllowedPrincipalIds)
            .HasColumnName("allowed_principal_ids")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(version => version.IsDeleted)
            .HasColumnName("is_deleted")
            .IsRequired();

        builder.Property(version => version.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(version => new
        {
            version.DocumentId,
            version.Version
        }).IsUnique();

        builder.HasIndex(version => version.TenantId);

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(version => version.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
