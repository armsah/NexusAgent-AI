using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Infrastructure.Persistence.Configurations;

public sealed class WorkflowStepConfiguration
    : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.ToTable("workflow_steps");

        builder.HasKey(step => step.Id);

        builder.Property(step => step.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<Guid>("WorkflowId")
            .HasColumnName("workflow_id")
            .IsRequired();

        builder.Property(step => step.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(step => step.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .IsRequired();

        builder.Property(step => step.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(step => step.Outcome)
            .HasColumnName("outcome")
            .HasMaxLength(4000);

        builder.HasIndex("WorkflowId");
    }
}