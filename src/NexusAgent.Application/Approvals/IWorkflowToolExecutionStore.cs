namespace NexusAgent.Application.Approvals;

public interface IWorkflowToolExecutionStore
{
    Task<bool> FinishAsync(
        Guid approvalId,
        Guid workflowId,
        ApprovalAction action,
        string outcome,
        CancellationToken cancellationToken);
}
