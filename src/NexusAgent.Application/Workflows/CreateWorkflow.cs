using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Application.Workflows;

public sealed record CreateWorkflowCommand(
    string RequesterId,
    string TenantId,
    string Request);

public sealed class CreateWorkflowHandler
{
    private readonly IWorkflowRepository _repository;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;

    public CreateWorkflowHandler(
        IWorkflowRepository repository,
        IClock clock,
        IIdGenerator idGenerator)
    {
        _repository = repository;
        _clock = clock;
        _idGenerator = idGenerator;
    }

    public async Task<WorkflowDto> HandleAsync(
        CreateWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workflow = Workflow.Create(
            _idGenerator.NewId(),
            command.RequesterId,
            command.TenantId,
            command.Request,
            _clock.UtcNow);

        await _repository.AddAsync(
            workflow,
            cancellationToken);

        await _repository.SaveChangesAsync(
            cancellationToken);

        return WorkflowDto.FromDomain(workflow);
    }
}