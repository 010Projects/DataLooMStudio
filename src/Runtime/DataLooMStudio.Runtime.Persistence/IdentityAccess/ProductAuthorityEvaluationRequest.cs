using DataLooMStudio.Modules.IdentityAccess;

namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed record ProductAuthorityEvaluationRequest(
    string ActorSubject,
    string PermissionKey,
    string ResourceType,
    string ResourceId,
    string ActorType = ProductActorTypes.Human,
    string? ProductCapability = null,
    string? Action = null,
    string? ProductRole = null,
    string? Classification = null,
    string? LifecycleState = null,
    long? CapturedAuthorityVersion = null,
    DateTimeOffset? CapturedAt = null,
    TimeSpan? MaximumAuthorityAge = null,
    bool RequireEntitlement = false,
    bool ExternalStrongAuthenticationSatisfied = false,
    bool RequireAuthenticatedActorMatch = true,
    string? CorrelationId = null,
    string? CausationId = null);

public sealed record ProductSeparationOfDutyRequest(
    string ActorSubject,
    string ConflictingActorSubject,
    string DutyConflict);