using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceQueryService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    PostgresRlsSessionContext rlsSessionContext,
    IProductAuthorityService productAuthorityService) : IEvidenceQueryService
{
    public async Task<EvidenceSummary> GetAsync(
        EvidenceId evidenceId,
        CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required.");
        var actor = context.PrincipalSubject.ToString();
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var evidence = await dbContext.EvidenceRecords
                .SingleOrDefaultAsync(item => item.Id == evidenceId, cancellationToken)
                ?? throw new EvidenceQueryForbiddenException("Evidence is not available in the active workspace.");
            var version = await dbContext.EvidenceVersions
                .SingleAsync(item => item.Id == evidence.CurrentVersionId, cancellationToken);

            var permission = evidence.Classification.Equals("Restricted", StringComparison.OrdinalIgnoreCase)
                ? ProductAuthorityPermissions.ReadRestrictedEvidence
                : ProductAuthorityPermissions.ReadEvidence;
            var authority = await productAuthorityService.EvaluatePermissionAsync(
                new ProductAuthorityEvaluationRequest(
                    actor,
                    permission,
                    ProductAuthorityResourceTypes.Evidence,
                    evidence.Id.ToString(),
                    ProductCapability: ProductAuthorityCapabilities.EvidenceContent,
                    Action: ProductAuthorityActions.EvidenceRead,
                    Classification: evidence.Classification,
                    LifecycleState: evidence.LifecycleState),
                cancellationToken);

            if (!authority.Succeeded)
            {
                throw new EvidenceQueryForbiddenException("Product authority denied Evidence access.");
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new EvidenceSummary(
                evidence.Id,
                version.Id,
                evidence.EvidenceType,
                evidence.Classification,
                evidence.LifecycleState,
                evidence.VerificationStatus.ToString(),
                version.OriginalFileName,
                version.MediaType,
                evidence.ContentLength,
                evidence.Sha256Hash,
                evidence.CapturedAt,
                evidence.LineageId.ToString());
        });
    }
}