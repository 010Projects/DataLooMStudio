namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityRoleClasses
{
    public const string ProductBusinessRole = "ProductBusinessRole";

    public const string ProductAdministrationRole = "ProductAdministrationRole";

    public const string CommercialAdministrationRole = "CommercialAdministrationRole";

    public const string OperationalSupportAuthority = "OperationalSupportAuthority";

    public const string PrivilegedTechnicalAuthority = "PrivilegedTechnicalAuthority";

    public const string IndependentAssuranceRole = "IndependentAssuranceRole";

    public static bool IsPrivilegedTechnicalOrOperational(string roleClass)
    {
        return roleClass.Equals(OperationalSupportAuthority, StringComparison.Ordinal)
            || roleClass.Equals(PrivilegedTechnicalAuthority, StringComparison.Ordinal);
    }
}