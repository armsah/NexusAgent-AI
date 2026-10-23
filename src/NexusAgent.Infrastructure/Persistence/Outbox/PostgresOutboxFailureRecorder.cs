using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class PostgresOutboxFailureRecorder(
    NexusAgentDbContext dbContext)
{
    public async Task<bool> RecordFailureAsync(
        Guid messageId,
        Guid claimToken,
        string error,
        CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException(
                "Message ID is required.", nameof(messageId));

        if (claimToken == Guid.Empty)
            throw new ArgumentException(
                "Claim token is required.", nameof(claimToken));

        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException(
                "Failure details are required.", nameof(error));

        if (error.Length > 4000)
            throw new ArgumentOutOfRangeException(
                nameof(error),
                "Failure details must not exceed 4000 characters.");

        var affected = await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE outbox_messages
            SET retry_count = retry_count + 1,
                failed_at = CASE
                    WHEN retry_count + 1 >= 5
                    THEN clock_timestamp()
                    ELSE NULL
                END,
                next_attempt_at = CASE
                    WHEN retry_count + 1 >= 5
                    THEN NULL
                    ELSE clock_timestamp() +
                    (
                        LEAST(
                            3600::double precision,
                            5::double precision *
                            power(
                                2::double precision,
                                LEAST(retry_count, 10)
                            )
                        ) * interval '1 second'
                    )
                    END,
                last_error = @error,
                claim_token = NULL,
                lease_expires_at = NULL
            WHERE id = @message_id
              AND claim_token = @claim_token
              AND lease_expires_at > clock_timestamp()
              AND processed_at IS NULL;
            """,
            new object[]
            {
                new NpgsqlParameter<Guid>("message_id", messageId),
                new NpgsqlParameter<Guid>("claim_token", claimToken),
                new NpgsqlParameter<string>("error", error)
            },
            cancellationToken);

        return affected == 1;
    }
}