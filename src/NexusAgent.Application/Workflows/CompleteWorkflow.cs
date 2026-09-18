using NexusAgent.Application.Abstractions;

namespace NexusAgent.Application.Workflows;

public sealed class CompleteWorkflowHandler(
    IWorkflowRepository repository,
    IClock clock)
{
    public async Task<WorkflowDto?> HandleAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await repository.GetByIdAsync(
            workflowId,
            cancellationToken);

        if (workflow is null)
        {
            return null;
        }

        workflow.Complete(clock.UtcNow);

        await repository.SaveChangesAsync(cancellationToken);

        return WorkflowDto.FromDomain(workflow);
    }
}