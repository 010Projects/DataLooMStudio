namespace DataLooMStudio.Modules.Retention;

public sealed record DisposalPolicyDecision(
    bool IsPermitted,
    string ReasonCode,
    string Reason)
{
    public static DisposalPolicyDecision Permit(string reason)
    {
        return new(true, DisposalReasonCodes.Permitted, reason);
    }

    public static DisposalPolicyDecision Deny(string reasonCode, string reason)
    {
        return new(false, reasonCode, reason);
    }
}