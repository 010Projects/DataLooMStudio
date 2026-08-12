using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Modules.IdentityAccess;

public sealed record ValidatedIdentityCorrelation(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    string ExternalProvider,
    string ExternalSubject,
    string ProductActorSubject,
    string ActorType,
    DateTimeOffset CorrelatedAt);