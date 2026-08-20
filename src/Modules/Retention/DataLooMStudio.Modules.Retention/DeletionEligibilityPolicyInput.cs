namespace DataLooMStudio.Modules.Retention;

public sealed record DeletionEligibilityPolicyInput(
    bool RetentionPolicyExists,
    DateTimeOffset? RetentionExpiresAt,
    bool HasActiveLegalHold,
    string LifecycleState,
    DateTimeOffset Now);