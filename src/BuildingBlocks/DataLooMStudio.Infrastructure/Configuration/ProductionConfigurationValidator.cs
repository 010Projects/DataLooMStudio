using System.Data.Common;

using Microsoft.Extensions.Configuration;

namespace DataLooMStudio.Infrastructure.Configuration;

public static class ProductionConfigurationValidator
{
    private static readonly string[] PlaceholderTokens =
    [
        "placeholder",
        "changeme",
        "change-me",
        "example",
        "not-set",
        "todo"
    ];

    public static ProductionConfigurationValidationResult Validate(
        IConfiguration configuration,
        string environmentName,
        string componentName,
        bool requireHttpSurface,
        bool requireWorkerIdentity)
    {
        var errors = new List<string>();

        var environmentKind = configuration["DataLooM:EnvironmentKind"];
        if (!IsHardenedEnvironment(environmentName, environmentKind))
        {
            return new ProductionConfigurationValidationResult(errors);
        }

        RequireEnvironmentIdentity(configuration, environmentName, errors);
        RequireDatabase(configuration, errors);
        RequireAzureDependency(
            configuration["DataLooM:BlobServiceUri"],
            "DataLooM:BlobServiceUri",
            "blob.",
            errors);
        RequireValue(configuration["DataLooM:EvidenceContainerName"], "DataLooM:EvidenceContainerName", errors);
        RequireAzureHost(
            configuration["DataLooM:ServiceBusFullyQualifiedNamespace"],
            "DataLooM:ServiceBusFullyQualifiedNamespace",
            ".servicebus.windows.net",
            errors);
        RequireValue(configuration["DataLooM:ServiceBusOutboxTopic"], "DataLooM:ServiceBusOutboxTopic", errors);
        RequireAzureDependency(configuration["DataLooM:KeyVaultUri"], "DataLooM:KeyVaultUri", ".vault.azure.net", errors);
        RequireOtelEndpoint(configuration, errors);

        if (requireHttpSurface)
        {
            RequireHttpSurface(configuration, errors);
            RequireEntra(configuration, errors);
            RequireMalwareScanner(configuration, errors);
        }

        if (requireWorkerIdentity)
        {
            RequireValue(configuration["DataLooM:WorkerIdentitySubject"], "DataLooM:WorkerIdentitySubject", errors);
        }

        return new ProductionConfigurationValidationResult(errors);
    }

    public static void ValidateAndThrow(
        IConfiguration configuration,
        string environmentName,
        string componentName,
        bool requireHttpSurface,
        bool requireWorkerIdentity)
    {
        Validate(configuration, environmentName, componentName, requireHttpSurface, requireWorkerIdentity)
            .ThrowIfInvalid(componentName);
    }

    public static string[] ResolveAllowedOrigins(IConfiguration configuration)
    {
        var configuredArray = configuration
            .GetSection("DataLooM:AllowedOrigins")
            .Get<string[]>() ?? [];

        var configuredCsv = configuration["DataLooM:AllowedOriginsCsv"];
        var csvOrigins = string.IsNullOrWhiteSpace(configuredCsv)
            ? []
            : configuredCsv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return configuredArray
            .Concat(csvOrigins)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RequireEnvironmentIdentity(
        IConfiguration configuration,
        string environmentName,
        ICollection<string> errors)
    {
        RequireValue(configuration["DataLooM:EnvironmentName"], "DataLooM:EnvironmentName", errors);

        var environmentKind = configuration["DataLooM:EnvironmentKind"];
        RequireValue(environmentKind, "DataLooM:EnvironmentKind", errors);
        if (string.IsNullOrWhiteSpace(environmentKind))
        {
            return;
        }

        var isTestHost = environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase);
        var isProductionHost = environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase);
        var isTestKind = environmentKind.Equals("test", StringComparison.OrdinalIgnoreCase);
        var isProductionKind = environmentKind.Equals("prod", StringComparison.OrdinalIgnoreCase)
            || environmentKind.Equals("production", StringComparison.OrdinalIgnoreCase);

        if ((isTestHost && !isTestKind) || (isProductionHost && !isProductionKind))
        {
            errors.Add("DataLooM:EnvironmentKind must match the hardened host environment identity.");
        }
    }

    private static void RequireDatabase(IConfiguration configuration, ICollection<string> errors)
    {
        var connectionString = configuration.GetConnectionString("DataLooM")
            ?? configuration["DataLooM:PostgreSqlConnectionString"];

        RequireValue(connectionString, "ConnectionStrings:DataLooM", errors);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (ContainsDevelopmentHost(connectionString))
        {
            errors.Add("ConnectionStrings:DataLooM must not point at localhost or a development database in a hardened environment.");
        }

        if (!configuration.GetValue<bool>("DataLooM:PostgreSqlUseManagedIdentity"))
        {
            errors.Add("DataLooM:PostgreSqlUseManagedIdentity must be true in a hardened environment.");
        }

        var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (parsed.ContainsKey("Password") || parsed.ContainsKey("Pwd"))
        {
            errors.Add("ConnectionStrings:DataLooM must not contain a persisted database password in a hardened environment.");
        }
    }

    private static void RequireEntra(IConfiguration configuration, ICollection<string> errors)
    {
        var authority = configuration["EntraId:Authority"];
        var instance = configuration["EntraId:Instance"];
        var tenantId = configuration["EntraId:TenantId"];
        var clientId = configuration["EntraId:ClientId"];
        var audience = configuration["EntraId:Audience"];

        if (string.IsNullOrWhiteSpace(authority))
        {
            RequireHttpsUrl(instance, "EntraId:Instance", errors);
            RequireValue(tenantId, "EntraId:TenantId", errors);
        }
        else
        {
            RequireHttpsUrl(authority, "EntraId:Authority", errors);
        }

        if (string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(audience))
        {
            errors.Add("Either EntraId:ClientId or EntraId:Audience must be configured in a hardened environment.");
        }

        RejectPlaceholder(clientId, "EntraId:ClientId", errors);
        RejectPlaceholder(audience, "EntraId:Audience", errors);
        RequireValue(configuration["EntraId:RequiredScope"], "EntraId:RequiredScope", errors);
    }

    private static void RequireHttpSurface(IConfiguration configuration, ICollection<string> errors)
    {
        var allowedHosts = configuration["AllowedHosts"];
        RequireValue(allowedHosts, "AllowedHosts", errors);
        if (!string.IsNullOrWhiteSpace(allowedHosts)
            && allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(host => host.Equals("*", StringComparison.Ordinal) || host.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("AllowedHosts must be explicitly governed and must not include wildcard or localhost values in a hardened environment.");
        }

        var allowedOrigins = ResolveAllowedOrigins(configuration);
        if (allowedOrigins.Length == 0)
        {
            errors.Add("At least one externally governed HTTPS origin must be configured for DataLooM:AllowedOrigins or DataLooM:AllowedOriginsCsv in a hardened environment.");
        }

        foreach (var origin in allowedOrigins)
        {
            RequireHttpsUrl(origin, "DataLooM:AllowedOrigins", errors);
            if (origin.Contains('*', StringComparison.Ordinal)
                || origin.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Allowed origins must not include wildcard or local development origins in a hardened environment.");
            }
        }
    }

    private static void RequireOtelEndpoint(IConfiguration configuration, ICollection<string> errors)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? configuration["Otel:ExporterOtlpEndpoint"];

        RequireHttpsUrl(endpoint, "OTEL_EXPORTER_OTLP_ENDPOINT", errors);
    }

    private static void RequireAzureDependency(
        string? value,
        string key,
        string expectedToken,
        ICollection<string> errors)
    {
        RequireHttpsUrl(value, key, errors);

        if (!string.IsNullOrWhiteSpace(value)
            && !value.Contains(expectedToken, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{key} must reference the approved Azure dependency boundary.");
        }
    }

    private static void RequireAzureHost(
        string? value,
        string key,
        string expectedToken,
        ICollection<string> errors)
    {
        RequireValue(value, key, errors);

        if (!string.IsNullOrWhiteSpace(value)
            && !value.Contains(expectedToken, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{key} must reference the approved Azure dependency boundary.");
        }
    }

    private static void RequireHttpsUrl(string? value, string key, ICollection<string> errors)
    {
        RequireValue(value, key, errors);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{key} must be an absolute HTTPS URI in a hardened environment.");
        }
    }

    private static void RequireValue(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required in a hardened environment.");
            return;
        }

        RejectPlaceholder(value, key, errors);
    }

    private static void RejectPlaceholder(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (PlaceholderTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{key} contains a placeholder value.");
        }
    }

    private static bool ContainsDevelopmentHost(string value)
    {
        return value.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Host=.", StringComparison.OrdinalIgnoreCase)
            || value.Contains("dataloom_app", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireMalwareScanner(IConfiguration configuration, ICollection<string> errors)
    {
        RequireHttpsUrl(
            configuration["DataLooM:MalwareScannerEndpoint"],
            "DataLooM:MalwareScannerEndpoint",
            errors);
        RequireValue(
            configuration["DataLooM:MalwareScannerAudience"],
            "DataLooM:MalwareScannerAudience",
            errors);

        var timeoutSeconds = configuration.GetValue<int>("DataLooM:MalwareScannerTimeoutSeconds");
        if (timeoutSeconds is < 5 or > 120)
        {
            errors.Add("DataLooM:MalwareScannerTimeoutSeconds must be between 5 and 120 in a hardened environment.");
        }
    }

    private static bool IsHardenedEnvironment(string environmentName, string? environmentKind)
    {
        return environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase)
            || environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase)
            || environmentKind?.Equals("test", StringComparison.OrdinalIgnoreCase) == true
            || environmentKind?.Equals("prod", StringComparison.OrdinalIgnoreCase) == true
            || environmentKind?.Equals("production", StringComparison.OrdinalIgnoreCase) == true
            || environmentKind?.Equals("pilot", StringComparison.OrdinalIgnoreCase) == true;
    }
}