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

        if (!environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            return new ProductionConfigurationValidationResult(errors);
        }

        RequireEnvironmentIdentity(configuration, errors);
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

    private static void RequireEnvironmentIdentity(IConfiguration configuration, ICollection<string> errors)
    {
        RequireValue(configuration["DataLooM:EnvironmentName"], "DataLooM:EnvironmentName", errors);

        var environmentKind = configuration["DataLooM:EnvironmentKind"];
        RequireValue(environmentKind, "DataLooM:EnvironmentKind", errors);
        if (!string.IsNullOrWhiteSpace(environmentKind)
            && !environmentKind.Equals("prod", StringComparison.OrdinalIgnoreCase)
            && !environmentKind.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("DataLooM:EnvironmentKind must identify a production environment when ASPNETCORE_ENVIRONMENT is Production.");
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
            errors.Add("ConnectionStrings:DataLooM must not point at localhost or a development database in Production.");
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
            errors.Add("Either EntraId:ClientId or EntraId:Audience must be configured in Production.");
        }

        RejectPlaceholder(clientId, "EntraId:ClientId", errors);
        RejectPlaceholder(audience, "EntraId:Audience", errors);
    }

    private static void RequireHttpSurface(IConfiguration configuration, ICollection<string> errors)
    {
        var allowedHosts = configuration["AllowedHosts"];
        RequireValue(allowedHosts, "AllowedHosts", errors);
        if (!string.IsNullOrWhiteSpace(allowedHosts)
            && allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(host => host.Equals("*", StringComparison.Ordinal) || host.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("AllowedHosts must be explicitly governed and must not include wildcard or localhost values in Production.");
        }

        var allowedOrigins = ResolveAllowedOrigins(configuration);
        if (allowedOrigins.Length == 0)
        {
            errors.Add("At least one externally governed HTTPS origin must be configured for DataLooM:AllowedOrigins or DataLooM:AllowedOriginsCsv in Production.");
        }

        foreach (var origin in allowedOrigins)
        {
            RequireHttpsUrl(origin, "DataLooM:AllowedOrigins", errors);
            if (origin.Contains('*', StringComparison.Ordinal)
                || origin.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                || origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Allowed origins must not include wildcard or local development origins in Production.");
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
            errors.Add($"{key} must be an absolute HTTPS URI in Production.");
        }
    }

    private static void RequireValue(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required in Production.");
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
}