using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.Database;
using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.Observability;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.Runtime.Persistence.Retention;
using DataLooMStudio.Runtime.Persistence.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

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
        services.TryAddSingleton<AuditPersistenceTelemetryInterceptor>();

        services.TryAddSingleton<NpgsqlDataSource>(serviceProvider =>
        {
            var connectionString = configuration.GetConnectionString("DataLooM")
                ?? configuration["DataLooM:PostgreSqlConnectionString"]
                ?? LocalDevelopmentConnectionString;

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            if (configuration.GetValue<bool>("DataLooM:PostgreSqlUseManagedIdentity"))
            {
                var tokenProvider = serviceProvider.GetRequiredService<IDatabaseAccessTokenProvider>();
                dataSourceBuilder.UsePeriodicPasswordProvider(
                    async (_, cancellationToken) => await tokenProvider.GetTokenAsync(cancellationToken),
                    successRefreshInterval: TimeSpan.FromMinutes(45),
                    failureRefreshInterval: TimeSpan.FromSeconds(10));
            }

            return dataSourceBuilder.Build();
        });

        services.AddDbContext<DataLooMDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(
                serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                ConfigureNpgsql);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditPersistenceTelemetryInterceptor>());
        });

        services.TryAddScoped<IOutboxWriter, EfOutboxWriter>();
        services.TryAddSingleton<IOutboxDispatchStore, PostgresOutboxDispatchStore>();
        services.TryAddScoped<IProductAuthorityAuditStore, ProductAuthorityAuditStore>();
        services.TryAddScoped<IProductAuthorityService, ProductAuthorityService>();
        services.TryAddScoped<IEvidenceRegistrationService, EvidenceRegistrationService>();
        services.TryAddScoped<IEvidenceContentService, EvidenceContentService>();
        services.TryAddScoped<IEvidenceQueryService, EvidenceQueryService>();
        services.TryAddScoped<IEvidenceReviewDecisionService, EvidenceReviewDecisionService>();
        services.TryAddScoped<IRetentionGovernanceService, RetentionGovernanceService>();
        services.TryAddScoped<EvidenceRegistrationService>();
        services.TryAddScoped<EvidenceContentService>();
        services.TryAddScoped<EvidenceReviewDecisionService>();
        services.TryAddScoped<RetentionGovernanceService>();
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