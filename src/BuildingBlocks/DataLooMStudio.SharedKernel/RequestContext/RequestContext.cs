using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.SharedKernel.RequestContext;

public sealed record RequestContext(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    PrincipalSubject PrincipalSubject,
    string CorrelationId);