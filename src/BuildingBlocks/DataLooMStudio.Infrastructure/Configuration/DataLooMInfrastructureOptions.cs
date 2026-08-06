namespace DataLooMStudio.Infrastructure.Configuration;

public sealed class DataLooMInfrastructureOptions
{
    public string? PostgreSqlConnectionString { get; init; }

    public string? BlobServiceUri { get; init; }

    public string EvidenceContainerName { get; init; } = "evidence";

    public string? ServiceBusFullyQualifiedNamespace { get; init; }

    public string ServiceBusOutboxTopic { get; init; } = "dataloomstudio-outbox";

    public string? KeyVaultUri { get; init; }
}