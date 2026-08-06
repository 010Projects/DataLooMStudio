namespace DataLooMStudio.Runtime.Persistence.Migrations;

public sealed record ModuleMigrationBoundary(
    string ModuleName,
    string SchemaName,
    string MigrationsNamespace);