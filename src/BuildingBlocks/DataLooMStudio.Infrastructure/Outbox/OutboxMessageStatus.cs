namespace DataLooMStudio.Infrastructure.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Published,
    Failed,
    DeadLettered
}