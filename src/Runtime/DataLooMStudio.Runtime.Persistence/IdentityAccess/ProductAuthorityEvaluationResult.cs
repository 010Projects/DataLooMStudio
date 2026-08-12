using DataLooMStudio.Modules.IdentityAccess;

namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed record ProductAuthorityEvaluationResult(
    bool Succeeded,
    string? Reason,
    string DenialReasonCode,
    string? EffectivePermission,
    string AuthoritySource,
    long AuthorityVersion,
    string PolicyIdentifier,
    int PolicyVersion,
    DateTimeOffset EvaluatedAt)
{
    public static ProductAuthorityEvaluationResult Allowed(
        string effectivePermission,
        string authoritySource,
        long authorityVersion,
        string policyIdentifier,
        int policyVersion,
        DateTimeOffset evaluatedAt)
    {
        return new(
            true,
            null,
            ProductAuthorityDenyReasonCodes.None,
            effectivePermission,
            authoritySource,
            authorityVersion,
            policyIdentifier,
            policyVersion,
            evaluatedAt);
    }

    public static ProductAuthorityEvaluationResult Denied(
        string reasonCode,
        string reason,
        long authorityVersion,
        string policyIdentifier,
        int policyVersion,
        DateTimeOffset evaluatedAt)
    {
        return new(
            false,
            reason,
            reasonCode,
            null,
            ProductAuthoritySources.None,
            authorityVersion,
            policyIdentifier,
            policyVersion,
            evaluatedAt);
    }
}