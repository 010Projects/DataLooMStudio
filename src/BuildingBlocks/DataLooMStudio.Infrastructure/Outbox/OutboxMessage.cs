using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Infrastructure.Outbox;

public sealed class OutboxMessage : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string OwningModule { get; init; } = string.Empty;

    public string MessageType { get; init; } = string.Empty;

    public string PayloadJson { get; init; } = "{}";

    public string CorrelationId { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset AvailableAt { get; init; }

    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    public int Attempts { get; set; }

    public Guid? LeaseId { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
}