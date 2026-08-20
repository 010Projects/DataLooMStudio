namespace DataLooMStudio.Modules.Retention;

public sealed record DeletionEligibilityPolicyDecision(
    bool IsEligible,
    string ReasonCode,
    string Reason)
{
    public static DeletionEligibilityPolicyDecision Eligible(string reason)
    {
        return new(true, DeletionEligibilityReasonCodes.Eligible, reason);
    }

    public static DeletionEligibilityPolicyDecision NotEligible(string reasonCode, string reason)
    {
        return new(false, reasonCode, reason);
    }
}