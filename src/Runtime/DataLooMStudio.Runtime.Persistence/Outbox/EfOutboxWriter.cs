using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Runtime.Persistence;

namespace DataLooMStudio.Runtime.Persistence.Outbox;

public sealed class EfOutboxWriter(DataLooMDbContext dbContext) : IOutboxWriter
{
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);
    }
}