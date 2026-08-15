namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthoritySources
{
    public const string None = "None";

    public const string IdentityCorrelation = "IdentityCorrelation";

    public const string PermissionAssignment = "PermissionAssignment";

    public const string PrivilegedElevation = "PrivilegedElevation";

    public const string SupportElevation = "SupportElevation";

    public const string BreakGlassElevation = "BreakGlassElevation";

    public const string WorkloadMatrix = "WorkloadMatrix";
}