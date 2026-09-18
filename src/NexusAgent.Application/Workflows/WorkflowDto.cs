using NexusAgent.Domain.Workflows;

namespace NexusAgent.Application.Workflows;

public sealed record WorkflowStepDto(
    Guid Id,
    string Name,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Outcome);

public sealed record WorkflowDto(
    Guid Id,
    string RequesterId,
    string TenantId,
    string Request,
    WorkflowStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? FailureReason,
    IReadOnlyCollection<WorkflowStepDto> Steps)
{
    public static WorkflowDto FromDomain(Workflow workflow)
    {
        return new WorkflowDto(
            workflow.Id,
            workflow.RequesterId,
            workflow.TenantId,
            workflow.Request,
            workflow.Status,
            workflow.CreatedAtUtc,
            workflow.UpdatedAtUtc,
            workflow.FailureReason,
            workflow.Steps
                .Select(step => new WorkflowStepDto(
                    step.Id,
                    step.Name,
                    step.StartedAtUtc,
                    step.CompletedAtUtc,
                    step.Outcome))
                .ToArray());
    }
}