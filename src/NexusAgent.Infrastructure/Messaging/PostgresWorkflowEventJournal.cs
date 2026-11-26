using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Workflows;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Infrastructure.Messaging;

public sealed class PostgresWorkflowEventJournal(
    NexusAgentDbContext dbContext)
{
    public async Task AppendAsync(
        WorkflowLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);

        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Workflow event persistence requires an active transaction.");

        var payload = WorkflowEventWireCodec.Serialize(lifecycleEvent);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO kafka_workflow_events
                (event_id, tenant_id, workflow_id, sequence, payload)
            VALUES
                ({lifecycleEvent.EventId},
                 {lifecycleEvent.TenantId},
                 {lifecycleEvent.WorkflowId},
                 {lifecycleEvent.Sequence},
                 CAST({payload} AS jsonb))
            """,
            cancellationToken);
    }
}