using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.SharedKernel.Identity;

using Npgsql;

namespace DataLooMStudio.Runtime.Persistence.Outbox;

public sealed class PostgresOutboxDispatchStore(NpgsqlDataSource dataSource) : IOutboxDispatchStore
{
    public async Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        int batchSize,
        Guid leaseId,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        var messages = new List<OutboxMessage>();
        await using var command = dataSource.CreateCommand(
            "select * from foundation.claim_outbox_messages($1, $2, $3);");
        command.Parameters.AddWithValue(batchSize);
        command.Parameters.AddWithValue(leaseId);
        command.Parameters.AddWithValue(leaseExpiresAt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessage
            {
                Id = reader.GetGuid(0),
                TenantId = new TenantId(reader.GetGuid(1)),
                WorkspaceId = new WorkspaceId(reader.GetGuid(2)),
                OwningModule = reader.GetString(3),
                MessageType = reader.GetString(4),
                PayloadJson = reader.GetString(5),
                CorrelationId = reader.GetString(6),
                OccurredAt = reader.GetFieldValue<DateTimeOffset>(7),
                AvailableAt = reader.GetFieldValue<DateTimeOffset>(8),
                Attempts = reader.GetInt32(9),
                Status = OutboxMessageStatus.Processing,
                LeaseId = leaseId,
                LeaseExpiresAt = leaseExpiresAt
            });
        }

        return messages;
    }

    public Task<bool> CompleteAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken) =>
        ExecuteBooleanAsync(
            "select foundation.complete_outbox_message($1, $2, $3);",
            [messageId, leaseId, publishedAt],
            cancellationToken);

    public Task<bool> FailAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset availableAt,
        string error,
        bool deadLetter,
        CancellationToken cancellationToken) =>
        ExecuteBooleanAsync(
            "select foundation.fail_outbox_message($1, $2, $3, $4, $5);",
            [messageId, leaseId, availableAt, error, deadLetter],
            cancellationToken);

    public async Task<long> GetBacklogCountAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select foundation.outbox_backlog_count();");
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private async Task<bool> ExecuteBooleanAsync(
        string sql,
        object[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}