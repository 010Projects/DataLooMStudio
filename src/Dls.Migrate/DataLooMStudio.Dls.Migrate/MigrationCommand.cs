namespace DataLooMStudio.Dls.Migrate;

public sealed record MigrationCommand(
    bool Apply,
    bool BootstrapRuntimeRoles,
    string? ConnectionString)
{
    public static MigrationCommand Parse(string[] args)
    {
        var apply = false;
        var bootstrapRuntimeRoles = false;
        string? connectionString = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.Equals("--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
                continue;
            }

            if (arg.Equals("--bootstrap-runtime-roles", StringComparison.OrdinalIgnoreCase))
            {
                bootstrapRuntimeRoles = true;
                continue;
            }

            if (arg.Equals("--connection", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                connectionString = args[++index];
            }
        }

        return new MigrationCommand(apply, bootstrapRuntimeRoles, connectionString);
    }
}