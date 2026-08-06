namespace DataLooMStudio.Infrastructure.Outbox;

public interface IOutboxWriter
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken);
}