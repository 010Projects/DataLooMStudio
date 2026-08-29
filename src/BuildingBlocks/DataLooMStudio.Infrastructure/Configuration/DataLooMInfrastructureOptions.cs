namespace DataLooMStudio.Infrastructure.Configuration;

public sealed class DataLooMInfrastructureOptions
{
    public string? PostgreSqlConnectionString { get; init; }

    public string? BlobServiceUri { get; init; }

    public string EvidenceContainerName { get; init; } = "evidence";

    public string? ServiceBusFullyQualifiedNamespace { get; init; }

    public string ServiceBusOutboxTopic { get; init; } = "dataloomstudio-outbox";

    public string? KeyVaultUri { get; init; }

    public string? EnvironmentName { get; init; }

    public string? EnvironmentKind { get; init; }

    public string? AllowedOriginsCsv { get; init; }

    public string? WorkerIdentitySubject { get; init; }
}