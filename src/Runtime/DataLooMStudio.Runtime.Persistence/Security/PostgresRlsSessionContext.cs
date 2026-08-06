using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.Security;

public sealed class PostgresRlsSessionContext(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor)
{
    public async Task SetTransactionLocalContextAsync(CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for database access.");

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("RLS context must be set inside an active database transaction.");
        }

        var tenantId = context.TenantId.Value.ToString("D");
        var workspaceId = context.WorkspaceId.Value.ToString("D");
        var actor = context.PrincipalSubject.ToString();
        var correlationId = context.CorrelationId;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             select
                 set_config('app.tenant_id', {tenantId}, true),
                 set_config('app.workspace_id', {workspaceId}, true),
                 set_config('app.actor', {actor}, true),
                 set_config('app.correlation_id', {correlationId}, true)
             """,
            cancellationToken);
    }
}