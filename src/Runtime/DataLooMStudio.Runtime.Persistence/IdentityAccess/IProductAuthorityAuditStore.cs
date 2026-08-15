namespace DataLooMStudio.Runtime.Persistence.IdentityAccess;

public interface IProductAuthorityAuditStore
{
    void AddTransactionalAudit(
        DataLooMDbContext dbContext,
        ProductAuthorityAuditRecord auditRecord);

    Task PersistDurableDenialAsync(
        ProductAuthorityAuditRecord auditRecord,
        CancellationToken cancellationToken);
}