using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed record ProductAuthorityAuditRecord(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    string ActorSubject,
    string AuthorityContext,
    string Action,
    string TargetType,
    string TargetId,
    string CorrelationId,
    string CausationId,
    string Outcome,
    string MetadataJson,
    DateTimeOffset OccurredAt);