using System.Security.Claims;

namespace DataLooMStudio.Infrastructure.Configuration;

public static class EntraTokenIdentityValidator
{
    public static bool HasCanonicalActorClaims(ClaimsPrincipal principal, string? configuredTenantId)
    {
        var tenantClaim = principal.FindFirst("tid")?.Value;
        var actorClaim = principal.FindFirst("oid")?.Value;

        return Guid.TryParse(configuredTenantId, out var expectedTenantId)
            && expectedTenantId != Guid.Empty
            && Guid.TryParse(tenantClaim, out var tokenTenantId)
            && tokenTenantId == expectedTenantId
            && Guid.TryParse(actorClaim, out var actorId)
            && actorId != Guid.Empty;
    }
}