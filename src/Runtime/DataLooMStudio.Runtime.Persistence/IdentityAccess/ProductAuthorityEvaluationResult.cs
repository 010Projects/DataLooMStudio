namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed record ProductAuthorityEvaluationResult(bool Succeeded, string? Reason)
{
    public static ProductAuthorityEvaluationResult Allowed() => new(true, null);

    public static ProductAuthorityEvaluationResult Denied(string reason) => new(false, reason);
}