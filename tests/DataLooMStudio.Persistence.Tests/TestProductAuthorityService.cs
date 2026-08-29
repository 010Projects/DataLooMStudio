using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;

namespace DataLooMStudio.Persistence.Tests;

internal sealed class TestProductAuthorityService(bool permit = true) : IProductAuthorityService
{
    public List<ProductAuthorityEvaluationRequest> PermissionRequests { get; } = [];

    public Task<ProductAuthorityEvaluationResult> EvaluatePermissionAsync(
        ProductAuthorityEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PermissionRequests.Add(request);
        var result = permit
            ? ProductAuthorityEvaluationResult.Allowed(
                request.PermissionKey,
                ProductAuthoritySources.PermissionAssignment,
                1,
                ProductAuthorityPolicyVersions.PolicyIdentifier,
                ProductAuthorityPolicyVersions.PolicyVersion,
                DateTimeOffset.UtcNow)
            : ProductAuthorityEvaluationResult.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "The test authority policy denied the request.",
                1,
                ProductAuthorityPolicyVersions.PolicyIdentifier,
                ProductAuthorityPolicyVersions.PolicyVersion,
                DateTimeOffset.UtcNow);

        return Task.FromResult(result);
    }

    public Task<ProductAuthorityEvaluationResult> EvaluateSeparationOfDutyAsync(
        ProductSeparationOfDutyRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProductAuthorityEvaluationResult.Allowed(
            "Test.SeparationOfDuty",
            ProductAuthoritySources.PermissionAssignment,
            1,
            ProductAuthorityPolicyVersions.PolicyIdentifier,
            ProductAuthorityPolicyVersions.PolicyVersion,
            DateTimeOffset.UtcNow));
    }
}