using Microsoft.EntityFrameworkCore;

namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class PendingOutboxMessageReader(
    NexusAgentDbContext dbContext)
{
    public async Task<IReadOnlyList<OutboxMessage>> ReadAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1 || batchSize > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Batch size must be between 1 and 1000.");
        }

        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }
}