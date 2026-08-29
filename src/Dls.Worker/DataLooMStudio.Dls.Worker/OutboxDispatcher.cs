using System.Diagnostics;
using System.Diagnostics.Metrics;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.SharedKernel.Abstractions;

namespace DataLooMStudio.Dls.Worker;

public sealed class OutboxDispatcher(
    IOutboxDispatchStore store,
    IOutboxPublisher publisher,
    IClock clock,
    ILogger<OutboxDispatcher> logger)
{
    private const int MaximumAttempts = 10;
    private static readonly ActivitySource ActivitySource = new("DataLooMStudio.Worker");
    private static readonly Meter Meter = new("DataLooMStudio.Worker");
    private static readonly Counter<long> Published = Meter.CreateCounter<long>("dls.outbox.published");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("dls.outbox.failed");
    private static readonly Histogram<long> Backlog = Meter.CreateHistogram<long>("dls.outbox.backlog");

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        var now = clock.UtcNow;
        var messages = await store.ClaimAsync(20, leaseId, now.AddMinutes(2), cancellationToken);

        foreach (var message in messages)
        {
            using var activity = ActivitySource.StartActivity("outbox.publish", ActivityKind.Producer);
            activity?.SetTag("messaging.system", "servicebus");
            activity?.SetTag("messaging.operation.name", "publish");
            activity?.SetTag("messaging.message.id", message.Id.ToString("D"));
            activity?.SetTag("dls.correlation.id", message.CorrelationId);
            activity?.SetTag("dls.tenant.id", message.TenantId.ToString());
            activity?.SetTag("dls.workspace.id", message.WorkspaceId.ToString());
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = message.CorrelationId,
                ["TenantId"] = message.TenantId.ToString(),
                ["WorkspaceId"] = message.WorkspaceId.ToString(),
                ["OutboxMessageId"] = message.Id
            });

            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                if (!await store.CompleteAsync(message.Id, leaseId, clock.UtcNow, cancellationToken))
                {
                    throw new InvalidOperationException("Outbox completion lease was lost.");
                }

                Published.Add(1, new KeyValuePair<string, object?>("message.type", message.MessageType));
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var deadLetter = message.Attempts >= MaximumAttempts;
                var retryDelay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(message.Attempts, 8))));
                await store.FailAsync(
                    message.Id,
                    leaseId,
                    clock.UtcNow.Add(retryDelay),
                    exception.GetType().Name,
                    deadLetter,
                    cancellationToken);
                Failed.Add(1,
                    new KeyValuePair<string, object?>("message.type", message.MessageType),
                    new KeyValuePair<string, object?>("dead_letter", deadLetter));
                logger.LogError(
                    exception,
                    "Outbox message {MessageId} failed on attempt {Attempt}; dead-letter={DeadLetter}",
                    message.Id,
                    message.Attempts,
                    deadLetter);
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            }
        }

        Backlog.Record(await store.GetBacklogCountAsync(cancellationToken));
        return messages.Count;
    }
}