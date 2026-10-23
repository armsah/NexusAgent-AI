using NexusAgent.Application.Abstractions;

namespace NexusAgent.Application.Workflows;

public sealed class GetWorkflowHandler
{
    private readonly IWorkflowRepository _repository;

    public GetWorkflowHandler(
        IWorkflowRepository repository)
    {
        _repository = repository;
    }

    public async Task<WorkflowDto?> HandleAsync(
        Guid workflowId,
        WorkflowAccess access,
        CancellationToken cancellationToken = default)
    {
        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "Workflow ID cannot be empty.",
                nameof(workflowId));
        }

        var workflow = await _repository.GetByIdAsync(
            workflowId,
            cancellationToken);

        return workflow is null || !access.CanAccess(workflow)
            ? null
            : WorkflowDto.FromDomain(workflow);
    }
}