namespace DataLooMStudio.Modules.IdentityAccess;

public sealed record ProductAuthorityRoleDefinition(
    string RoleName,
    string RoleClass,
    IReadOnlyList<string> PermissionBundle)
{
    public bool IsPrivilegedTechnicalOrOperational =>
        ProductAuthorityRoleClasses.IsPrivilegedTechnicalOrOperational(RoleClass);
}