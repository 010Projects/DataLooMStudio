namespace DataLooMStudio.Runtime.Persistence.Retention;

public interface IRetentionGovernanceService
{
    Task<RetentionPolicyResult> DefineRetentionPolicyAsync(
        RetentionPolicyCommand command,
        CancellationToken cancellationToken);

    Task<LegalHoldResult> PlaceLegalHoldAsync(
        PlaceLegalHoldCommand command,
        CancellationToken cancellationToken);
}