namespace DataLooMStudio.Infrastructure.Configuration;

public sealed class DataLooMInfrastructureOptions
{
    public string? PostgreSqlConnectionString { get; init; }

    public bool PostgreSqlUseManagedIdentity { get; init; }

    public string? BlobServiceUri { get; init; }

    public string EvidenceContainerName { get; init; } = "evidence";

    public string? ServiceBusFullyQualifiedNamespace { get; init; }

    public string ServiceBusOutboxTopic { get; init; } = "dataloomstudio-outbox";

    public string? KeyVaultUri { get; init; }

    public string? EnvironmentName { get; init; }

    public string? EnvironmentKind { get; init; }

    public string? AllowedOriginsCsv { get; init; }

    public string? WorkerIdentitySubject { get; init; }

    public bool WorkerProcessingEnabled { get; init; }

    public string? MalwareScannerEndpoint { get; init; }

    public string? MalwareScannerAudience { get; init; }

    public int MalwareScannerTimeoutSeconds { get; init; } = 30;
}