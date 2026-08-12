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
        await dbContext.SaveChangesAsync();
        var audits = await dbContext.AuditEntries
            .Where(audit => audit.Action == "ProductAuthority.Denied" && audit.Outcome == "Deny")
            .Select(audit => audit.MetadataJson)
            .ToArrayAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.IdentityInvalid, result.DenialReasonCode);
        Assert.Contains(audits, metadata => metadata.Contains(ProductAuthorityDenyReasonCodes.IdentityInvalid, StringComparison.Ordinal));
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

    [Theory]
    [InlineData("platform.admin", "PlatformAdmin")]
    [InlineData("commercial.admin", "CommercialAdmin")]
    [InlineData("billing.admin", "BillingAdmin")]
    [InlineData("support.operator", "SupportOperator")]
    public async Task Product_role_labels_do_not_grant_evidence_authority_without_explicit_permission(
        string actorSubject,
        string role)
    {
        var scenario = await CreateScenarioAsync(actorSubject);
        await SeedActorAsync(
            scenario,
            actorSubject,
            actorSubject.StartsWith("support.", StringComparison.Ordinal) ? ProductActorTypes.Support : ProductActorTypes.Human,
            ProductActorStates.Active,
            seedTenantMembership: true,
            seedWorkspaceMembership: true);

        var result = await EvaluateAsync(
            scenario,
            Request(actorSubject, ProductAuthorityPermissions.ReadEvidence, "evidence-1") with
            {
                ActorType = actorSubject.StartsWith("support.", StringComparison.Ordinal) ? ProductActorTypes.Support : ProductActorTypes.Human,
                ProductRole = role
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AssignmentRequired, result.DenialReasonCode);
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

        Assert.Equal(ProductAuthorityDenyReasonCodes.PermissionDenied, revoked.DenialReasonCode);
        Assert.Equal(ProductAuthorityDenyReasonCodes.AuthorityStale, stale.DenialReasonCode);
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
        var audits = await dbContext.AuditEntries
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
        long? assignmentAuthorityVersion = null)
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
                State = ProductMembershipStates.Active,
                AuthorityVersion = membershipAuthorityVersion ?? actor.AuthorityVersion,
                GrantedBy = "identity-security-test",
                GrantedAt = scenario.Clock.UtcNow,
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
                State = ProductMembershipStates.Active,
                AuthorityVersion = membershipAuthorityVersion ?? actor.AuthorityVersion,
                GrantedBy = "identity-security-test",
                GrantedAt = scenario.Clock.UtcNow,
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
        bool requiresStrongAuthentication = false)
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
            State = ProductAuthorityElevationStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            RequestedBy = "identity-security-test",
            RequestedAt = scenario.Clock.UtcNow.AddMinutes(-5),
            ApprovedBy = "security-approver",
            ApprovedAt = scenario.Clock.UtcNow.AddMinutes(-4),
            EffectiveFrom = scenario.Clock.UtcNow.AddMinutes(-3),
            ExpiresAt = scenario.Clock.UtcNow.AddMinutes(30),
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

    private static ProductAuthorityService CreateService(
        DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        IClock clock)
    {
        return new ProductAuthorityService(dbContext, accessor, clock);
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