using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class PostgresOutboxAcknowledger(
    NexusAgentDbContext dbContext)
{
    public async Task<bool> AcknowledgeAsync(
        Guid messageId,
        Guid claimToken,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException(
                "Message ID is required.", nameof(messageId));

        if (claimToken == Guid.Empty)
            throw new ArgumentException(
                "Claim token is required.", nameof(claimToken));

        var affected = await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE outbox_messages
            SET processed_at = clock_timestamp(),
                claim_token = NULL,
                lease_expires_at = NULL,
                last_error = NULL
            WHERE id = @message_id
              AND claim_token = @claim_token
              AND lease_expires_at > clock_timestamp()
              AND processed_at IS NULL;
            """,
            new object[]
            {
                new NpgsqlParameter<Guid>("message_id", messageId),
                new NpgsqlParameter<Guid>("claim_token", claimToken)
            },
            cancellationToken);

        return affected == 1;
    }
}