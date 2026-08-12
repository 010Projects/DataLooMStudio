namespace DataLooMStudio.Modules.IdentityAccess;

public sealed record ProductAuthorityPolicyDecision(
    bool Succeeded,
    string? Reason,
    string DenialReasonCode,
    string? EffectivePermission,
    string AuthoritySource,
    long AuthorityVersion,
    string PolicyIdentifier,
    int PolicyVersion)
{
    public static ProductAuthorityPolicyDecision Allowed(
        string effectivePermission,
        string authoritySource,
        long authorityVersion)
    {
        return new(
            true,
            null,
            ProductAuthorityDenyReasonCodes.None,
            effectivePermission,
            authoritySource,
            authorityVersion,
            ProductAuthorityPolicyVersions.PolicyIdentifier,
            ProductAuthorityPolicyVersions.PolicyVersion);
    }

    public static ProductAuthorityPolicyDecision Denied(
        string reasonCode,
        string reason,
        long authorityVersion = 0)
    {
        return new(
            false,
            reason,
            reasonCode,
            null,
            ProductAuthoritySources.None,
            authorityVersion,
            ProductAuthorityPolicyVersions.PolicyIdentifier,
            ProductAuthorityPolicyVersions.PolicyVersion);
    }
}