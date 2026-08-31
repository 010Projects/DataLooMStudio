using System.Diagnostics.Metrics;

using DataLooMStudio.Modules.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DataLooMStudio.Runtime.Persistence.Observability;

public sealed class AuditPersistenceTelemetryInterceptor : SaveChangesInterceptor
{
    private static readonly Meter Meter = new("DataLooMStudio.Persistence");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("dls.audit.persistence.failures");
    private static readonly Counter<long> DependencyFailures = Meter.CreateCounter<long>("dls.dependencies.failures");

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        RecordIfAuditWasPending(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordIfAuditWasPending(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static void RecordIfAuditWasPending(DbContext? context)
    {
        DependencyFailures.Add(
            1,
            new KeyValuePair<string, object?>("dependency", "postgresql"),
            new KeyValuePair<string, object?>("operation", "save_changes"));

        if (context?.ChangeTracker.Entries<AuditEntry>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified) == true)
        {
            Failures.Add(1);
        }
    }
}