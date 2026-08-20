using Azure.Core;
using Azure.Identity;

using DataLooMStudio.Infrastructure.Clock;
using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Infrastructure.Secrets;
using DataLooMStudio.Infrastructure.SecurityScanning;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataLooMStudio.Infrastructure.DependencyInjection;

public static class DataLooMInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDataLooMInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DataLooMInfrastructureOptions>(configuration.GetSection("DataLooM"));

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<IRequestContextAccessor, RequestContextAccessor>();
        services.TryAddSingleton<TokenCredential, DefaultAzureCredential>();

        services.TryAddSingleton<IOutboxPublisher, ServiceBusOutboxPublisher>();
        services.TryAddSingleton<IEvidenceObjectStore, AzureEvidenceObjectStore>();
        services.TryAddSingleton<IEvidenceDisposalObjectStore, DisabledEvidenceDisposalObjectStore>();
        services.TryAddSingleton<IEvidenceMalwareScanner, UnavailableEvidenceMalwareScanner>();
        services.TryAddSingleton<ISecretResolver, KeyVaultSecretResolver>();

        return services;
    }
}