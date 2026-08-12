using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.IdentityAccess;

public sealed class ProductActor : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string State { get; set; } = ProductActorStates.Active;

    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}