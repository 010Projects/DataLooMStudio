using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.Runtime.Persistence.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataLooMStudio.Runtime.Persistence.DependencyInjection;

public static class DataLooMPersistenceServiceCollectionExtensions
{
    private const string LocalDevelopmentConnectionString =
        "Host=localhost;Port=5432;Database=dataloomstudio;Username=dataloom_app";

    public static IServiceCollection AddDataLooMPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DataLooMInfrastructureOptions>(configuration.GetSection("DataLooM"));

        services.AddDbContext<DataLooMDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DataLooM")
                ?? configuration["DataLooM:PostgreSqlConnectionString"]
                ?? LocalDevelopmentConnectionString;

            options.UseNpgsql(connectionString, ConfigureNpgsql);
        });

        services.TryAddScoped<IOutboxWriter, EfOutboxWriter>();
        services.TryAddScoped<EvidenceRegistrationService>();
        services.TryAddScoped<PostgresRlsSessionContext>();

        return services;
    }

    internal static void ConfigureNpgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 5);
        npgsql.MigrationsHistoryTable("__ef_migrations_history", "foundation");
        npgsql.MigrationsAssembly(typeof(DataLooMDbContext).Assembly.FullName);
    }
}