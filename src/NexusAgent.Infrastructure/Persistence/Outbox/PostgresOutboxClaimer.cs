using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class PostgresOutboxClaimer(
    NexusAgentDbContext dbContext)
{
    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        if (leaseDuration <= TimeSpan.Zero ||
            leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = """
                WITH candidates AS (
                    SELECT id
                    FROM outbox_messages
                    WHERE processed_at IS NULL
                      AND failed_at IS NULL
                      AND (
                          next_attempt_at IS NULL
                          OR next_attempt_at <= clock_timestamp()
                      )
                      AND (
                          claim_token IS NULL
                          OR lease_expires_at <= clock_timestamp()
                      )
                    ORDER BY created_at, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT @batch_size
                )
                UPDATE outbox_messages AS message
                SET claim_token = gen_random_uuid(),
                    lease_expires_at =
                        clock_timestamp() + @lease_duration
                FROM candidates
                WHERE message.id = candidates.id
                RETURNING
                    message.id,
                    message.claim_token,
                    message.tenant_id,
                    message.event_type,
                    message.document_id,
                    message.document_version,
                    message.payload::text,
                    message.lease_expires_at;
                """;

            command.Parameters.Add(
                new NpgsqlParameter<int>("batch_size", batchSize));

            command.Parameters.Add(
                new NpgsqlParameter<TimeSpan>(
                    "lease_duration",
                    leaseDuration)
                {
                    NpgsqlDbType =
                        NpgsqlTypes.NpgsqlDbType.Interval
                });

            var claimed = new List<ClaimedOutboxMessage>();

            await using (var reader =
                await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    claimed.Add(new ClaimedOutboxMessage(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetGuid(4),
                        reader.GetInt32(5),
                        reader.GetString(6),
                        reader.GetFieldValue<DateTimeOffset>(7)));
                }
            }

            await transaction.CommitAsync(cancellationToken);

            return claimed;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}