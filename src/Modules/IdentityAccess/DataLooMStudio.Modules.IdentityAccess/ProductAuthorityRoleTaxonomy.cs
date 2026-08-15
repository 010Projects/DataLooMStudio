namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityRoleTaxonomy
{
    public const string DecisionId = "DLS-PROD-AUTH-001";

    private static readonly ProductAuthorityRoleDefinition[] RoleDefinitions =
    [
        new(
            ProductAuthorityRoleNames.TenantOwner,
            ProductAuthorityRoleClasses.ProductAdministrationRole,
            []),
        new(
            ProductAuthorityRoleNames.WorkspaceOwner,
            ProductAuthorityRoleClasses.ProductAdministrationRole,
            []),
        new(
            ProductAuthorityRoleNames.EvidenceContributor,
            ProductAuthorityRoleClasses.ProductBusinessRole,
            [ProductAuthorityPermissions.RegisterEvidence]),
        new(
            ProductAuthorityRoleNames.EvidenceReader,
            ProductAuthorityRoleClasses.ProductBusinessRole,
            [ProductAuthorityPermissions.ReadEvidence]),
        new(
            ProductAuthorityRoleNames.Reviewer,
            ProductAuthorityRoleClasses.ProductBusinessRole,
            [ProductAuthorityPermissions.CreateEvidenceCandidateDecision]),
        new(
            ProductAuthorityRoleNames.DecisionApprover,
            ProductAuthorityRoleClasses.ProductBusinessRole,
            [ProductAuthorityPermissions.ApplyEvidenceDecision]),
        new(
            ProductAuthorityRoleNames.GovernanceAdministrator,
            ProductAuthorityRoleClasses.ProductAdministrationRole,
            [
                ProductAuthorityPermissions.ManageProductPermissionAssignments,
                ProductAuthorityPermissions.ManageEvidenceReviewAssignments
            ]),
        new(
            ProductAuthorityRoleNames.RetentionAdministrator,
            ProductAuthorityRoleClasses.ProductAdministrationRole,
            [
                ProductAuthorityPermissions.ManageRetentionPolicy,
                ProductAuthorityPermissions.EvaluateDeletionEligibility
            ]),
        new(
            ProductAuthorityRoleNames.LegalHoldAdministrator,
            ProductAuthorityRoleClasses.ProductAdministrationRole,
            [
                ProductAuthorityPermissions.ManageLegalHold,
                ProductAuthorityPermissions.RequestLegalHoldRelease,
                ProductAuthorityPermissions.ApproveLegalHoldRelease
            ]),
        new(
            ProductAuthorityRoleNames.CommercialAdministrator,
            ProductAuthorityRoleClasses.CommercialAdministrationRole,
            []),
        new(
            ProductAuthorityRoleNames.BillingAdministrator,
            ProductAuthorityRoleClasses.CommercialAdministrationRole,
            []),
        new(
            ProductAuthorityRoleNames.SupportOperator,
            ProductAuthorityRoleClasses.OperationalSupportAuthority,
            [
                ProductAuthorityPermissions.ReadSupportDiagnostics,
                ProductAuthorityPermissions.ActivateSupportElevation
            ]),
        new(
            ProductAuthorityRoleNames.SecurityOperator,
            ProductAuthorityRoleClasses.PrivilegedTechnicalAuthority,
            [ProductAuthorityPermissions.ActivateBreakGlass]),
        new(
            ProductAuthorityRoleNames.RepositoryAdministrator,
            ProductAuthorityRoleClasses.PrivilegedTechnicalAuthority,
            []),
        new(
            ProductAuthorityRoleNames.PlatformAdministrator,
            ProductAuthorityRoleClasses.PrivilegedTechnicalAuthority,
            []),
        new(
            ProductAuthorityRoleNames.Auditor,
            ProductAuthorityRoleClasses.IndependentAssuranceRole,
            [])
    ];

    public static IReadOnlyList<ProductAuthorityRoleDefinition> Roles => RoleDefinitions;

    public static bool IsSupportedRole(string roleName)
    {
        return RoleDefinitions.Any(role => role.RoleName.Equals(roleName, StringComparison.Ordinal));
    }

    public static ProductAuthorityRoleDefinition? FindRole(string roleName)
    {
        return RoleDefinitions.SingleOrDefault(role => role.RoleName.Equals(roleName, StringComparison.Ordinal));
    }
}