namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed record ProductAuthorityEvaluationRequest(
    string ActorSubject,
    string PermissionKey,
    string ResourceType,
    string ResourceId);

public sealed record ProductSeparationOfDutyRequest(
    string ActorSubject,
    string ConflictingActorSubject,
    string DutyConflict);