namespace DataLooMStudio.Modules.Retention;

public sealed record DisposalPolicyInput(
    bool EligibilityExists,
    bool EligibilityIsApproved,
    bool EligibilityMatchesEvidence,
    bool RetentionPolicyExists,
    DateTimeOffset? CurrentRetentionExpiresAt,
    bool HasActiveLegalHold,
    string CurrentLifecycleState,
    DateTimeOffset Now);