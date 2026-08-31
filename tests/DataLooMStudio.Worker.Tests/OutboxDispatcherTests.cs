using DataLooMStudio.Dls.Worker;
using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

using Microsoft.Extensions.Logging.Abstractions;

namespace DataLooMStudio.Worker.Tests;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task Successful_publish_completes_the_same_lease()
    {
        var message = BuildMessage(attempts: 1);
        var store = new RecordingStore(message);
        var publisher = new RecordingPublisher();
        var dispatcher = new OutboxDispatcher(store, publisher, new FixedClock(), NullLogger<OutboxDispatcher>.Instance);

        var processed = await dispatcher.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(message.Id, publisher.Published.Single().Id);
        Assert.Equal(message.Id, store.CompletedMessageId);
        Assert.Equal(store.ClaimedLeaseId, store.CompletedLeaseId);
        Assert.Null(store.FailedMessageId);
    }

    [Fact]
    public async Task Tenth_failure_is_dead_lettered_without_losing_scope()
    {
        var message = BuildMessage(attempts: 10);
        var store = new RecordingStore(message);
        var dispatcher = new OutboxDispatcher(
            store,
            new ThrowingPublisher(),
            new FixedClock(),
            NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(message.Id, store.FailedMessageId);
        Assert.Equal(store.ClaimedLeaseId, store.FailedLeaseId);
        Assert.True(store.DeadLettered);
        Assert.Null(store.CompletedMessageId);
        Assert.Equal(message.TenantId, store.Claimed.Single().TenantId);
        Assert.Equal(message.WorkspaceId, store.Claimed.Single().WorkspaceId);
    }

    private static OutboxMessage BuildMessage(int attempts) => new()
    {
        TenantId = TenantId.New(),
        WorkspaceId = WorkspaceId.New(),
        OwningModule = "Evidence",
        MessageType = "EvidenceAvailable",
        PayloadJson = "{}",
        CorrelationId = "test-correlation",
        OccurredAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
        AvailableAt = DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
        Attempts = attempts,
        Status = OutboxMessageStatus.Processing
    };

    private sealed class RecordingStore(OutboxMessage message) : IOutboxDispatchStore
    {
        public IReadOnlyList<OutboxMessage> Claimed { get; private set; } = [];
        public Guid ClaimedLeaseId { get; private set; }
        public Guid? CompletedMessageId { get; private set; }
        public Guid? CompletedLeaseId { get; private set; }
        public Guid? FailedMessageId { get; private set; }
        public Guid? FailedLeaseId { get; private set; }
        public bool DeadLettered { get; private set; }

        public Task<IReadOnlyList<OutboxMessage>> ClaimAsync(int batchSize, Guid leaseId, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken)
        {
            ClaimedLeaseId = leaseId;
            Claimed = [message];
            return Task.FromResult(Claimed);
        }

        public Task<bool> CompleteAsync(Guid messageId, Guid leaseId, DateTimeOffset publishedAt, CancellationToken cancellationToken)
        {
            CompletedMessageId = messageId;
            CompletedLeaseId = leaseId;
            return Task.FromResult(true);
        }

        public Task<bool> FailAsync(Guid messageId, Guid leaseId, DateTimeOffset availableAt, string error, bool deadLetter, CancellationToken cancellationToken)
        {
            FailedMessageId = messageId;
            FailedLeaseId = leaseId;
            DeadLettered = deadLetter;
            return Task.FromResult(true);
        }

        public Task<long> GetBacklogCountAsync(CancellationToken cancellationToken) => Task.FromResult(1L);
    }

    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public List<OutboxMessage> Published { get; } = [];
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IOutboxPublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic publish failure");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-29T12:00:00Z");
    }
}