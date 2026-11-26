using System.Text.Json;

namespace NexusAgent.Application.Workflows;

public enum WorkflowEventKind
{
    Started,
    ApprovalRequired,
    Approved,
    Rejected,
    Completed,
    Failed,
    Cancelled
}

public sealed record WorkflowLifecycleEvent(
    Guid EventId,
    string TenantId,
    Guid WorkflowId,
    long Sequence,
    WorkflowEventKind Kind,
    DateTimeOffset OccurredAt,
    long? DurationMillis);

public static class WorkflowEventWireCodec
{
    public static string Serialize(WorkflowLifecycleEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var terminal = value.Kind is
            WorkflowEventKind.Rejected or
            WorkflowEventKind.Completed or
            WorkflowEventKind.Failed or
            WorkflowEventKind.Cancelled;

        if (value.EventId == Guid.Empty ||
            value.WorkflowId == Guid.Empty ||
            value.Sequence < 1 ||
            string.IsNullOrWhiteSpace(value.TenantId) ||
            value.TenantId.Length > 200 ||
            value.TenantId != value.TenantId.Trim() ||
            value.TenantId.Any(char.IsControl) ||
            (terminal && (value.DurationMillis is null or < 0)) ||
            (!terminal && value.DurationMillis.HasValue))
        {
            throw new ArgumentException("Invalid workflow event.", nameof(value));
        }

        var eventType = value.Kind switch
        {
            WorkflowEventKind.Started => "STARTED",
            WorkflowEventKind.ApprovalRequired => "APPROVAL_REQUIRED",
            WorkflowEventKind.Approved => "APPROVED",
            WorkflowEventKind.Rejected => "REJECTED",
            WorkflowEventKind.Completed => "COMPLETED",
            WorkflowEventKind.Failed => "FAILED",
            WorkflowEventKind.Cancelled => "CANCELLED",
            _ => throw new ArgumentException("Unknown workflow event kind.")
        };

        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            eventId = value.EventId,
            tenantId = value.TenantId,
            workflowId = value.WorkflowId,
            sequence = value.Sequence,
            eventType,
            occurredAt = value.OccurredAt.UtcDateTime,
            durationMillis = value.DurationMillis
        });
    }
}