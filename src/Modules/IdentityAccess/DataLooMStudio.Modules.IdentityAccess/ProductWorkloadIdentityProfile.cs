namespace DataLooMStudio.Modules.IdentityAccess;

public sealed record ProductWorkloadIdentityProfile(
    string WorkloadName,
    string ActorSubject,
    string Purpose,
    string[] AllowedPermissions,
    string[] ProhibitedPermissions,
    bool MayImpersonateHumanApprover,
    string AuditIdentity);