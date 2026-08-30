using System.Security.Claims;

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
            ["ConnectionStrings:DataLooM"] = "Host=dls-prod-pg.postgres.database.azure.com;Port=5432;Database=dataloomstudio;Username=dls_app;Ssl Mode=Require;Trust Server Certificate=false",
            ["DataLooM:PostgreSqlUseManagedIdentity"] = "true",
            ["DataLooM:BlobServiceUri"] = "https://dlsprodstorage.blob.core.windows.net",
            ["DataLooM:EvidenceContainerName"] = "evidence",
            ["DataLooM:ServiceBusFullyQualifiedNamespace"] = "dls-prod.servicebus.windows.net",
            ["DataLooM:ServiceBusOutboxTopic"] = "dataloomstudio-outbox",
            ["DataLooM:KeyVaultUri"] = "https://dls-prod-kv.vault.azure.net/",
            ["DataLooM:EnvironmentName"] = "dls-prod",
            ["DataLooM:EnvironmentKind"] = "prod",
            ["DataLooM:AllowedOriginsCsv"] = "https://app.dataloomstudio.internal",
            ["EntraId:Authority"] = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ["EntraId:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["EntraId:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["EntraId:RequiredScope"] = "Dls.Access",
            ["DataLooM:MalwareScannerEndpoint"] = "https://scanner.dataloomstudio.internal/",
            ["DataLooM:MalwareScannerAudience"] = "api://33333333-3333-3333-3333-333333333333",
            ["DataLooM:MalwareScannerTimeoutSeconds"] = "30",
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

    [Theory]
    [InlineData("https://identity.example.net/11111111-1111-1111-1111-111111111111/v2.0")]
    [InlineData("https://login.microsoftonline.com/common/v2.0")]
    [InlineData("https://login.microsoftonline.com/organizations/v2.0")]
    [InlineData("https://login.microsoftonline.com/99999999-9999-9999-9999-999999999999/v2.0")]
    public void Hardened_configuration_rejects_noncanonical_or_cross_tenant_authority(string authority)
    {
        var values = ValidApiConfiguration();
        values["EntraId:Authority"] = authority;

        var result = ProductionConfigurationValidator.Validate(
            BuildConfiguration(values),
            "Test",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("EntraId:Authority", StringComparison.Ordinal));
    }

    [Fact]
    public void Hardened_configuration_rejects_audience_that_is_not_the_configured_application()
    {
        var values = ValidApiConfiguration();
        values["EntraId:Audience"] = "api://99999999-9999-9999-9999-999999999999";

        var result = ProductionConfigurationValidator.Validate(
            BuildConfiguration(values),
            "Test",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Audience", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("api://22222222-2222-2222-2222-222222222222/Dls.Access")]
    [InlineData("Dls.Access Other.Scope")]
    public void Hardened_configuration_rejects_noncanonical_required_scope(string requiredScope)
    {
        var values = ValidApiConfiguration();
        values["EntraId:RequiredScope"] = requiredScope;

        var result = ProductionConfigurationValidator.Validate(
            BuildConfiguration(values),
            "Test",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("RequiredScope", StringComparison.Ordinal));
    }

    [Fact]
    public void Canonical_actor_claims_require_matching_tenant_and_nonempty_object_id()
    {
        const string tenantId = "11111111-1111-1111-1111-111111111111";
        var valid = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tid", tenantId),
            new Claim("oid", "22222222-2222-2222-2222-222222222222")
        ], "Bearer"));
        var wrongTenant = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("tid", "99999999-9999-9999-9999-999999999999"),
            new Claim("oid", "22222222-2222-2222-2222-222222222222")
        ], "Bearer"));

        Assert.True(EntraTokenIdentityValidator.HasCanonicalActorClaims(valid, tenantId));
        Assert.False(EntraTokenIdentityValidator.HasCanonicalActorClaims(wrongTenant, tenantId));
        Assert.False(EntraTokenIdentityValidator.HasCanonicalActorClaims(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", tenantId)], "Bearer")),
            tenantId));
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

    [Fact]
    public void Test_configuration_rejects_development_defaults()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["ConnectionStrings:DataLooM"] = "Host=localhost;Port=5432;Database=dataloomstudio;Username=dataloom_app;Password=local",
            ["DataLooM:EnvironmentName"] = "dls-test",
            ["DataLooM:EnvironmentKind"] = "test"
        });

        var result = ProductionConfigurationValidator.Validate(
            configuration,
            "Test",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("localhost", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("PostgreSqlUseManagedIdentity", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("persisted database password", StringComparison.Ordinal));
    }

    [Fact]
    public void Development_host_cannot_bypass_hardened_test_kind()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["DataLooM:EnvironmentKind"] = "test"
        });

        var result = ProductionConfigurationValidator.Validate(
            configuration,
            "Development",
            "DataLooMStudio.Api",
            requireHttpSurface: true,
            requireWorkerIdentity: false);

        Assert.False(result.Succeeded);
    }

    private static Dictionary<string, string?> GovernedCommonConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:DataLooM"] = "Host=dls-prod-pg.postgres.database.azure.com;Port=5432;Database=dataloomstudio;Username=dls_worker;Ssl Mode=Require;Trust Server Certificate=false",
            ["DataLooM:PostgreSqlUseManagedIdentity"] = "true",
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

    private static Dictionary<string, string?> ValidApiConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "api.dataloomstudio.internal",
            ["ConnectionStrings:DataLooM"] = "Host=dls-test-pg.postgres.database.azure.com;Port=5432;Database=dataloomstudio;Username=dls_api;Ssl Mode=Require;Trust Server Certificate=false",
            ["DataLooM:PostgreSqlUseManagedIdentity"] = "true",
            ["DataLooM:BlobServiceUri"] = "https://dlsteststorage.blob.core.windows.net",
            ["DataLooM:EvidenceContainerName"] = "evidence",
            ["DataLooM:ServiceBusFullyQualifiedNamespace"] = "dls-test.servicebus.windows.net",
            ["DataLooM:ServiceBusOutboxTopic"] = "dataloomstudio-outbox",
            ["DataLooM:KeyVaultUri"] = "https://dls-test-kv.vault.azure.net/",
            ["DataLooM:EnvironmentName"] = "dls-test",
            ["DataLooM:EnvironmentKind"] = "test",
            ["DataLooM:AllowedOriginsCsv"] = "https://app.dataloomstudio.internal",
            ["EntraId:Authority"] = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ["EntraId:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["EntraId:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["EntraId:Audience"] = "api://22222222-2222-2222-2222-222222222222",
            ["EntraId:RequiredScope"] = "Dls.Access",
            ["DataLooM:MalwareScannerEndpoint"] = "https://scanner.dataloomstudio.internal/",
            ["DataLooM:MalwareScannerAudience"] = "api://33333333-3333-3333-3333-333333333333",
            ["DataLooM:MalwareScannerTimeoutSeconds"] = "30",
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