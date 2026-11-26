using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Infrastructure.Persistence.Agents;

public sealed class AgentExecutionStateConfiguration
    : IEntityTypeConfiguration<AgentExecutionState>
{
    public void Configure(EntityTypeBuilder<AgentExecutionState> builder)
    {
        builder.ToTable("agent_executions", table =>
        {
            table.HasCheckConstraint(
                "ck_agent_execution_status",
                "\"Status\" IN ('Pending','Running','Completed','Failed','Cancelled')");

            table.HasCheckConstraint(
                "ck_agent_execution_stage",
                "\"NextStage\" BETWEEN 0 AND 4");

            table.HasCheckConstraint(
                "ck_agent_execution_attempt",
                "\"AttemptCount\" BETWEEN 0 AND 3");

            table.HasCheckConstraint(
                "ck_agent_execution_deadline",
                "\"DeadlineAt\" > \"CreatedAt\"");

            table.HasCheckConstraint(
                "ck_agent_execution_lease",
                "(\"Status\" = 'Running' AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL) OR " +
                "(\"Status\" <> 'Running' AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RequesterId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RequestDigest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PlanVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.FinalAnswer).HasMaxLength(8000);
        builder.Property(x => x.FailureCode).HasMaxLength(128);

        builder.HasIndex(x => new { x.TenantId, x.RequesterId, x.WorkflowId });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresAt });
        builder.HasIndex(x => x.DeadlineAt);

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AgentExecutionCheckpointConfiguration
    : IEntityTypeConfiguration<AgentExecutionCheckpoint>
{
    public void Configure(EntityTypeBuilder<AgentExecutionCheckpoint> builder)
    {
        builder.ToTable("agent_execution_checkpoints", table =>
        {
            table.HasCheckConstraint(
                "ck_agent_checkpoint_stage",
                "\"StageNumber\" BETWEEN 0 AND 3");
        });

        builder.HasKey(x => new { x.ExecutionId, x.StageNumber });
        builder.HasIndex(x => x.InvocationId).IsUnique();

        builder.Property(x => x.AgentName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Output).HasMaxLength(32000).IsRequired();

        builder.HasOne<AgentExecutionState>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AgentApprovalStateConfiguration
    : IEntityTypeConfiguration<AgentApprovalState>
{
    public void Configure(EntityTypeBuilder<AgentApprovalState> builder)
    {
        builder.ToTable("agent_approvals", table =>
        {
            table.HasCheckConstraint(
                "ck_agent_approval_status",
                "\"Status\" BETWEEN 0 AND 4");

            table.HasCheckConstraint(
                "ck_agent_approval_risk",
                "\"Risk\" BETWEEN 2 AND 3");

            table.HasCheckConstraint(
                "ck_agent_approval_expiry",
                "\"ExpiresAt\" > \"CreatedAt\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(x => x.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RequesterId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ToolName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PayloadDigest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ApproverId).HasMaxLength(256);

        builder.HasIndex(x => new { x.TenantId, x.Status, x.ExpiresAt });

        builder.HasOne<Workflow>()
            .WithMany()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.WorkflowId)
            .IsUnique()
            .HasFilter("\"WorkflowId\" IS NOT NULL AND \"Status\" = 0");
    }
}