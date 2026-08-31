using DataLooMStudio.Runtime.Persistence;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Dls.Migrate;

public sealed class MigrationRunner(DataLooMDbContext dbContext)
{
    public async Task<MigrationRunResult> ApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingMigrationCount = pendingMigrations.Count();

            await dbContext.Database.MigrateAsync(cancellationToken);
            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);

            return MigrationRunResult.Success(pendingMigrationCount, appliedMigrations.LastOrDefault());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return MigrationRunResult.Failure(exception);
        }
    }
}