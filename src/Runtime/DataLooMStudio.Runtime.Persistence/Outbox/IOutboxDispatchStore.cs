using DataLooMStudio.Infrastructure.Outbox;

namespace DataLooMStudio.Runtime.Persistence.Outbox;

public interface IOutboxDispatchStore
{
    Task<IReadOnlyList<OutboxMessage>> ClaimAsync(
        int batchSize,
        Guid leaseId,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(Guid messageId, Guid leaseId, DateTimeOffset publishedAt, CancellationToken cancellationToken);

    Task<bool> FailAsync(
        Guid messageId,
        Guid leaseId,
        DateTimeOffset availableAt,
        string error,
        bool deadLetter,
        CancellationToken cancellationToken);

    Task<long> GetBacklogCountAsync(CancellationToken cancellationToken);
}