using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Infrastructure.Persistence.Repositories;

public sealed class WorkflowRepository(
    NexusAgentDbContext dbContext)
    : IWorkflowRepository
{
    public async Task AddAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Workflows.AddAsync(
            workflow,
            cancellationToken);
    }

    public Task<Workflow?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Workflows
            .Include(workflow => workflow.Steps)
            .SingleOrDefaultAsync(
                workflow => workflow.Id == id,
                cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}