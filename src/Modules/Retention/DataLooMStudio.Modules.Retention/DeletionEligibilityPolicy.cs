namespace DataLooMStudio.Modules.Retention;

public static class DeletionEligibilityPolicy
{
    public static DeletionEligibilityPolicyDecision Evaluate(DeletionEligibilityPolicyInput input)
    {
        if (input.HasActiveLegalHold)
        {
            return DeletionEligibilityPolicyDecision.NotEligible(
                DeletionEligibilityReasonCodes.ActiveLegalHold,
                "Active Legal Hold prevents deletion eligibility.");
        }

        if (!input.RetentionPolicyExists)
        {
            return DeletionEligibilityPolicyDecision.NotEligible(
                DeletionEligibilityReasonCodes.RetentionPolicyMissing,
                "No applicable retention policy was found for the Evidence record.");
        }

        if (IsLifecycleRestricted(input.LifecycleState))
        {
            return DeletionEligibilityPolicyDecision.NotEligible(
                DeletionEligibilityReasonCodes.LifecycleRestricted,
                "Evidence lifecycle state prevents deletion eligibility.");
        }

        if (!input.RetentionExpiresAt.HasValue || input.RetentionExpiresAt.Value > input.Now)
        {
            return DeletionEligibilityPolicyDecision.NotEligible(
                DeletionEligibilityReasonCodes.RetentionNotExpired,
                "Applicable retention period has not expired.");
        }

        return DeletionEligibilityPolicyDecision.Eligible(
            "Retention has expired, no active Legal Hold exists, and policy conditions permit deletion eligibility.");
    }

    private static bool IsLifecycleRestricted(string lifecycleState)
    {
        return lifecycleState.Equals("Deleted", StringComparison.OrdinalIgnoreCase)
            || lifecycleState.Equals("Quarantined", StringComparison.OrdinalIgnoreCase)
            || lifecycleState.Equals("Superseded", StringComparison.OrdinalIgnoreCase);
    }
}