namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public interface IProductAuthorityService
{
    Task<ProductAuthorityEvaluationResult> EvaluatePermissionAsync(
        ProductAuthorityEvaluationRequest request,
        CancellationToken cancellationToken);

    Task<ProductAuthorityEvaluationResult> EvaluateSeparationOfDutyAsync(
        ProductSeparationOfDutyRequest request,
        CancellationToken cancellationToken);
}