using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class WorkflowConfiguration
    : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("workflows");

        builder.HasKey(workflow => workflow.Id);

        builder.Property(workflow => workflow.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(workflow => workflow.RequesterId)
            .HasColumnName("requester_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(workflow => workflow.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(workflow => workflow.Request)
            .HasColumnName("request")
            .HasMaxLength(16000)
            .IsRequired();

        builder.Property(workflow => workflow.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(workflow => workflow.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(workflow => workflow.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(workflow => workflow.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(4000);

        builder.HasMany(workflow => workflow.Steps)
            .WithOne()
            .HasForeignKey("WorkflowId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(workflow => workflow.Steps)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}