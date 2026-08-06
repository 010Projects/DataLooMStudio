namespace DataLooMStudio.Infrastructure.Outbox;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}