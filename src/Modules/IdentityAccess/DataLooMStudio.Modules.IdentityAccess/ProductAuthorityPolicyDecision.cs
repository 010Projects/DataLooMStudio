namespace DataLooMStudio.Modules.IdentityAccess;

public sealed record ProductAuthorityPolicyDecision(bool Succeeded, string? Reason)
{
    public static ProductAuthorityPolicyDecision Allowed() => new(true, null);

    public static ProductAuthorityPolicyDecision Denied(string reason) => new(false, reason);
}