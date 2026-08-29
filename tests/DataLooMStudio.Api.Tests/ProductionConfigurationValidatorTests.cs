using DataLooMStudio.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;

namespace DataLooMStudio.Api.Tests;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Production_configuration_rejects_development_defaults()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["ConnectionStrings:DataLooM"] = "Host=localhost;Port=5432;Database=dataloomstudio;Username=dataloom_app",
            ["DataLooM:BlobServiceUri"] = "",
            ["DataLooM:EvidenceContainerName"] = "evidence",
            ["DataLooM:ServiceBusFullyQualifiedNamespace"] = "",
            ["DataLooM:ServiceBusOutboxTopic"] = "dataloomstudio-outbox",
            ["DataLooM:KeyVaultUri"] = "",
            ["DataLooM:EnvironmentName"] = "dls-dev",
            ["DataLooM:EnvironmentKind"] = "dev",
            ["EntraId:Instance"] = "https://login.microsoftonline.com/",
            ["EntraId:TenantId"] = "",
            ["EntraId:ClientId"] = "",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = ""
        });

        var result = ProductionConfigurationValidator.Validate(
            configuration,
            "Production",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("AllowedHosts", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("localhost", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("EntraId:TenantId", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("OTEL_EXPORTER_OTLP_ENDPOINT", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_configuration_accepts_governed_external_values()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "api.dataloomstudio.internal",
            ["ConnectionStrings:DataLooM"] = "Host=dls-prod-pg.postgres.database.azure.com;Port=5432;Database=dataloomstudio;Username=dls_app;Password=externalized;Ssl Mode=Require;Trust Server Certificate=false",
            ["DataLooM:BlobServiceUri"] = "https://dlsprodstorage.blob.core.windows.net",
            ["DataLooM:EvidenceContainerName"] = "evidence",
            ["DataLooM:ServiceBusFullyQualifiedNamespace"] = "dls-prod.servicebus.windows.net",
            ["DataLooM:ServiceBusOutboxTopic"] = "dataloomstudio-outbox",
            ["DataLooM:KeyVaultUri"] = "https://dls-prod-kv.vault.azure.net/",
            ["DataLooM:EnvironmentName"] = "dls-prod",
            ["DataLooM:EnvironmentKind"] = "prod",
            ["DataLooM:AllowedOriginsCsv"] = "https://app.dataloomstudio.internal",
            ["EntraId:Authority"] = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ["EntraId:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://otel.dataloomstudio.internal"
        });

        var result = ProductionConfigurationValidator.Validate(
            configuration,
            "Production",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void Production_worker_configuration_requires_workload_identity()
    {
        var configuration = BuildConfiguration(GovernedCommonConfiguration());

        var result = ProductionConfigurationValidator.Validate(
            configuration,
            "Production",
            "DataLooMStudio.Dls.Worker",
            requireHttpSurface: false,
            requireWorkerIdentity: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("DataLooM:WorkerIdentitySubject", StringComparison.Ordinal));
    }

    [Fact]
    public void Development_configuration_allows_local_defaults()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["ConnectionStrings:DataLooM"] = "Host=localhost;Port=5432;Database=dataloomstudio;Username=dataloom_app"
        });

        var result = ProductionConfigurationValidator.Validate(
            configuration,
            "Development",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.True(result.Succeeded);
    }

    private static Dictionary<string, string?> GovernedCommonConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:DataLooM"] = "Host=dls-prod-pg.postgres.database.azure.com;Port=5432;Database=dataloomstudio;Username=dls_worker;Password=externalized;Ssl Mode=Require;Trust Server Certificate=false",
            ["DataLooM:BlobServiceUri"] = "https://dlsprodstorage.blob.core.windows.net",
            ["DataLooM:EvidenceContainerName"] = "evidence",
            ["DataLooM:ServiceBusFullyQualifiedNamespace"] = "dls-prod.servicebus.windows.net",
            ["DataLooM:ServiceBusOutboxTopic"] = "dataloomstudio-outbox",
            ["DataLooM:KeyVaultUri"] = "https://dls-prod-kv.vault.azure.net/",
            ["DataLooM:EnvironmentName"] = "dls-prod",
            ["DataLooM:EnvironmentKind"] = "prod",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://otel.dataloomstudio.internal"
        };
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}