namespace DataLooMStudio.Modules.Retention;

public static class DisposalPolicy
{
    public static DisposalPolicyDecision Evaluate(DisposalPolicyInput input)
    {
        if (!input.EligibilityExists || !input.EligibilityIsApproved)
        {
            return DisposalPolicyDecision.Deny(
                DisposalReasonCodes.EligibilityRequired,
                "A current positive deletion eligibility evaluation is required before disposal can proceed.");
        }

        if (!input.EligibilityMatchesEvidence)
        {
            return DisposalPolicyDecision.Deny(
                DisposalReasonCodes.EligibilityStale,
                "Deletion eligibility does not match the current Evidence state.");
        }

        if (input.HasActiveLegalHold)
        {
            return DisposalPolicyDecision.Deny(
                DisposalReasonCodes.ActiveLegalHold,
                "Active Legal Hold blocks disposal execution.");
        }

        if (!input.RetentionPolicyExists)
        {
            return DisposalPolicyDecision.Deny(
                DisposalReasonCodes.RetentionPolicyMissing,
                "Current retention policy is missing.");
        }

        if (!input.CurrentRetentionExpiresAt.HasValue || input.CurrentRetentionExpiresAt.Value > input.Now)
        {
            return DisposalPolicyDecision.Deny(
                DisposalReasonCodes.RetentionNotExpired,
                "Current retention requirements have not expired.");
        }

        if (IsRestrictedLifecycle(input.CurrentLifecycleState))
        {
            return DisposalPolicyDecision.Deny(
                DisposalReasonCodes.LifecycleRestricted,
                "Current Evidence lifecycle state restricts disposal.");
        }

        return DisposalPolicyDecision.Permit(
            "Deletion eligibility is current, retention has expired, and no active Legal Hold exists.");
    }

    private static bool IsRestrictedLifecycle(string lifecycleState)
    {
        return lifecycleState.Equals("Deleted", StringComparison.OrdinalIgnoreCase)
            || lifecycleState.Equals("Quarantined", StringComparison.OrdinalIgnoreCase)
            || lifecycleState.Equals("Superseded", StringComparison.OrdinalIgnoreCase);
    }
}