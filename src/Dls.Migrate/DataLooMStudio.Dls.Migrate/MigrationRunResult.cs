namespace DataLooMStudio.Dls.Migrate;

public sealed record MigrationRunResult(
    bool Succeeded,
    int AppliedMigrationCount,
    string? LastAppliedMigration,
    string? ErrorMessage)
{
    public static MigrationRunResult Success(int appliedMigrationCount, string? lastAppliedMigration) =>
        new(true, appliedMigrationCount, lastAppliedMigration, null);

    public static MigrationRunResult Failure(Exception exception) => new(false, 0, null, exception.Message);
}