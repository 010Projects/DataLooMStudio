namespace DataLooMStudio.Modules.IdentityAccess;

public sealed record AuthenticatedExternalPrincipal(
    string Provider,
    string ExternalSubject,
    string TenantIssuer,
    string AuthenticationMethodReference,
    DateTimeOffset AuthenticatedAt);