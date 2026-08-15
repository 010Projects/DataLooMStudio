namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductActorCorrelationPolicy
{
    public static ProductAuthorityPolicyDecision CanCorrelate(
        AuthenticatedExternalPrincipal principal,
        ValidatedIdentityCorrelation correlation)
    {
        if (string.IsNullOrWhiteSpace(principal.Provider)
            || string.IsNullOrWhiteSpace(principal.ExternalSubject)
            || string.IsNullOrWhiteSpace(principal.TenantIssuer))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "External principal is incomplete.");
        }

        if (!principal.Provider.Equals(correlation.ExternalProvider, StringComparison.Ordinal)
            || !principal.ExternalSubject.Equals(correlation.ExternalSubject, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "External principal does not match the validated Product actor correlation.");
        }

        if (!ProductActorTypes.IsSupported(correlation.ActorType))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Product actor type is not supported.");
        }

        return ProductAuthorityPolicyDecision.Allowed(
            correlation.ProductActorSubject,
            ProductAuthoritySources.IdentityCorrelation,
            1);
    }
}