using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Modules.Lineage;

public sealed class LineageRelationship : IWorkspaceScoped
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public TenantId TenantId { get; init; }

    public WorkspaceId WorkspaceId { get; init; }

    public LineageId SourceLineageId { get; init; }

    public LineageId TargetLineageId { get; init; }

    public string RelationshipType { get; init; } = string.Empty;

    public string ActorOrProcess { get; init; } = "system";

    public string CorrelationId { get; init; } = string.Empty;

    public string CausationId { get; init; } = string.Empty;

    public int Version { get; init; } = 1;

    public DateTimeOffset ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }
}