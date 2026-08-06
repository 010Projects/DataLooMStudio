using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.Retention;

public sealed class RetentionPolicy : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public string PolicyKey { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int RetainForDays { get; init; }

    public bool LegalHoldOverridesDeletion { get; init; } = true;

    public DateTimeOffset CreatedAt { get; init; }
}