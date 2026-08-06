namespace DataLooMStudio.Dls.Migrate;

public sealed record MigrationRunResult(bool Succeeded, int AppliedMigrationCount, string? ErrorMessage)
{
    public static MigrationRunResult Success(int appliedMigrationCount) => new(true, appliedMigrationCount, null);

    public static MigrationRunResult Failure(Exception exception) => new(false, 0, exception.Message);
}