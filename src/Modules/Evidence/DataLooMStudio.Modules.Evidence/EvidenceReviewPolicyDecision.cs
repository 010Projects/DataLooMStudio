namespace DataLooMStudio.Modules.Evidence;

public sealed record EvidenceReviewPolicyDecision(bool Succeeded, string? Reason)
{
    public static EvidenceReviewPolicyDecision Allowed() => new(true, null);

    public static EvidenceReviewPolicyDecision Denied(string reason) => new(false, reason);
}