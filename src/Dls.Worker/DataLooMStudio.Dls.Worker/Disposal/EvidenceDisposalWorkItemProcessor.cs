using DataLooMStudio.Runtime.Persistence.Retention;
using DataLooMStudio.SharedKernel.RequestContext;

namespace DataLooMStudio.Dls.Worker.Disposal;

public sealed class EvidenceDisposalWorkItemProcessor(IServiceScopeFactory scopeFactory)
{
    public async Task<EvidenceDisposalResult> ExecuteAsync(
        EvidenceDisposalWorkItem workItem,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var contextAccessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();
        contextAccessor.Current = new RequestContext(
            workItem.TenantId,
            workItem.WorkspaceId,
            new(workItem.WorkloadIdentitySubject),
            workItem.CorrelationId);

        var retentionService = scope.ServiceProvider.GetRequiredService<IRetentionGovernanceService>();
        return await retentionService.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(
                workItem.DisposalRecordId,
                workItem.IdempotencyKey),
            cancellationToken);
    }
}