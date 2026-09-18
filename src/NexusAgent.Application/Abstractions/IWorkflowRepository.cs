using NexusAgent.Domain.Workflows;

namespace NexusAgent.Application.Abstractions;

public interface IWorkflowRepository
{
    Task AddAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default);

    Task<Workflow?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}