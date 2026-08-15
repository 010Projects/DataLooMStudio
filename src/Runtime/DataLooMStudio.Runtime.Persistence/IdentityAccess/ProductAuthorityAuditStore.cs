using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public sealed class ProductAuthorityAuditStore(
    DbContextOptions<DataLooMDbContext> dbContextOptions) : IProductAuthorityAuditStore
{
    public void AddTransactionalAudit(
        DataLooMDbContext dbContext,
        ProductAuthorityAuditRecord auditRecord)
    {
        dbContext.AuditEntries.Add(ToAuditEntry(auditRecord));
    }

    public async Task PersistDurableDenialAsync(
        ProductAuthorityAuditRecord auditRecord,
        CancellationToken cancellationToken)
    {
        var requestContextAccessor = new RequestContextAccessor
        {
            Current = new RequestContext(
                auditRecord.TenantId,
                auditRecord.WorkspaceId,
                new PrincipalSubject(auditRecord.ActorSubject),
                auditRecord.CorrelationId)
        };

        await using var auditDbContext = new DataLooMDbContext(dbContextOptions, requestContextAccessor);
        var rlsSessionContext = new PostgresRlsSessionContext(auditDbContext, requestContextAccessor);
        var executionStrategy = auditDbContext.Database.CreateExecutionStrategy();

        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await auditDbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            auditDbContext.AuditEntries.Add(ToAuditEntry(auditRecord));
            await auditDbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static AuditEntry ToAuditEntry(ProductAuthorityAuditRecord auditRecord)
    {
        return new AuditEntry
        {
            TenantId = auditRecord.TenantId,
            WorkspaceId = auditRecord.WorkspaceId,
            ActorSubject = auditRecord.ActorSubject,
            AuthorityContext = auditRecord.AuthorityContext,
            Action = auditRecord.Action,
            TargetType = auditRecord.TargetType,
            TargetId = auditRecord.TargetId,
            CorrelationId = auditRecord.CorrelationId,
            CausationId = auditRecord.CausationId,
            Outcome = auditRecord.Outcome,
            MetadataJson = auditRecord.MetadataJson,
            OccurredAt = auditRecord.OccurredAt
        };
    }
}