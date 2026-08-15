using System.Security.Cryptography;
using System.Text;

using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Modules.Commercial;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Persistence.Tests;

public sealed class IdentityAccessSecurityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Unknown_actor_is_denied_by_default_and_audited()
    {
        var scenario = await CreateScenarioAsync("unknown.actor");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await service.EvaluatePermissionAsync(
            Request("unknown.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var audits = await verificationDbContext.AuditEntries
            .Where(audit => audit.Action == "ProductAuthority.Denied" && audit.Outcome == "Deny")
            .Select(audit => audit.MetadataJson)
            .ToArrayAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.IdentityInvalid, result.DenialReasonCode);
        Assert.Contains(audits, metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.IdentityInvalid, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tenant_context_substitution_cannot_use_another_tenants_authority()
    {
        var tenantA = await CreateScenarioAsync("tenant-a.actor");
        await SeedActorAsync(
            tenantA,
            "tenant-a.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);
        var tenantB = await CreateScenarioAsync("tenant-a.actor");

        var result = await EvaluateAsync(
            tenantB,
            Request("tenant-a.actor", ProductAuthorityPermissions.ReadEvidence, "tenant-a-evidence"));

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.IdentityInvalid, result.DenialReasonCode);
    }

    [Fact]
    public async Task Forged_tenant_identifier_does_not_disclose_or_grant_product_authority()
    {
        var tenantA = await CreateScenarioAsync("forged-tenant.actor");
        await SeedActorAsync(
            tenantA,
            "forged-tenant.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);
        var forgedAccessor = CreateRequestContext(TenantId.New(), tenantA.WorkspaceId, "forged-tenant.actor");
        await using var dbContext = fixture.CreateDbContext(forgedAccessor);
        var service = CreateService(dbContext, forgedAccessor, tenantA.Clock);

        var result = await service.EvaluatePermissionAsync(
            Request("forged-tenant.actor", ProductAuthorityPermissions.ReadEvidence, "tenant-a-evidence"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.IdentityInvalid, result.DenialReasonCode);
    }

    [Fact]
    public async Task Disabled_actor_cannot_complete_consequential_action()
    {
        var scenario = await CreateScenarioAsync("disabled.actor");
        await SeedActorAsync(
            scenario,
            "disabled.actor",
            ProductActorTypes.Human,
            ProductActorStates.Disabled,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ApplyEvidenceDecision);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await service.EvaluatePermissionAsync(
            ReviewRequest("disabled.actor", ProductAuthorityPermissions.ApplyEvidenceDecision, "review-1"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.IdentityDisabled, result.DenialReasonCode);
    }

    [Fact]
    public async Task Tenant_and_workspace_memberships_are_required_before_permissions_are_honored()
    {
        var tenantScenario = await CreateScenarioAsync("tenant.denied");
        await SeedActorAsync(
            tenantScenario,
            "tenant.denied",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: false,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);
        var tenantDenied = await EvaluateAsync(
            tenantScenario,
            Request("tenant.denied", ProductAuthorityPermissions.ReadEvidence, "evidence-1"));

        var workspaceScenario = await CreateScenarioAsync("workspace.denied");
        await SeedActorAsync(
            workspaceScenario,
            "workspace.denied",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: false,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);
        var workspaceDenied = await EvaluateAsync(
            workspaceScenario,
            Request("workspace.denied", ProductAuthorityPermissions.ReadEvidence, "evidence-1"));

        Assert.Equal(ProductAuthorityDenyReasonCodes.TenantAccessDenied, tenantDenied.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.WorkspaceAccessDenied, workspaceDenied.DenialReasonCode);
    }

    [Fact]
    public async Task Revoked_and_stale_memberships_cannot_exercise_product_authority()
    {
        var revokedTenant = await CreateScenarioAsync("revoked-tenant.actor");
        await SeedActorAsync(
            revokedTenant,
            "revoked-tenant.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence,
            tenantMembershipState: ProductMembershipStates.Revoked);
        var revokedTenantResult = await EvaluateAsync(
            revokedTenant,
            Request("revoked-tenant.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1"));

        var revokedWorkspace = await CreateScenarioAsync("revoked-workspace.actor");
        await SeedActorAsync(
            revokedWorkspace,
            "revoked-workspace.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence,
            workspaceMembershipState: ProductMembershipStates.Revoked);
        var revokedWorkspaceResult = await EvaluateAsync(
            revokedWorkspace,
            Request("revoked-workspace.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1"));

        var staleMembership = await CreateScenarioAsync("stale-membership.actor");
        await SeedActorAsync(
            staleMembership,
            "stale-membership.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence,
            actorAuthorityVersion: 2,
            membershipAuthorityVersion: 1,
            assignmentAuthorityVersion: 2);
        var staleMembershipResult = await EvaluateAsync(
            staleMembership,
            Request("stale-membership.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1"));

        Assert.Equal(ProductAuthorityDenyReasonCodes.TenantAccessDenied, revokedTenantResult.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.WorkspaceAccessDenied, revokedWorkspaceResult.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AuthorityStale, staleMembershipResult.DenialReasonCode);
    }

    [Theory]
    [InlineData("tenant.owner", ProductAuthorityRoleNames.TenantOwner, ProductActorTypes.Human)]
    [InlineData("workspace.owner", ProductAuthorityRoleNames.WorkspaceOwner, ProductActorTypes.Human)]
    [InlineData("commercial.admin", ProductAuthorityRoleNames.CommercialAdministrator, ProductActorTypes.Human)]
    [InlineData("billing.admin", ProductAuthorityRoleNames.BillingAdministrator, ProductActorTypes.Human)]
    [InlineData("support.operator", ProductAuthorityRoleNames.SupportOperator, ProductActorTypes.Support)]
    [InlineData("security.operator", ProductAuthorityRoleNames.SecurityOperator, ProductActorTypes.Human)]
    [InlineData("repository.admin", ProductAuthorityRoleNames.RepositoryAdministrator, ProductActorTypes.Human)]
    [InlineData("platform.admin", ProductAuthorityRoleNames.PlatformAdministrator, ProductActorTypes.Human)]
    public async Task Canonical_role_labels_do_not_grant_evidence_authority_without_explicit_permission(
        string actorSubject,
        string role,
        string actorType)
    {
        var scenario = await CreateScenarioAsync(actorSubject);
        await SeedActorAsync(
            scenario,
            actorSubject,
            actorType,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);

        var result = await EvaluateAsync(
            scenario,
            Request(actorSubject, ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ActorType = actorType,
                ProductRole = role
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AssignmentRequired, result.DenialReasonCode);
    }

    [Theory]
    [InlineData("tenant-owner-review", ProductAuthorityRoleNames.TenantOwner, ProductActorTypes.Human)]
    [InlineData("workspace-owner-review", ProductAuthorityRoleNames.WorkspaceOwner, ProductActorTypes.Human)]
    [InlineData("commercial-admin-review", ProductAuthorityRoleNames.CommercialAdministrator, ProductActorTypes.Human)]
    [InlineData("billing-admin-review", ProductAuthorityRoleNames.BillingAdministrator, ProductActorTypes.Human)]
    [InlineData("support-operator-review", ProductAuthorityRoleNames.SupportOperator, ProductActorTypes.Support)]
    [InlineData("security-operator-review", ProductAuthorityRoleNames.SecurityOperator, ProductActorTypes.Human)]
    [InlineData("repository-admin-review", ProductAuthorityRoleNames.RepositoryAdministrator, ProductActorTypes.Human)]
    [InlineData("platform-admin-review", ProductAuthorityRoleNames.PlatformAdministrator, ProductActorTypes.Human)]
    public async Task Canonical_owner_admin_and_technical_roles_do_not_grant_review_or_decision_authority(
        string actorSubject,
        string role,
        string actorType)
    {
        var scenario = await CreateScenarioAsync(actorSubject);
        await SeedActorAsync(
            scenario,
            actorSubject,
            actorType,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);

        var result = await EvaluateAsync(
            scenario,
            ReviewRequest(actorSubject, ProductAuthorityPermissions.ApplyEvidenceDecision, "review-1") with
            {
                ActorType = actorType,
                ProductRole = role
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AssignmentRequired, result.DenialReasonCode);
    }

    [Fact]
    public async Task Legacy_local_role_labels_are_not_canonical_product_authority_metadata()
    {
        var scenario = await CreateScenarioAsync("legacy-role.actor");
        await SeedActorAsync(
            scenario,
            "legacy-role.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);

        var result = await EvaluateAsync(
            scenario,
            Request("legacy-role.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ProductRole = "EvidenceReviewer"
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.PermissionDenied, result.DenialReasonCode);
    }

    [Fact]
    public async Task Explicit_scoped_permission_assignment_grants_only_the_requested_resource()
    {
        var scenario = await CreateScenarioAsync("scoped.reviewer");
        await SeedActorAsync(
            scenario,
            "scoped.reviewer",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ApplyEvidenceDecision,
            resourceType: ProductAuthorityResourceTypes.EvidenceReview,
            resourceId: "review-allowed");

        var allowed = await EvaluateAsync(
            scenario,
            ReviewRequest("scoped.reviewer", ProductAuthorityPermissions.ApplyEvidenceDecision, "review-allowed"));
        var denied = await EvaluateAsync(
            scenario,
            ReviewRequest("scoped.reviewer", ProductAuthorityPermissions.ApplyEvidenceDecision, "review-denied"));

        Assert.True(allowed.Succeeded);
        Assert.Equal(ProductAuthorityPermissions.ApplyEvidenceDecision, allowed.EffectivePermission);
        Assert.Equal(ProductAuthoritySources.PermissionAssignment, allowed.AuthoritySource);
        Assert.False(denied.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AssignmentRequired, denied.DenialReasonCode);
    }

    [Fact]
    public async Task Revoked_and_stale_authority_cannot_complete_new_actions()
    {
        var revokedScenario = await CreateScenarioAsync("revoked.reviewer");
        await SeedActorAsync(
            revokedScenario,
            "revoked.reviewer",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ApplyEvidenceDecision,
            resourceType: ProductAuthorityResourceTypes.EvidenceReview,
            resourceId: "review-1",
            assignmentState: ProductPermissionAssignmentStates.Revoked);
        var revoked = await EvaluateAsync(
            revokedScenario,
            ReviewRequest("revoked.reviewer", ProductAuthorityPermissions.ApplyEvidenceDecision, "review-1"));
        var revokedAudits = await ReadDeniedAuthorityAuditMetadataAsync(revokedScenario);

        var staleScenario = await CreateScenarioAsync("stale.reviewer");
        await SeedActorAsync(
            staleScenario,
            "stale.reviewer",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ApplyEvidenceDecision,
            resourceType: ProductAuthorityResourceTypes.EvidenceReview,
            resourceId: "review-1",
            actorAuthorityVersion: 2,
            membershipAuthorityVersion: 2,
            assignmentAuthorityVersion: 1);
        var stale = await EvaluateAsync(
            staleScenario,
            ReviewRequest("stale.reviewer", ProductAuthorityPermissions.ApplyEvidenceDecision, "review-1"));
        var staleAudits = await ReadDeniedAuthorityAuditMetadataAsync(staleScenario);

        Assert.Equal(ProductAuthorityDenyReasonCodes.PermissionDenied, revoked.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AuthorityStale, stale.DenialReasonCode);
        Assert.Contains(revokedAudits, metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.PermissionDenied, StringComparison.Ordinal));
        Assert.Contains(staleAudits, metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.AuthorityStale, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Captured_authority_version_and_age_are_revalidated()
    {
        var versionScenario = await CreateScenarioAsync("captured-version.actor");
        await SeedActorAsync(
            versionScenario,
            "captured-version.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence,
            actorAuthorityVersion: 2,
            membershipAuthorityVersion: 2,
            assignmentAuthorityVersion: 2);
        var versionResult = await EvaluateAsync(
            versionScenario,
            Request("captured-version.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                CapturedAuthorityVersion = 1
            });

        var ageScenario = await CreateScenarioAsync("captured-age.actor");
        await SeedActorAsync(
            ageScenario,
            "captured-age.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);
        var ageResult = await EvaluateAsync(
            ageScenario,
            Request("captured-age.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                CapturedAt = ageScenario.Clock.UtcNow.AddMinutes(-10),
                MaximumAuthorityAge = TimeSpan.FromMinutes(1)
            });

        Assert.Equal(ProductAuthorityDenyReasonCodes.AuthorityStale, versionResult.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AuthorityStale, ageResult.DenialReasonCode);
        Assert.Contains(await ReadDeniedAuthorityAuditMetadataAsync(versionScenario), metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.AuthorityStale, StringComparison.Ordinal));
        Assert.Contains(await ReadDeniedAuthorityAuditMetadataAsync(ageScenario), metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.AuthorityStale, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Support_elevation_grants_support_diagnostics_but_not_evidence_access()
    {
        var scenario = await CreateScenarioAsync("support.operator");
        await SeedActorAsync(
            scenario,
            "support.operator",
            ProductActorTypes.Support,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);
        await SeedElevationAsync(
            scenario,
            "support.operator",
            ProductAuthorityElevationTypes.Support,
            ProductAuthorityPermissions.ReadSupportDiagnostics,
            ProductAuthorityResourceTypes.SupportDiagnostics,
            ProductAuthorityResourceIds.Any);

        var diagnostics = await EvaluateAsync(
            scenario,
            new ProductAuthorityEvaluationRequest(
                "support.operator",
                ProductAuthorityPermissions.ReadSupportDiagnostics,
                ProductAuthorityResourceTypes.SupportDiagnostics,
                ProductAuthorityResourceIds.Any,
                ProductActorTypes.Support,
                ProductAuthorityCapabilities.SupportDiagnostics,
                ProductAuthorityActions.SupportDiagnosticsRead));
        var evidence = await EvaluateAsync(
            scenario,
            Request("support.operator", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ActorType = ProductActorTypes.Support
            });

        Assert.True(diagnostics.Succeeded);
        Assert.Equal(ProductAuthoritySources.SupportElevation, diagnostics.AuthoritySource);
        Assert.False(evidence.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AssignmentRequired, evidence.DenialReasonCode);
    }

    [Fact]
    public async Task Expired_and_revoked_elevations_cannot_exercise_product_authority()
    {
        var privileged = await CreateScenarioAsync("expired-privileged.actor");
        await SeedActorAsync(
            privileged,
            "expired-privileged.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);
        await SeedElevationAsync(
            privileged,
            "expired-privileged.actor",
            ProductAuthorityElevationTypes.PrivilegedAccess,
            ProductAuthorityPermissions.ManageProductPermissionAssignments,
            ProductAuthorityResourceTypes.Any,
            ProductAuthorityResourceIds.Any,
            expiresAt: privileged.Clock.UtcNow.AddMinutes(-1));
        var expiredPrivileged = await EvaluateAsync(
            privileged,
            new ProductAuthorityEvaluationRequest(
                "expired-privileged.actor",
                ProductAuthorityPermissions.ManageProductPermissionAssignments,
                ProductAuthorityResourceTypes.Any,
                ProductAuthorityResourceIds.Any,
                ProductActorTypes.Human,
                ProductAuthorityCapabilities.EvidenceReviewDecision,
                ProductAuthorityActions.ReviewAssignmentManage));

        var support = await CreateScenarioAsync("expired-support.actor");
        await SeedActorAsync(
            support,
            "expired-support.actor",
            ProductActorTypes.Support,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);
        await SeedElevationAsync(
            support,
            "expired-support.actor",
            ProductAuthorityElevationTypes.Support,
            ProductAuthorityPermissions.ReadSupportDiagnostics,
            ProductAuthorityResourceTypes.SupportDiagnostics,
            ProductAuthorityResourceIds.Any,
            expiresAt: support.Clock.UtcNow.AddMinutes(-1));
        var expiredSupport = await EvaluateAsync(
            support,
            new ProductAuthorityEvaluationRequest(
                "expired-support.actor",
                ProductAuthorityPermissions.ReadSupportDiagnostics,
                ProductAuthorityResourceTypes.SupportDiagnostics,
                ProductAuthorityResourceIds.Any,
                ProductActorTypes.Support,
                ProductAuthorityCapabilities.SupportDiagnostics,
                ProductAuthorityActions.SupportDiagnosticsRead));

        var revoked = await CreateScenarioAsync("revoked-elevation.actor");
        await SeedActorAsync(
            revoked,
            "revoked-elevation.actor",
            ProductActorTypes.Support,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);
        await SeedElevationAsync(
            revoked,
            "revoked-elevation.actor",
            ProductAuthorityElevationTypes.Support,
            ProductAuthorityPermissions.ReadSupportDiagnostics,
            ProductAuthorityResourceTypes.SupportDiagnostics,
            ProductAuthorityResourceIds.Any,
            elevationState: ProductAuthorityElevationStates.Revoked);
        var revokedElevation = await EvaluateAsync(
            revoked,
            new ProductAuthorityEvaluationRequest(
                "revoked-elevation.actor",
                ProductAuthorityPermissions.ReadSupportDiagnostics,
                ProductAuthorityResourceTypes.SupportDiagnostics,
                ProductAuthorityResourceIds.Any,
                ProductActorTypes.Support,
                ProductAuthorityCapabilities.SupportDiagnostics,
                ProductAuthorityActions.SupportDiagnosticsRead));

        Assert.Equal(ProductAuthorityDenyReasonCodes.PermissionDenied, expiredPrivileged.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.PermissionDenied, expiredSupport.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.PermissionDenied, revokedElevation.DenialReasonCode);
    }

    [Fact]
    public async Task Break_glass_elevation_requires_validated_external_strong_authentication()
    {
        var scenario = await CreateScenarioAsync("emergency.operator");
        await SeedActorAsync(
            scenario,
            "emergency.operator",
            ProductActorTypes.Emergency,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);
        await SeedElevationAsync(
            scenario,
            "emergency.operator",
            ProductAuthorityElevationTypes.BreakGlass,
            ProductAuthorityPermissions.ActivateBreakGlass,
            ProductAuthorityResourceTypes.Any,
            ProductAuthorityResourceIds.Any,
            requiresStrongAuthentication: true);

        var denied = await EvaluateAsync(
            scenario,
            BreakGlassRequest("emergency.operator", strongAuthentication: false));
        var allowed = await EvaluateAsync(
            scenario,
            BreakGlassRequest("emergency.operator", strongAuthentication: true));

        Assert.False(denied.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AuthorityUnavailable, denied.DenialReasonCode);
        Assert.True(allowed.Succeeded);
        Assert.Equal(ProductAuthoritySources.BreakGlassElevation, allowed.AuthoritySource);
    }

    [Fact]
    public async Task Classification_lifecycle_and_commercial_entitlement_restrictions_are_enforced()
    {
        var scenario = await CreateScenarioAsync("restricted.reader");
        await SeedActorAsync(
            scenario,
            "restricted.reader",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);

        var classificationDenied = await EvaluateAsync(
            scenario,
            Request("restricted.reader", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                Classification = "Restricted"
            });
        var lifecycleDenied = await EvaluateAsync(
            scenario,
            Request("restricted.reader", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                LifecycleState = "Archived"
            });
        var entitlementDenied = await EvaluateAsync(
            scenario,
            Request("restricted.reader", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ProductCapability = ProductAuthorityCapabilities.EvidenceContent,
                RequireEntitlement = true
            });

        Assert.Equal(ProductAuthorityDenyReasonCodes.ClassificationRestricted, classificationDenied.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.LifecycleRestricted, lifecycleDenied.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.CapabilityNotEntitled, entitlementDenied.DenialReasonCode);
    }

    [Fact]
    public async Task Commercial_entitlement_does_not_replace_product_permission_assignment()
    {
        var scenario = await CreateScenarioAsync("entitled.user");
        await SeedActorAsync(
            scenario,
            "entitled.user",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);
        await SeedEntitlementAsync(scenario, ProductAuthorityCapabilities.EvidenceContent);

        var entitlementOnly = await EvaluateAsync(
            scenario,
            Request("entitled.user", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ProductCapability = ProductAuthorityCapabilities.EvidenceContent,
                RequireEntitlement = true
            });

        await SeedPermissionAsync(
            scenario,
            "entitled.user",
            ProductAuthorityPermissions.ReadEvidence,
            ProductAuthorityResourceTypes.Evidence,
            "evidence-1");
        var entitlementPlusPermission = await EvaluateAsync(
            scenario,
            Request("entitled.user", ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ProductCapability = ProductAuthorityCapabilities.EvidenceContent,
                RequireEntitlement = true
            });

        Assert.Equal(ProductAuthorityDenyReasonCodes.AssignmentRequired, entitlementOnly.DenialReasonCode);
        Assert.True(entitlementPlusPermission.Succeeded);
    }

    [Fact]
    public async Task Separation_of_duty_matrix_denies_same_actor_decision_application()
    {
        var scenario = await CreateScenarioAsync("decision.actor");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var denied = await service.EvaluateSeparationOfDutyAsync(
            new ProductSeparationOfDutyRequest("decision.actor", "decision.actor", "Evidence review requester cannot apply final decision"),
            CancellationToken.None);
        var allowed = await service.EvaluateSeparationOfDutyAsync(
            new ProductSeparationOfDutyRequest("decision.actor", "review.requester", "Evidence review requester cannot apply final decision"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var audits = await verificationDbContext.AuditEntries
            .Where(audit => audit.Action == "ProductAuthority.SeparationOfDutiesDenied")
            .Select(audit => audit.MetadataJson)
            .ToArrayAsync();

        Assert.Equal(ProductAuthorityDenyReasonCodes.SeparationOfDutiesViolation, denied.DenialReasonCode);
        Assert.True(allowed.Succeeded);
        Assert.Contains(audits, metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.SeparationOfDutiesViolation, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authenticated_context_cannot_evaluate_as_another_product_actor()
    {
        var scenario = await CreateScenarioAsync("actual.actor");
        await SeedActorAsync(
            scenario,
            "target.actor",
            ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true,
            permissionKey: ProductAuthorityPermissions.ReadEvidence);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await service.EvaluatePermissionAsync(
            Request("target.actor", ProductAuthorityPermissions.ReadEvidence, "evidence-1"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.IdentityInvalid, result.DenialReasonCode);
    }

    private async Task<SecurityScenario> CreateScenarioAsync(string principalSubject)
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId, principalSubject);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = $"Identity Security Tenant {Guid.NewGuid():N}",
            ExternalAuthority = $"identity-security-{Guid.NewGuid():N}",
            LifecycleState = "Active",
            CreatedBy = "identity-security-test",
            CreatedAt = clock.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Identity Security Workspace {Guid.NewGuid():N}",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "identity-security-test",
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return new SecurityScenario(tenantId, workspaceId, accessor, clock);
    }

    private async Task SeedActorAsync(
        SecurityScenario scenario,
        string subject,
        string actorType,
        string actorState,
        bool seedTenantMembership,
        bool seedWorkspaceMembership,
        string? permissionKey = null,
        string resourceType = ProductAuthorityResourceTypes.Evidence,
        string resourceId = "evidence-1",
        string assignmentState = ProductPermissionAssignmentStates.Active,
        long actorAuthorityVersion = 1,
        long? membershipAuthorityVersion = null,
        long? assignmentAuthorityVersion = null,
        string tenantMembershipState = ProductMembershipStates.Active,
        string workspaceMembershipState = ProductMembershipStates.Active)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var actor = new ProductActor
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            Subject = subject,
            DisplayName = subject,
            ActorType = actorType,
            State = actorState,
            AuthorityVersion = actorAuthorityVersion,
            AuthorityChangedAt = scenario.Clock.UtcNow,
            DisabledAt = actorState.Equals(ProductActorStates.Disabled, StringComparison.Ordinal) ? scenario.Clock.UtcNow : null,
            CreatedBy = "identity-security-test",
            CreatedAt = scenario.Clock.UtcNow
        };
        dbContext.ProductActors.Add(actor);

        if (seedTenantMembership)
        {
            dbContext.ProductTenantMemberships.Add(new ProductTenantMembership
            {
                TenantId = scenario.TenantId,
                ActorId = actor.Id,
                ActorSubject = subject,
                State = tenantMembershipState,
                AuthorityVersion = membershipAuthorityVersion ?? actor.AuthorityVersion,
                GrantedBy = "identity-security-test",
                GrantedAt = scenario.Clock.UtcNow,
                RevokedAt = tenantMembershipState.Equals(ProductMembershipStates.Revoked, StringComparison.Ordinal)
                    ? scenario.Clock.UtcNow
                    : null,
                RevokedBy = tenantMembershipState.Equals(ProductMembershipStates.Revoked, StringComparison.Ordinal)
                    ? "identity-security-test"
                    : null,
                IdempotencyKey = $"tenant-membership-{Guid.NewGuid():N}",
                RequestHash = Sha256(Encoding.UTF8.GetBytes($"{subject}|tenant"))
            });
        }

        if (seedWorkspaceMembership)
        {
            dbContext.ProductWorkspaceMemberships.Add(new ProductWorkspaceMembership
            {
                TenantId = scenario.TenantId,
                WorkspaceId = scenario.WorkspaceId,
                ActorId = actor.Id,
                ActorSubject = subject,
                State = workspaceMembershipState,
                AuthorityVersion = membershipAuthorityVersion ?? actor.AuthorityVersion,
                GrantedBy = "identity-security-test",
                GrantedAt = scenario.Clock.UtcNow,
                RevokedAt = workspaceMembershipState.Equals(ProductMembershipStates.Revoked, StringComparison.Ordinal)
                    ? scenario.Clock.UtcNow
                    : null,
                RevokedBy = workspaceMembershipState.Equals(ProductMembershipStates.Revoked, StringComparison.Ordinal)
                    ? "identity-security-test"
                    : null,
                IdempotencyKey = $"workspace-membership-{Guid.NewGuid():N}",
                RequestHash = Sha256(Encoding.UTF8.GetBytes($"{subject}|workspace"))
            });
        }

        if (permissionKey is not null)
        {
            dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
            {
                TenantId = scenario.TenantId,
                WorkspaceId = scenario.WorkspaceId,
                ActorId = actor.Id,
                ActorSubject = subject,
                PermissionKey = permissionKey,
                ResourceType = resourceType,
                ResourceId = resourceId,
                State = assignmentState,
                AuthorityVersion = assignmentAuthorityVersion ?? actor.AuthorityVersion,
                AssignedBy = "identity-security-test",
                AssignedAt = scenario.Clock.UtcNow,
                EffectiveFrom = scenario.Clock.UtcNow.AddMinutes(-1),
                RevokedAt = assignmentState.Equals(ProductPermissionAssignmentStates.Revoked, StringComparison.Ordinal)
                    ? scenario.Clock.UtcNow
                    : null,
                RevokedBy = assignmentState.Equals(ProductPermissionAssignmentStates.Revoked, StringComparison.Ordinal)
                    ? "identity-security-test"
                    : null,
                IdempotencyKey = $"permission-assignment-{Guid.NewGuid():N}",
                RequestHash = Sha256(Encoding.UTF8.GetBytes($"{subject}|{permissionKey}|{resourceType}|{resourceId}"))
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedPermissionAsync(
        SecurityScenario scenario,
        string subject,
        string permissionKey,
        string resourceType,
        string resourceId)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var actor = await dbContext.ProductActors.SingleAsync(item => item.Subject == subject);
        dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            ActorId = actor.Id,
            ActorSubject = subject,
            PermissionKey = permissionKey,
            ResourceType = resourceType,
            ResourceId = resourceId,
            State = ProductPermissionAssignmentStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            AssignedBy = "identity-security-test",
            AssignedAt = scenario.Clock.UtcNow,
            EffectiveFrom = scenario.Clock.UtcNow.AddMinutes(-1),
            IdempotencyKey = $"permission-assignment-{Guid.NewGuid():N}",
            RequestHash = Sha256(Encoding.UTF8.GetBytes($"{subject}|{permissionKey}|{resourceType}|{resourceId}"))
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedElevationAsync(
        SecurityScenario scenario,
        string subject,
        string elevationType,
        string permissionKey,
        string resourceType,
        string resourceId,
        bool requiresStrongAuthentication = false,
        string elevationState = ProductAuthorityElevationStates.Active,
        DateTimeOffset? expiresAt = null)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var actor = await dbContext.ProductActors.SingleAsync(item => item.Subject == subject);
        dbContext.ProductAuthorityElevations.Add(new ProductAuthorityElevation
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            ActorId = actor.Id,
            ActorSubject = subject,
            ElevationType = elevationType,
            RequestedCapability = ProductAuthorityCapabilities.SupportDiagnostics,
            PermissionKey = permissionKey,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Reason = "Security control test elevation.",
            State = elevationState,
            AuthorityVersion = actor.AuthorityVersion,
            RequestedBy = "identity-security-test",
            RequestedAt = scenario.Clock.UtcNow.AddMinutes(-5),
            ApprovedBy = "security-approver",
            ApprovedAt = scenario.Clock.UtcNow.AddMinutes(-4),
            EffectiveFrom = (expiresAt ?? scenario.Clock.UtcNow.AddMinutes(30)) <= scenario.Clock.UtcNow
                ? scenario.Clock.UtcNow.AddMinutes(-30)
                : scenario.Clock.UtcNow.AddMinutes(-3),
            ExpiresAt = expiresAt ?? scenario.Clock.UtcNow.AddMinutes(30),
            RevokedAt = elevationState.Equals(ProductAuthorityElevationStates.Revoked, StringComparison.Ordinal)
                ? scenario.Clock.UtcNow
                : null,
            RevokedBy = elevationState.Equals(ProductAuthorityElevationStates.Revoked, StringComparison.Ordinal)
                ? "identity-security-test"
                : null,
            RequiresExternalStrongAuthentication = requiresStrongAuthentication,
            CorrelationId = scenario.Accessor.Current!.CorrelationId
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedEntitlementAsync(SecurityScenario scenario, string capability)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        dbContext.CapabilityEntitlements.Add(new CapabilityEntitlement
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            CapabilityKey = capability,
            PlanKey = "restricted-pilot-disabled",
            EffectiveFrom = scenario.Clock.UtcNow.AddMinutes(-1),
            EffectiveTo = scenario.Clock.UtcNow.AddDays(1)
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task<ProductAuthorityEvaluationResult> EvaluateAsync(
        SecurityScenario scenario,
        ProductAuthorityEvaluationRequest request)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);
        var result = await service.EvaluatePermissionAsync(request, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        return result;
    }

    private async Task<string[]> ReadDeniedAuthorityAuditMetadataAsync(SecurityScenario scenario)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        return await dbContext.AuditEntries
            .Where(audit => audit.Action == "ProductAuthority.Denied" && audit.Outcome == "Deny")
            .Select(audit => audit.MetadataJson)
            .ToArrayAsync();
    }

    private static ProductAuthorityEvaluationRequest Request(
        string actor,
        string permission,
        string resourceId)
    {
        return new ProductAuthorityEvaluationRequest(
            actor,
            permission,
            ProductAuthorityResourceTypes.Evidence,
            resourceId,
            ProductActorTypes.Human,
            ProductCapability: ProductAuthorityCapabilities.EvidenceContent,
            Action: ProductAuthorityActions.EvidenceRead);
    }

    private static ProductAuthorityEvaluationRequest ReviewRequest(
        string actor,
        string permission,
        string reviewId)
    {
        return new ProductAuthorityEvaluationRequest(
            actor,
            permission,
            ProductAuthorityResourceTypes.EvidenceReview,
            reviewId,
            ProductActorTypes.Human,
            ProductCapability: ProductAuthorityCapabilities.EvidenceReviewDecision,
            Action: ProductAuthorityActions.DecisionApply);
    }

    private static ProductAuthorityEvaluationRequest BreakGlassRequest(
        string actor,
        bool strongAuthentication)
    {
        return new ProductAuthorityEvaluationRequest(
            actor,
            ProductAuthorityPermissions.ActivateBreakGlass,
            ProductAuthorityResourceTypes.Any,
            ProductAuthorityResourceIds.Any,
            ProductActorTypes.Emergency,
            ProductAuthorityCapabilities.SupportDiagnostics,
            ProductAuthorityActions.SupportDiagnosticsRead,
            ExternalStrongAuthenticationSatisfied: strongAuthentication);
    }

    private ProductAuthorityService CreateService(
        DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        IClock clock)
    {
        return new ProductAuthorityService(
            dbContext,
            accessor,
            clock,
            new ProductAuthorityAuditStore(fixture.CreateDbContextOptions()));
    }

    private static RequestContextAccessor CreateRequestContext(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string actor)
    {
        return new RequestContextAccessor
        {
            Current = new RequestContext(
                tenantId,
                workspaceId,
                new PrincipalSubject(actor),
                $"corr-{Guid.NewGuid():N}")
        };
    }

    private static string Sha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed record SecurityScenario(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        RequestContextAccessor Accessor,
        MutableClock Clock);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}