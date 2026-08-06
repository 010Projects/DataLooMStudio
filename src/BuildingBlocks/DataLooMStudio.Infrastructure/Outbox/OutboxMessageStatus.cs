namespace DataLooMStudio.Infrastructure.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Published,
    Failed,
    DeadLettered
}