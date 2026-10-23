using NexusAgent.Domain.Workflows;

namespace NexusAgent.Application.Workflows;

public sealed record WorkflowAccess(
    string TenantId,
    string RequesterId)
{
    public bool CanAccess(Workflow workflow)
    {
        return !string.IsNullOrWhiteSpace(TenantId)
            && !string.IsNullOrWhiteSpace(RequesterId)
            && string.Equals(
                workflow.TenantId,
                TenantId,
                StringComparison.Ordinal)
            && string.Equals(
                workflow.RequesterId,
                RequesterId,
                StringComparison.Ordinal);
    }
}