using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataLooMStudio.Runtime.Persistence;

public sealed class DesignTimeDataLooMDbContextFactory : IDesignTimeDbContextFactory<DataLooMDbContext>
{
    public DataLooMDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DataLooMDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("DATALOOM_MIGRATION_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=dataloomstudio;Username=dataloom_migrator";

        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.EnableRetryOnFailure(maxRetryCount: 5);
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "foundation");
            npgsql.MigrationsAssembly(typeof(DataLooMDbContext).Assembly.FullName);
        });

        return new DataLooMDbContext(optionsBuilder.Options);
    }
}