using System.Security.Cryptography;
using System.Text;

using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Modules.Retention;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.Runtime.Persistence.Retention;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Persistence.Tests;

public sealed class RetentionGovernanceServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Retention_policy_requires_explicit_permission_not_role_label()
    {
        var scenario = await CreateScenarioAsync("retention-role-only");
        await SeedActorAsync(scenario, "retention-role-only");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.DefineRetentionPolicyAsync(
            new RetentionPolicyCommand("role-only-policy", "role label is not authority", 365, true, "ret-role-only-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        Assert.Equal(0, await verificationDbContext.RetentionPolicies.CountAsync());
        Assert.Equal(0, await verificationDbContext.OutboxMessages.CountAsync(message => message.OwningModule == "Retention"));
    }

    [Fact]
    public async Task Retention_policy_definition_is_authorized_idempotent_audited_and_outboxed()
    {
        var scenario = await CreateScenarioAsync("retention-admin");
        await SeedActorAsync(
            scenario,
            "retention-admin",
            ProductAuthorityPermissions.ManageRetentionPolicy,
            ProductAuthorityResourceTypes.GovernanceRetention,
            "retain-seven-years");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);
        var command = new RetentionPolicyCommand("retain-seven-years", "Seven year retention", 2555, true, "ret-policy-001");

        var created = await service.DefineRetentionPolicyAsync(command, CancellationToken.None);
        var replay = await service.DefineRetentionPolicyAsync(command, CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var audits = await verificationDbContext.AuditEntries
            .Select(audit => audit.Action)
            .ToArrayAsync();

        Assert.False(created.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(created.PolicyId, replay.PolicyId);
        Assert.Equal(1, await verificationDbContext.RetentionPolicies.CountAsync());
        Assert.Contains("ProductAuthority.Evaluated", audits);
        Assert.Contains("Retention.PolicyDefined", audits);
        Assert.Equal(1, await verificationDbContext.OutboxMessages.CountAsync(message => message.MessageType == "RetentionPolicyDefined"));
    }

    [Fact]
    public async Task Legal_hold_requires_explicit_permission_and_records_evidence_lineage()
    {
        var scenario = await CreateScenarioAsync("legal-hold-admin");
        var evidence = await SeedEvidenceAsync(scenario);
        await SeedActorAsync(
            scenario,
            "legal-hold-admin",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(evidence.Id, "Legal preservation notice", "legal-hold-001"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var persistedEvidence = await verificationDbContext.EvidenceRecords.SingleAsync(item => item.Id == evidence.Id);
        var audits = await verificationDbContext.AuditEntries
            .Select(audit => audit.Action)
            .ToArrayAsync();

        Assert.False(result.IdempotentReplay);
        Assert.True(result.EvidenceUnderLegalHold);
        Assert.True(persistedEvidence.IsUnderLegalHold);
        Assert.Equal(1, await verificationDbContext.LegalHolds.CountAsync());
        Assert.Contains("ProductAuthority.Evaluated", audits);
        Assert.Contains("Retention.LegalHoldPlaced", audits);
        Assert.Equal(1, await verificationDbContext.LineageRelationships.CountAsync(relationship => relationship.RelationshipType == "LegalHoldPlaced"));
        Assert.Equal(1, await verificationDbContext.OutboxMessages.CountAsync(message => message.MessageType == "LegalHoldPlaced"));
    }

    [Fact]
    public async Task Legal_hold_cannot_cross_tenant_or_workspace_boundary()
    {
        var protectedScenario = await CreateScenarioAsync("protected-legal-hold-admin");
        var protectedEvidence = await SeedEvidenceAsync(protectedScenario);
        var attackerScenario = await CreateScenarioAsync("attacker-legal-hold-admin");
        await SeedActorAsync(
            attackerScenario,
            "attacker-legal-hold-admin",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            protectedEvidence.Id.ToString());
        await using var attackerDbContext = fixture.CreateDbContext(attackerScenario.Accessor);
        var attackerService = CreateService(attackerDbContext, attackerScenario.Accessor, attackerScenario.Clock);

        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerService.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(protectedEvidence.Id, "Cross tenant attempt", "legal-hold-cross-tenant-001"),
            CancellationToken.None));

        await using var protectedVerificationDbContext = fixture.CreateDbContext(protectedScenario.Accessor);
        await using var attackerVerificationDbContext = fixture.CreateDbContext(attackerScenario.Accessor);
        Assert.Equal(0, await protectedVerificationDbContext.LegalHolds.CountAsync());
        Assert.Equal(0, await attackerVerificationDbContext.LegalHolds.CountAsync());
    }

    [Fact]
    public async Task Active_legal_hold_prevents_deletion_eligibility_even_after_retention_expiry()
    {
        var scenario = await CreateScenarioAsync("retention-active-hold-admin");
        await SeedRetentionPolicyAsync(scenario, "expired-policy", retainForDays: 1);
        var evidence = await SeedEvidenceAsync(
            scenario,
            retentionPolicyKey: "expired-policy",
            capturedAt: scenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            scenario,
            "retention-active-hold-admin",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            evidence.Id.ToString());
        await SeedPermissionAssignmentAsync(
            scenario,
            "retention-active-hold-admin",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        await service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(evidence.Id, "Preservation overrides retention expiry", "active-hold-place-001"),
            CancellationToken.None);
        var result = await service.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, "active-hold-eval-001"),
            CancellationToken.None);
        var replay = await service.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, "active-hold-eval-001"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var persistedEvidence = await verificationDbContext.EvidenceRecords.SingleAsync(item => item.Id == evidence.Id);

        Assert.False(result.IsEligible);
        Assert.Equal(DeletionEligibilityReasonCodes.ActiveLegalHold, result.ReasonCode);
        Assert.True(result.HasActiveLegalHold);
        Assert.False(result.EvidencePhysicallyDeleted);
        Assert.Equal(result.EvaluationId, replay.EvaluationId);
        Assert.True(replay.IdempotentReplay);
        Assert.True(persistedEvidence.IsUnderLegalHold);
        Assert.NotEqual("Deleted", persistedEvidence.LifecycleState);
        Assert.Equal(1, await verificationDbContext.EvidenceRecords.CountAsync(item => item.Id == evidence.Id));
        Assert.Equal(1, await verificationDbContext.EvidenceVersions.CountAsync(version => version.EvidenceId == evidence.Id));
        Assert.Equal(1, await verificationDbContext.DeletionEligibilityEvaluations.CountAsync());
        Assert.Contains(
            "Retention.DeletionEligibilityDenied",
            await verificationDbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync());
    }

    [Fact]
    public async Task Legal_hold_release_requires_independent_approval_and_does_not_delete_evidence()
    {
        var scenario = await CreateScenarioAsync("legal-hold-placer");
        await SeedRetentionPolicyAsync(scenario, "default", retainForDays: 1);
        var evidence = await SeedEvidenceAsync(scenario, capturedAt: scenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            scenario,
            "legal-hold-placer",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);
        var hold = await service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(evidence.Id, "Preserve for release workflow", "release-hold-place-001"),
            CancellationToken.None);

        scenario.SetActor("release-requester");
        await SeedActorAsync(
            scenario,
            "release-requester",
            ProductAuthorityPermissions.RequestLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        await SeedPermissionAssignmentAsync(
            scenario,
            "release-requester",
            ProductAuthorityPermissions.ApproveLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        var request = await service.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(evidence.Id, hold.LegalHoldId, "Hold no longer required", "release-request-001"),
            CancellationToken.None);
        var replayedRequest = await service.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(evidence.Id, hold.LegalHoldId, "Hold no longer required", "release-request-001"),
            CancellationToken.None);

        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "Self approval is forbidden", "release-self-approval-001"),
            CancellationToken.None));

        scenario.SetActor("release-approver");
        await SeedActorAsync(
            scenario,
            "release-approver",
            ProductAuthorityPermissions.ApproveLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        var approved = await service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "Independent approval", "release-approval-001"),
            CancellationToken.None);
        var replayedApproval = await service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "Independent approval", "release-approval-001"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var persistedHold = await verificationDbContext.LegalHolds.SingleAsync(item => item.Id == hold.LegalHoldId);
        var persistedEvidence = await verificationDbContext.EvidenceRecords.SingleAsync(item => item.Id == evidence.Id);
        var audits = await verificationDbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync();

        Assert.Equal(request.ReleaseRequestId, replayedRequest.ReleaseRequestId);
        Assert.True(replayedRequest.IdempotentReplay);
        Assert.Equal(LegalHoldReleaseStates.Approved, approved.State);
        Assert.Equal(approved.ReleaseRequestId, replayedApproval.ReleaseRequestId);
        Assert.True(replayedApproval.IdempotentReplay);
        Assert.False(approved.EvidenceUnderLegalHold);
        Assert.False(approved.EvidencePhysicallyDeleted);
        Assert.NotNull(persistedHold.ReleasedAt);
        Assert.Equal("release-approver", persistedHold.ReleasedBy);
        Assert.False(persistedEvidence.IsUnderLegalHold);
        Assert.NotEqual("Deleted", persistedEvidence.LifecycleState);
        Assert.Equal(1, await verificationDbContext.EvidenceRecords.CountAsync(item => item.Id == evidence.Id));
        Assert.Equal(1, await verificationDbContext.EvidenceVersions.CountAsync(version => version.EvidenceId == evidence.Id));
        Assert.Contains("Retention.LegalHoldReleaseRequested", audits);
        Assert.Contains("Retention.LegalHoldReleaseDenied", audits);
        Assert.Contains("Retention.LegalHoldReleaseApproved", audits);
        Assert.Equal(1, await verificationDbContext.LineageRelationships.CountAsync(relationship => relationship.RelationshipType == "LegalHoldReleased"));
        Assert.Equal(1, await verificationDbContext.OutboxMessages.CountAsync(message => message.MessageType == "LegalHoldReleased"));
    }

    [Fact]
    public async Task Unauthorised_stale_and_revoked_authority_cannot_release_hold()
    {
        var scenario = await CreateScenarioAsync("hold-owner");
        var evidence = await SeedEvidenceAsync(scenario);
        await SeedActorAsync(
            scenario,
            "hold-owner",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);
        var hold = await service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(evidence.Id, "Hold for authority tests", "authority-hold-place-001"),
            CancellationToken.None);

        scenario.SetActor("release-requester");
        await SeedActorAsync(
            scenario,
            "release-requester",
            ProductAuthorityPermissions.RequestLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        var request = await service.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(evidence.Id, hold.LegalHoldId, "Release request", "authority-release-request-001"),
            CancellationToken.None);

        scenario.SetActor("no-release-authority");
        await SeedActorAsync(scenario, "no-release-authority");
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "No authority", "no-release-authority-001"),
            CancellationToken.None));

        scenario.SetActor("stale-release-approver");
        await SeedActorAsync(scenario, "stale-release-approver", actorAuthorityVersion: 2);
        await SeedPermissionAssignmentAsync(
            scenario,
            "stale-release-approver",
            ProductAuthorityPermissions.ApproveLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"),
            authorityVersion: 1);
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "Stale authority", "stale-release-approval-001"),
            CancellationToken.None));

        scenario.SetActor("revoked-release-approver");
        await SeedActorAsync(scenario, "revoked-release-approver");
        await SeedPermissionAssignmentAsync(
            scenario,
            "revoked-release-approver",
            ProductAuthorityPermissions.ApproveLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"),
            state: ProductPermissionAssignmentStates.Revoked);
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "Revoked authority", "revoked-release-approval-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var persistedHold = await verificationDbContext.LegalHolds.SingleAsync(item => item.Id == hold.LegalHoldId);
        var persistedRequest = await verificationDbContext.LegalHoldReleaseRequests.SingleAsync(item => item.Id == request.ReleaseRequestId);

        Assert.Null(persistedHold.ReleasedAt);
        Assert.Equal(LegalHoldReleaseStates.Pending, persistedRequest.State);
    }

    [Fact]
    public async Task Cross_tenant_and_cross_workspace_context_cannot_release_or_evaluate()
    {
        var protectedScenario = await CreateScenarioAsync("protected-retention-admin");
        await SeedRetentionPolicyAsync(protectedScenario, "expired-policy", retainForDays: 1);
        var protectedEvidence = await SeedEvidenceAsync(
            protectedScenario,
            retentionPolicyKey: "expired-policy",
            capturedAt: protectedScenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            protectedScenario,
            "protected-retention-admin",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            protectedEvidence.Id.ToString());
        await using var protectedDbContext = fixture.CreateDbContext(protectedScenario.Accessor);
        var protectedService = CreateService(protectedDbContext, protectedScenario.Accessor, protectedScenario.Clock);
        var hold = await protectedService.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(protectedEvidence.Id, "Protected hold", "protected-hold-place-001"),
            CancellationToken.None);

        var attackerTenantScenario = await CreateScenarioAsync("attacker-tenant-admin");
        await SeedActorAsync(
            attackerTenantScenario,
            "attacker-tenant-admin",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            protectedEvidence.Id.ToString());
        await SeedPermissionAssignmentAsync(
            attackerTenantScenario,
            "attacker-tenant-admin",
            ProductAuthorityPermissions.RequestLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        await using var attackerTenantDbContext = fixture.CreateDbContext(attackerTenantScenario.Accessor);
        var attackerTenantService = CreateService(attackerTenantDbContext, attackerTenantScenario.Accessor, attackerTenantScenario.Clock);

        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerTenantService.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(protectedEvidence.Id, "cross-tenant-eval-001"),
            CancellationToken.None));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerTenantService.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(protectedEvidence.Id, hold.LegalHoldId, "Cross tenant", "cross-tenant-release-001"),
            CancellationToken.None));

        var attackerWorkspaceScenario = await CreateWorkspaceScenarioAsync(protectedScenario, "attacker-workspace-admin");
        await SeedActorAsync(
            attackerWorkspaceScenario,
            "attacker-workspace-admin",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            protectedEvidence.Id.ToString());
        await SeedPermissionAssignmentAsync(
            attackerWorkspaceScenario,
            "attacker-workspace-admin",
            ProductAuthorityPermissions.RequestLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        await using var attackerWorkspaceDbContext = fixture.CreateDbContext(attackerWorkspaceScenario.Accessor);
        var attackerWorkspaceService = CreateService(attackerWorkspaceDbContext, attackerWorkspaceScenario.Accessor, attackerWorkspaceScenario.Clock);

        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerWorkspaceService.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(protectedEvidence.Id, "cross-workspace-eval-001"),
            CancellationToken.None));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerWorkspaceService.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(protectedEvidence.Id, hold.LegalHoldId, "Cross workspace", "cross-workspace-release-001"),
            CancellationToken.None));

        await using var protectedVerificationDbContext = fixture.CreateDbContext(protectedScenario.Accessor);
        Assert.Equal(0, await protectedVerificationDbContext.LegalHoldReleaseRequests.CountAsync());
        Assert.Equal(0, await protectedVerificationDbContext.DeletionEligibilityEvaluations.CountAsync());
    }

    [Fact]
    public async Task Retention_expiry_without_active_hold_can_become_deletion_eligible_without_deleting_evidence()
    {
        var scenario = await CreateScenarioAsync("eligibility-retention-admin");
        await SeedRetentionPolicyAsync(scenario, "expired-policy", retainForDays: 1);
        var evidence = await SeedEvidenceAsync(
            scenario,
            retentionPolicyKey: "expired-policy",
            capturedAt: scenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            scenario,
            "eligibility-retention-admin",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await service.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, "eligible-eval-001"),
            CancellationToken.None);
        var replay = await service.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, "eligible-eval-001"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var persistedEvidence = await verificationDbContext.EvidenceRecords.SingleAsync(item => item.Id == evidence.Id);

        Assert.True(result.IsEligible);
        Assert.Equal(DeletionEligibilityReasonCodes.Eligible, result.ReasonCode);
        Assert.False(result.HasActiveLegalHold);
        Assert.False(result.EvidencePhysicallyDeleted);
        Assert.Equal(result.EvaluationId, replay.EvaluationId);
        Assert.True(replay.IdempotentReplay);
        Assert.NotEqual("Deleted", persistedEvidence.LifecycleState);
        Assert.Equal(1, await verificationDbContext.EvidenceRecords.CountAsync(item => item.Id == evidence.Id));
        Assert.Equal(1, await verificationDbContext.EvidenceVersions.CountAsync(version => version.EvidenceId == evidence.Id));
        Assert.Contains(
            "Retention.DeletionEligibilityDetermined",
            await verificationDbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync());
        Assert.Equal(1, await verificationDbContext.LineageRelationships.CountAsync(relationship => relationship.RelationshipType == "DeletionEligibilityDetermined"));
    }

    [Fact]
    public async Task Retention_not_expired_is_not_deletion_eligible()
    {
        var scenario = await CreateScenarioAsync("not-expired-admin");
        await SeedRetentionPolicyAsync(scenario, "not-expired-policy", retainForDays: 365);
        var evidence = await SeedEvidenceAsync(
            scenario,
            retentionPolicyKey: "not-expired-policy",
            capturedAt: scenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            scenario,
            "not-expired-admin",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await service.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, "not-expired-eval-001"),
            CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal(DeletionEligibilityReasonCodes.RetentionNotExpired, result.ReasonCode);
        Assert.False(result.EvidencePhysicallyDeleted);
    }

    [Fact]
    public async Task Technical_or_administrative_privilege_alone_cannot_release_hold_or_evaluate_deletion()
    {
        var scenario = await CreateScenarioAsync("technical-admin");
        await SeedRetentionPolicyAsync(scenario, "expired-policy", retainForDays: 1);
        var evidence = await SeedEvidenceAsync(
            scenario,
            retentionPolicyKey: "expired-policy",
            capturedAt: scenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            scenario,
            "technical-admin",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);
        var hold = await service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(evidence.Id, "Technical privilege test", "technical-hold-place-001"),
            CancellationToken.None);

        scenario.SetActor("platform-operator");
        await SeedActorAsync(
            scenario,
            "platform-operator",
            ProductAuthorityPermissions.ActivateBreakGlass,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));

        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(evidence.Id, hold.LegalHoldId, "Technical privilege is not release authority", "technical-release-001"),
            CancellationToken.None));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, "technical-eval-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        Assert.Equal(0, await verificationDbContext.LegalHoldReleaseRequests.CountAsync());
        Assert.Equal(0, await verificationDbContext.DeletionEligibilityEvaluations.CountAsync());
    }

    [Fact]
    public async Task Mandatory_audit_persistence_failure_rolls_back_legal_hold_release()
    {
        var scenario = await CreateScenarioAsync("audit-failure-placer");
        var evidence = await SeedEvidenceAsync(scenario);
        await SeedActorAsync(
            scenario,
            "audit-failure-placer",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            evidence.Id.ToString());
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(dbContext, scenario.Accessor, scenario.Clock);
        var hold = await service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(evidence.Id, "Audit failure hold", "audit-failure-hold-001"),
            CancellationToken.None);

        scenario.SetActor("audit-failure-requester");
        await SeedActorAsync(
            scenario,
            "audit-failure-requester",
            ProductAuthorityPermissions.RequestLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));
        var request = await service.RequestLegalHoldReleaseAsync(
            new LegalHoldReleaseRequestCommand(evidence.Id, hold.LegalHoldId, "Release with later audit failure", "audit-failure-request-001"),
            CancellationToken.None);

        scenario.SetActor("audit-failure-approver", correlationId: new string('c', 200));
        await SeedActorAsync(
            scenario,
            "audit-failure-approver",
            ProductAuthorityPermissions.ApproveLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            hold.LegalHoldId.ToString("D"));

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ApproveLegalHoldReleaseAsync(
            new LegalHoldReleaseApprovalCommand(request.ReleaseRequestId, "This should roll back", "audit-failure-approval-001"),
            CancellationToken.None));

        scenario.SetActor("audit-failure-approver");
        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var persistedHold = await verificationDbContext.LegalHolds.SingleAsync(item => item.Id == hold.LegalHoldId);
        var persistedRequest = await verificationDbContext.LegalHoldReleaseRequests.SingleAsync(item => item.Id == request.ReleaseRequestId);
        var persistedEvidence = await verificationDbContext.EvidenceRecords.SingleAsync(item => item.Id == evidence.Id);

        Assert.Null(persistedHold.ReleasedAt);
        Assert.Equal(LegalHoldReleaseStates.Pending, persistedRequest.State);
        Assert.True(persistedEvidence.IsUnderLegalHold);
    }

    [Fact]
    public async Task Evidence_disposal_request_approve_execute_reconcile_requires_sod_and_never_claims_physical_deletion()
    {
        var store = TestDisposalObjectStore.Succeeding();
        var setup = await CreateEligibleDisposalSetupAsync("sod-execute", store);
        var service = setup.Service;

        var request = await service.RequestEvidenceDisposalAsync(
            new EvidenceDisposalRequestCommand(
                setup.Evidence.Id,
                setup.Eligibility.EvaluationId,
                "Retention expired and no hold remains",
                "dispose-request-sod-001"),
            CancellationToken.None);

        await SeedPermissionAssignmentAsync(
            setup.Scenario,
            "disposal-requester",
            ProductAuthorityPermissions.ApproveEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => service.ApproveEvidenceDisposalAsync(
            new EvidenceDisposalApprovalCommand(
                request.DisposalRecordId,
                "Self approval must be denied",
                "dispose-self-approval-001"),
            CancellationToken.None));

        setup.Scenario.SetActor("disposal-approver");
        await SeedActorAsync(
            setup.Scenario,
            "disposal-approver",
            ProductAuthorityPermissions.ApproveEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"));
        var approved = await service.ApproveEvidenceDisposalAsync(
            new EvidenceDisposalApprovalCommand(
                request.DisposalRecordId,
                "Independent approval",
                "dispose-approval-sod-001"),
            CancellationToken.None);

        setup.Scenario.SetActor("disposal-queue");
        await SeedActorAsync(
            setup.Scenario,
            "disposal-queue",
            ProductAuthorityPermissions.QueueEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"));
        var queued = await service.QueueEvidenceDisposalAsync(
            new EvidenceDisposalQueueCommand(request.DisposalRecordId, "dispose-queue-sod-001"),
            CancellationToken.None);

        setup.Scenario.SetActor("workload:evidence-disposal");
        await SeedActorAsync(
            setup.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);
        await SeedPermissionAssignmentAsync(
            setup.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ReconcileEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"));
        var executed = await service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(request.DisposalRecordId, "dispose-execute-sod-001"),
            CancellationToken.None);
        var reconciled = await service.ReconcileEvidenceDisposalAsync(
            new EvidenceDisposalReconciliationCommand(request.DisposalRecordId, "dispose-reconcile-sod-001"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(setup.Scenario.Accessor);
        var record = await verificationDbContext.DisposalRecords.SingleAsync(item => item.Id == request.DisposalRecordId);
        var audits = await verificationDbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync();

        Assert.Equal(DisposalRecordStates.Approved, approved.State);
        Assert.Equal(DisposalRecordStates.Queued, queued.State);
        Assert.Equal(DisposalRecordStates.StorageDisposed, executed.State);
        Assert.Equal(DisposalRecordStates.Completed, reconciled.State);
        Assert.False(reconciled.EvidencePhysicallyDeleted);
        Assert.False(record.EvidencePhysicallyDeleted);
        Assert.Equal(1, store.DisposeCallCount);
        Assert.Equal(1, store.ReconcileCallCount);
        Assert.Equal(1, await verificationDbContext.EvidenceRecords.CountAsync(item => item.Id == setup.Evidence.Id));
        Assert.Equal(1, await verificationDbContext.EvidenceVersions.CountAsync(version => version.EvidenceId == setup.Evidence.Id));
        Assert.Contains("Evidence.DisposalRequested", audits);
        Assert.Contains("Evidence.DisposalApprovalDenied", audits);
        Assert.Contains("Evidence.DisposalApproved", audits);
        Assert.Contains("Evidence.DisposalQueued", audits);
        Assert.Contains("Evidence.DisposalExecutionStarted", audits);
        Assert.Contains("Evidence.DisposalStorageDisposed", audits);
        Assert.Contains("Evidence.DisposalReconciled", audits);
        Assert.Equal(1, await verificationDbContext.LineageRelationships.CountAsync(relationship => relationship.RelationshipType == "EvidenceDisposalReconciled"));
    }

    [Fact]
    public async Task Stale_and_revoked_disposal_approval_authority_are_denied_without_state_change()
    {
        var setup = await CreateEligibleDisposalSetupAsync("stale-revoked", TestDisposalObjectStore.Succeeding());
        var request = await setup.Service.RequestEvidenceDisposalAsync(
            new EvidenceDisposalRequestCommand(setup.Evidence.Id, setup.Eligibility.EvaluationId, "Dispose", "dispose-request-authority-001"),
            CancellationToken.None);

        setup.Scenario.SetActor("stale-disposal-approver");
        await SeedActorAsync(setup.Scenario, "stale-disposal-approver", actorAuthorityVersion: 2);
        await SeedPermissionAssignmentAsync(
            setup.Scenario,
            "stale-disposal-approver",
            ProductAuthorityPermissions.ApproveEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"),
            authorityVersion: 1);
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => setup.Service.ApproveEvidenceDisposalAsync(
            new EvidenceDisposalApprovalCommand(request.DisposalRecordId, "Stale authority", "dispose-stale-approval-001"),
            CancellationToken.None));

        setup.Scenario.SetActor("revoked-disposal-approver");
        await SeedActorAsync(setup.Scenario, "revoked-disposal-approver");
        await SeedPermissionAssignmentAsync(
            setup.Scenario,
            "revoked-disposal-approver",
            ProductAuthorityPermissions.ApproveEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"),
            state: ProductPermissionAssignmentStates.Revoked);
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => setup.Service.ApproveEvidenceDisposalAsync(
            new EvidenceDisposalApprovalCommand(request.DisposalRecordId, "Revoked authority", "dispose-revoked-approval-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(setup.Scenario.Accessor);
        var record = await verificationDbContext.DisposalRecords.SingleAsync(item => item.Id == request.DisposalRecordId);
        Assert.Equal(DisposalRecordStates.Requested, record.State);
        Assert.Null(record.ApprovedAt);
    }

    [Fact]
    public async Task Cross_tenant_and_cross_workspace_disposal_execution_is_denied()
    {
        var protectedSetup = await CreateQueuedDisposalSetupAsync("protected-cross-boundary", TestDisposalObjectStore.Succeeding());

        var attackerTenant = await CreateScenarioAsync("workload:evidence-disposal");
        await SeedActorAsync(
            attackerTenant,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            protectedSetup.Request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);
        await using var attackerTenantDbContext = fixture.CreateDbContext(attackerTenant.Accessor);
        var attackerTenantService = CreateService(attackerTenantDbContext, attackerTenant.Accessor, attackerTenant.Clock, TestDisposalObjectStore.Succeeding());
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerTenantService.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(protectedSetup.Request.DisposalRecordId, "cross-tenant-execute-001"),
            CancellationToken.None));

        var attackerWorkspace = await CreateWorkspaceScenarioAsync(protectedSetup.Scenario, "workload:evidence-disposal");
        await SeedActorAsync(
            attackerWorkspace,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            protectedSetup.Request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);
        await using var attackerWorkspaceDbContext = fixture.CreateDbContext(attackerWorkspace.Accessor);
        var attackerWorkspaceService = CreateService(attackerWorkspaceDbContext, attackerWorkspace.Accessor, attackerWorkspace.Clock, TestDisposalObjectStore.Succeeding());
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => attackerWorkspaceService.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(protectedSetup.Request.DisposalRecordId, "cross-workspace-execute-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(protectedSetup.Scenario.Accessor);
        var protectedRecord = await verificationDbContext.DisposalRecords.SingleAsync(item => item.Id == protectedSetup.Request.DisposalRecordId);
        Assert.Equal(DisposalRecordStates.Queued, protectedRecord.State);
        Assert.False(protectedRecord.EvidencePhysicallyDeleted);
    }

    [Fact]
    public async Task Legal_hold_before_after_approval_or_while_queued_blocks_disposal()
    {
        var beforeApproval = await CreateEligibleDisposalSetupAsync("hold-before-approval", TestDisposalObjectStore.Succeeding());
        var beforeApprovalRequest = await beforeApproval.Service.RequestEvidenceDisposalAsync(
            new EvidenceDisposalRequestCommand(beforeApproval.Evidence.Id, beforeApproval.Eligibility.EvaluationId, "Dispose", "dispose-before-approval-request-001"),
            CancellationToken.None);
        await PlaceBlockingHoldAsync(beforeApproval, "hold-before-approval-001");
        beforeApproval.Scenario.SetActor("disposal-approver");
        await SeedActorAsync(
            beforeApproval.Scenario,
            "disposal-approver",
            ProductAuthorityPermissions.ApproveEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            beforeApprovalRequest.DisposalRecordId.ToString("D"));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => beforeApproval.Service.ApproveEvidenceDisposalAsync(
            new EvidenceDisposalApprovalCommand(beforeApprovalRequest.DisposalRecordId, "Hold blocks approval", "dispose-before-approval-001"),
            CancellationToken.None));

        var afterApproval = await CreateApprovedDisposalSetupAsync("hold-after-approval", TestDisposalObjectStore.Succeeding());
        await PlaceBlockingHoldAsync(afterApproval.Setup, "hold-after-approval-001");
        afterApproval.Setup.Scenario.SetActor("disposal-queue");
        await SeedActorAsync(
            afterApproval.Setup.Scenario,
            "disposal-queue",
            ProductAuthorityPermissions.QueueEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            afterApproval.Request.DisposalRecordId.ToString("D"));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => afterApproval.Setup.Service.QueueEvidenceDisposalAsync(
            new EvidenceDisposalQueueCommand(afterApproval.Request.DisposalRecordId, "dispose-hold-after-approval-001"),
            CancellationToken.None));

        var whileQueued = await CreateQueuedDisposalSetupAsync("hold-while-queued", TestDisposalObjectStore.Succeeding());
        await PlaceBlockingHoldAsync(whileQueued, "hold-while-queued-001");
        whileQueued.Scenario.SetActor("workload:evidence-disposal");
        await SeedActorAsync(
            whileQueued.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            whileQueued.Request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);
        var suspended = await whileQueued.Service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(whileQueued.Request.DisposalRecordId, "dispose-hold-queued-execute-001"),
            CancellationToken.None);

        Assert.Equal(DisposalRecordStates.Suspended, suspended.State);
        Assert.False(suspended.EvidencePhysicallyDeleted);
    }

    [Fact]
    public async Task Retention_change_after_approval_and_command_expiry_are_denied()
    {
        var changedRetention = await CreateApprovedDisposalSetupAsync("retention-change", TestDisposalObjectStore.Succeeding());
        await using (var changeDbContext = fixture.CreateDbContext(changedRetention.Setup.Scenario.Accessor))
        {
            await changeDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                update retention.retention_policies
                set "RetainForDays" = 365
                where "TenantId" = {changedRetention.Setup.Scenario.TenantId.Value}
                    and "WorkspaceId" = {changedRetention.Setup.Scenario.WorkspaceId.Value}
                    and "PolicyKey" = 'expired-policy'
                """);
        }
        changedRetention.Setup.DbContext.ChangeTracker.Clear();

        changedRetention.Setup.Scenario.SetActor("disposal-queue");
        await SeedActorAsync(
            changedRetention.Setup.Scenario,
            "disposal-queue",
            ProductAuthorityPermissions.QueueEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            changedRetention.Request.DisposalRecordId.ToString("D"));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => changedRetention.Setup.Service.QueueEvidenceDisposalAsync(
            new EvidenceDisposalQueueCommand(changedRetention.Request.DisposalRecordId, "dispose-retention-change-001"),
            CancellationToken.None));

        var expiredCommand = await CreateApprovedDisposalSetupAsync("command-expiry", TestDisposalObjectStore.Succeeding());
        expiredCommand.Setup.Scenario.Clock.UtcNow = expiredCommand.Setup.Scenario.Clock.UtcNow.AddMinutes(16);
        expiredCommand.Setup.Scenario.SetActor("disposal-queue");
        await SeedActorAsync(
            expiredCommand.Setup.Scenario,
            "disposal-queue",
            ProductAuthorityPermissions.QueueEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            expiredCommand.Request.DisposalRecordId.ToString("D"));
        await Assert.ThrowsAsync<RetentionGovernanceForbiddenException>(() => expiredCommand.Setup.Service.QueueEvidenceDisposalAsync(
            new EvidenceDisposalQueueCommand(expiredCommand.Request.DisposalRecordId, "dispose-command-expiry-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Command_replay_partial_failure_and_idempotent_retry_are_safe()
    {
        var store = TestDisposalObjectStore.FailingOnceThenSucceeding();
        var setup = await CreateQueuedDisposalSetupAsync("replay-retry", store);
        setup.Scenario.SetActor("workload:evidence-disposal");
        await SeedActorAsync(
            setup.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            setup.Request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);

        var failed = await setup.Service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(setup.Request.DisposalRecordId, "dispose-exec-retry-001"),
            CancellationToken.None);
        var replayedFailure = await setup.Service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(setup.Request.DisposalRecordId, "dispose-exec-retry-001"),
            CancellationToken.None);
        var retry = await setup.Service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(setup.Request.DisposalRecordId, "dispose-exec-retry-002"),
            CancellationToken.None);

        Assert.Equal(DisposalRecordStates.Failed, failed.State);
        Assert.True(replayedFailure.IdempotentReplay);
        Assert.Equal(failed.AttemptCount, replayedFailure.AttemptCount);
        Assert.Equal(DisposalRecordStates.StorageDisposed, retry.State);
        Assert.Equal(2, retry.AttemptCount);
        Assert.False(retry.EvidencePhysicallyDeleted);
    }

    [Fact]
    public async Task Disabled_disposal_store_enforces_kill_switch_without_destructive_execution()
    {
        var setup = await CreateQueuedDisposalSetupAsync("disabled-store", new DisabledEvidenceDisposalObjectStore());
        setup.Scenario.SetActor("workload:evidence-disposal");
        await SeedActorAsync(
            setup.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            setup.Request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);

        var result = await setup.Service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(setup.Request.DisposalRecordId, "dispose-disabled-execute-001"),
            CancellationToken.None);

        Assert.Equal(DisposalRecordStates.Suspended, result.State);
        Assert.False(result.EvidencePhysicallyDeleted);
        Assert.Contains("disabled", result.LastFailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_durability_failure_rolls_back_disposal_request_and_records_are_immutable()
    {
        var auditFailure = await CreateEligibleDisposalSetupAsync("audit-failure", TestDisposalObjectStore.Succeeding());
        auditFailure.Scenario.SetActor("disposal-requester", correlationId: new string('c', 200));
        await Assert.ThrowsAsync<DbUpdateException>(() => auditFailure.Service.RequestEvidenceDisposalAsync(
            new EvidenceDisposalRequestCommand(auditFailure.Evidence.Id, auditFailure.Eligibility.EvaluationId, "Audit failure", "dispose-audit-failure-001"),
            CancellationToken.None));

        auditFailure.Scenario.SetActor("disposal-requester");
        await using (var verificationDbContext = fixture.CreateDbContext(auditFailure.Scenario.Accessor))
        {
            Assert.Equal(0, await verificationDbContext.DisposalRecords.CountAsync());
        }

        var immutable = await CreateEligibleDisposalSetupAsync("immutable-record", TestDisposalObjectStore.Succeeding());
        var request = await immutable.Service.RequestEvidenceDisposalAsync(
            new EvidenceDisposalRequestCommand(immutable.Evidence.Id, immutable.Eligibility.EvaluationId, "Immutable record", "dispose-immutable-001"),
            CancellationToken.None);
        await using var immutableDbContext = fixture.CreateDbContext(immutable.Scenario.Accessor);
        var record = await immutableDbContext.DisposalRecords.SingleAsync(item => item.Id == request.DisposalRecordId);
        immutableDbContext.Entry(record).Property(nameof(DisposalRecord.EvidenceId)).CurrentValue = EvidenceId.New();

        await Assert.ThrowsAsync<InvalidOperationException>(() => immutableDbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Reconciliation_detects_resurrection_without_restoring_evidence_content()
    {
        var store = TestDisposalObjectStore.SucceedingWithResurrection();
        var setup = await CreateQueuedDisposalSetupAsync("resurrection", store);
        setup.Scenario.SetActor("workload:evidence-disposal");
        await SeedActorAsync(
            setup.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ExecuteEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            setup.Request.DisposalRecordId.ToString("D"),
            actorType: ProductActorTypes.Workload);
        await SeedPermissionAssignmentAsync(
            setup.Scenario,
            "workload:evidence-disposal",
            ProductAuthorityPermissions.ReconcileEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            setup.Request.DisposalRecordId.ToString("D"));

        await setup.Service.ExecuteEvidenceDisposalAsync(
            new EvidenceDisposalExecutionCommand(setup.Request.DisposalRecordId, "dispose-resurrection-exec-001"),
            CancellationToken.None);
        var reconciled = await setup.Service.ReconcileEvidenceDisposalAsync(
            new EvidenceDisposalReconciliationCommand(setup.Request.DisposalRecordId, "dispose-resurrection-reconcile-001"),
            CancellationToken.None);

        await using var verificationDbContext = fixture.CreateDbContext(setup.Scenario.Accessor);
        Assert.Equal(DisposalRecordStates.Failed, reconciled.State);
        Assert.False(reconciled.EvidencePhysicallyDeleted);
        Assert.Equal(1, await verificationDbContext.EvidenceRecords.CountAsync(item => item.Id == setup.Evidence.Id));
        Assert.Equal(1, await verificationDbContext.EvidenceVersions.CountAsync(version => version.EvidenceId == setup.Evidence.Id));
        Assert.Contains(
            "Evidence.DisposalResurrectionDetected",
            await verificationDbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync());
    }

    private async Task<EligibleDisposalSetup> CreateEligibleDisposalSetupAsync(
        string testScope,
        IEvidenceDisposalObjectStore disposalObjectStore)
    {
        var scenario = await CreateScenarioAsync("eligibility-evaluator");
        await SeedRetentionPolicyAsync(scenario, "expired-policy", retainForDays: 1);
        var evidence = await SeedEvidenceAsync(
            scenario,
            retentionPolicyKey: "expired-policy",
            capturedAt: scenario.Clock.UtcNow.AddDays(-10));
        await SeedActorAsync(
            scenario,
            "eligibility-evaluator",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            evidence.Id.ToString());
        await using var evaluationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var evaluationService = CreateService(
            evaluationDbContext,
            scenario.Accessor,
            scenario.Clock,
            disposalObjectStore);
        var eligibility = await evaluationService.EvaluateDeletionEligibilityAsync(
            new DeletionEligibilityCommand(evidence.Id, $"eligibility-{testScope}-001"),
            CancellationToken.None);

        scenario.SetActor("disposal-requester");
        await SeedActorAsync(
            scenario,
            "disposal-requester",
            ProductAuthorityPermissions.RequestEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            evidence.Id.ToString());
        var serviceDbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateService(
            serviceDbContext,
            scenario.Accessor,
            scenario.Clock,
            disposalObjectStore);

        return new EligibleDisposalSetup(scenario, evidence, eligibility, service, serviceDbContext);
    }

    private async Task<ApprovedDisposalSetup> CreateApprovedDisposalSetupAsync(
        string testScope,
        IEvidenceDisposalObjectStore disposalObjectStore)
    {
        var setup = await CreateEligibleDisposalSetupAsync(testScope, disposalObjectStore);
        var request = await setup.Service.RequestEvidenceDisposalAsync(
            new EvidenceDisposalRequestCommand(
                setup.Evidence.Id,
                setup.Eligibility.EvaluationId,
                $"Dispose {testScope}",
                $"dispose-request-{testScope}-001"),
            CancellationToken.None);
        setup.Scenario.SetActor("disposal-approver");
        await SeedActorAsync(
            setup.Scenario,
            "disposal-approver",
            ProductAuthorityPermissions.ApproveEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            request.DisposalRecordId.ToString("D"));
        await setup.Service.ApproveEvidenceDisposalAsync(
            new EvidenceDisposalApprovalCommand(
                request.DisposalRecordId,
                $"Approve {testScope}",
                $"dispose-approval-{testScope}-001"),
            CancellationToken.None);

        return new ApprovedDisposalSetup(setup, request);
    }

    private async Task<QueuedDisposalSetup> CreateQueuedDisposalSetupAsync(
        string testScope,
        IEvidenceDisposalObjectStore disposalObjectStore)
    {
        var approved = await CreateApprovedDisposalSetupAsync(testScope, disposalObjectStore);
        approved.Setup.Scenario.SetActor("disposal-queue");
        await SeedActorAsync(
            approved.Setup.Scenario,
            "disposal-queue",
            ProductAuthorityPermissions.QueueEvidenceDisposal,
            ProductAuthorityResourceTypes.EvidenceDisposal,
            approved.Request.DisposalRecordId.ToString("D"));
        await approved.Setup.Service.QueueEvidenceDisposalAsync(
            new EvidenceDisposalQueueCommand(
                approved.Request.DisposalRecordId,
                $"dispose-queue-{testScope}-001"),
            CancellationToken.None);

        return new QueuedDisposalSetup(
            approved.Setup.Scenario,
            approved.Setup.Evidence,
            approved.Setup.Eligibility,
            approved.Setup.Service,
            approved.Request);
    }

    private async Task PlaceBlockingHoldAsync(
        EligibleDisposalSetup setup,
        string idempotencyKey)
    {
        setup.Scenario.SetActor("blocking-legal-hold");
        await SeedActorAsync(
            setup.Scenario,
            "blocking-legal-hold",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            setup.Evidence.Id.ToString());
        await setup.Service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(
                setup.Evidence.Id,
                "Blocking disposal until legal preservation ends",
                idempotencyKey),
            CancellationToken.None);
    }

    private async Task PlaceBlockingHoldAsync(
        QueuedDisposalSetup setup,
        string idempotencyKey)
    {
        setup.Scenario.SetActor("blocking-legal-hold");
        await SeedActorAsync(
            setup.Scenario,
            "blocking-legal-hold",
            ProductAuthorityPermissions.ManageLegalHold,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            setup.Evidence.Id.ToString());
        await setup.Service.PlaceLegalHoldAsync(
            new PlaceLegalHoldCommand(
                setup.Evidence.Id,
                "Blocking queued disposal",
                idempotencyKey),
            CancellationToken.None);
    }

    private async Task<RetentionScenario> CreateScenarioAsync(string principalSubject)
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId, principalSubject);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = $"Retention Tenant {Guid.NewGuid():N}",
            ExternalAuthority = $"retention-{Guid.NewGuid():N}",
            LifecycleState = "Active",
            CreatedBy = "retention-test",
            CreatedAt = clock.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Retention Workspace {Guid.NewGuid():N}",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "retention-test",
            CreatedAt = clock.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return new RetentionScenario(tenantId, workspaceId, accessor, clock);
    }

    private async Task<RetentionScenario> CreateWorkspaceScenarioAsync(
        RetentionScenario source,
        string principalSubject)
    {
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(source.TenantId, workspaceId, principalSubject);
        var scenario = new RetentionScenario(source.TenantId, workspaceId, accessor, source.Clock);
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = source.TenantId,
            Name = $"Retention Workspace {Guid.NewGuid():N}",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "retention-test",
            CreatedAt = source.Clock.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return scenario;
    }

    private async Task SeedRetentionPolicyAsync(
        RetentionScenario scenario,
        string policyKey,
        int retainForDays)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        dbContext.RetentionPolicies.Add(new RetentionPolicy
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            PolicyKey = policyKey,
            Description = $"{policyKey} test policy",
            RetainForDays = retainForDays,
            LegalHoldOverridesDeletion = true,
            CreatedBy = "retention-test",
            CreatedAt = scenario.Clock.UtcNow,
            IdempotencyKey = $"retention-policy-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{policyKey}|{retainForDays}")
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedActorAsync(
        RetentionScenario scenario,
        string subject,
        string? permissionKey = null,
        string resourceType = ProductAuthorityResourceTypes.Any,
        string resourceId = ProductAuthorityResourceIds.Any,
        long actorAuthorityVersion = 1,
        string actorType = ProductActorTypes.Human)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var actor = new ProductActor
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            Subject = subject,
            DisplayName = subject,
            ActorType = actorType,
            State = ProductActorStates.Active,
            AuthorityVersion = actorAuthorityVersion,
            AuthorityChangedAt = scenario.Clock.UtcNow,
            CreatedBy = "retention-test",
            CreatedAt = scenario.Clock.UtcNow
        };
        dbContext.ProductActors.Add(actor);
        dbContext.ProductTenantMemberships.Add(new ProductTenantMembership
        {
            TenantId = scenario.TenantId,
            ActorId = actor.Id,
            ActorSubject = subject,
            State = ProductMembershipStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            GrantedBy = "retention-test",
            GrantedAt = scenario.Clock.UtcNow,
            IdempotencyKey = $"tenant-membership-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{subject}|tenant")
        });
        dbContext.ProductWorkspaceMemberships.Add(new ProductWorkspaceMembership
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            ActorId = actor.Id,
            ActorSubject = subject,
            State = ProductMembershipStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            GrantedBy = "retention-test",
            GrantedAt = scenario.Clock.UtcNow,
            IdempotencyKey = $"workspace-membership-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{subject}|workspace")
        });

        if (permissionKey is not null)
        {
            AddPermissionAssignment(
                dbContext,
                scenario,
                actor,
                permissionKey,
                resourceType,
                resourceId,
                actor.AuthorityVersion,
                ProductPermissionAssignmentStates.Active);
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedPermissionAssignmentAsync(
        RetentionScenario scenario,
        string subject,
        string permissionKey,
        string resourceType,
        string resourceId,
        long? authorityVersion = null,
        string state = ProductPermissionAssignmentStates.Active)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var actor = await dbContext.ProductActors.SingleAsync(item => item.Subject == subject);
        AddPermissionAssignment(
            dbContext,
            scenario,
            actor,
            permissionKey,
            resourceType,
            resourceId,
            authorityVersion ?? actor.AuthorityVersion,
            state);
        await dbContext.SaveChangesAsync();
    }

    private static void AddPermissionAssignment(
        DataLooMDbContext dbContext,
        RetentionScenario scenario,
        ProductActor actor,
        string permissionKey,
        string resourceType,
        string resourceId,
        long authorityVersion,
        string state)
    {
        dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            ActorId = actor.Id,
            ActorSubject = actor.Subject,
            PermissionKey = permissionKey,
            ResourceType = resourceType,
            ResourceId = resourceId,
            State = state,
            AuthorityVersion = authorityVersion,
            AssignedBy = "retention-test",
            AssignedAt = scenario.Clock.UtcNow,
            EffectiveFrom = scenario.Clock.UtcNow.AddMinutes(-1),
            RevokedAt = state == ProductPermissionAssignmentStates.Revoked ? scenario.Clock.UtcNow : null,
            RevokedBy = state == ProductPermissionAssignmentStates.Revoked ? "retention-test" : null,
            IdempotencyKey = $"permission-assignment-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actor.Subject}|{permissionKey}|{resourceType}|{resourceId}|{state}|{Guid.NewGuid():N}")
        });
    }

    private async Task<EvidenceRecord> SeedEvidenceAsync(
        RetentionScenario scenario,
        string retentionPolicyKey = "default",
        DateTimeOffset? capturedAt = null,
        string lifecycleState = "Available")
    {
        var evidenceId = EvidenceId.New();
        var versionId = EvidenceVersionId.New();
        var lineageId = LineageId.New();
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var evidence = new EvidenceRecord
        {
            Id = evidenceId,
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            LineageId = lineageId,
            CurrentVersionId = versionId,
            EvidenceType = "Document",
            Classification = "Internal",
            LifecycleState = lifecycleState,
            RegisteredBy = "retention-test",
            BlobName = $"retention/{evidenceId}",
            ContentType = "text/plain",
            ContentLength = 42,
            Sha256Hash = Sha256($"evidence|{evidenceId}"),
            VerificationStatus = EvidenceVerificationStatus.Verified,
            Version = 1,
            IsImmutable = true,
            IsUnderLegalHold = false,
            RetentionPolicyKey = retentionPolicyKey,
            RegistrationIdempotencyKey = $"evidence-{Guid.NewGuid():N}",
            RegistrationRequestHash = Sha256($"registration|{evidenceId}"),
            CapturedAt = capturedAt ?? scenario.Clock.UtcNow
        };
        dbContext.EvidenceRecords.Add(evidence);
        dbContext.EvidenceVersions.Add(new EvidenceVersion
        {
            Id = versionId,
            EvidenceId = evidenceId,
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            Sequence = 1,
            OriginalFileName = "retention.txt",
            MediaType = "text/plain",
            DeclaredSize = 42,
            ContentHash = evidence.Sha256Hash,
            StorageObjectReference = evidence.BlobName,
            IntegrityState = "Verified",
            CreatedAt = scenario.Clock.UtcNow,
            CreatedBy = "retention-test"
        });
        dbContext.LineageRelationships.Add(new LineageRelationship
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            SourceLineageId = lineageId,
            TargetLineageId = lineageId,
            RelationshipType = "RegisteredInitialVersion",
            ActorOrProcess = "retention-test",
            CorrelationId = scenario.Accessor.Current!.CorrelationId,
            CausationId = $"seed-evidence:{evidenceId}",
            Version = 1,
            ValidFrom = scenario.Clock.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return evidence;
    }

    private RetentionGovernanceService CreateService(
        DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        IClock clock,
        IEvidenceDisposalObjectStore? disposalObjectStore = null)
    {
        var productAuthorityService = new ProductAuthorityService(
            dbContext,
            accessor,
            clock,
            new ProductAuthorityAuditStore(fixture.CreateDbContextOptions()));

        return new RetentionGovernanceService(
            dbContext,
            accessor,
            clock,
            productAuthorityService,
            new EfOutboxWriter(dbContext),
            new PostgresRlsSessionContext(dbContext, accessor),
            disposalObjectStore ?? new DisabledEvidenceDisposalObjectStore());
    }

    private static RequestContextAccessor CreateRequestContext(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string actor,
        string? correlationId = null)
    {
        return new RequestContextAccessor
        {
            Current = new RequestContext(
                tenantId,
                workspaceId,
                new PrincipalSubject(actor),
                correlationId ?? $"corr-{Guid.NewGuid():N}")
        };
    }

    private static string Sha256(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private sealed record EligibleDisposalSetup(
        RetentionScenario Scenario,
        EvidenceRecord Evidence,
        DeletionEligibilityResult Eligibility,
        RetentionGovernanceService Service,
        DataLooMDbContext DbContext) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }

    private sealed record ApprovedDisposalSetup(
        EligibleDisposalSetup Setup,
        EvidenceDisposalResult Request);

    private sealed record QueuedDisposalSetup(
        RetentionScenario Scenario,
        EvidenceRecord Evidence,
        DeletionEligibilityResult Eligibility,
        RetentionGovernanceService Service,
        EvidenceDisposalResult Request);

    private sealed class TestDisposalObjectStore : IEvidenceDisposalObjectStore
    {
        private readonly Queue<EvidenceDisposalObjectResult> disposalResults;
        private readonly EvidenceDisposalReconciliationResult reconciliationResult;

        private TestDisposalObjectStore(
            IEnumerable<EvidenceDisposalObjectResult> disposalResults,
            EvidenceDisposalReconciliationResult reconciliationResult)
        {
            this.disposalResults = new Queue<EvidenceDisposalObjectResult>(disposalResults);
            this.reconciliationResult = reconciliationResult;
        }

        public int DisposeCallCount { get; private set; }

        public int ReconcileCallCount { get; private set; }

        public static TestDisposalObjectStore Succeeding()
        {
            return new TestDisposalObjectStore(
                [
                    new EvidenceDisposalObjectResult(
                        EvidenceDisposalObjectOutcomes.Succeeded,
                        "SyntheticNonProductionDisposed",
                        EvidencePhysicallyDeleted: false,
                        "Synthetic test disposal completed.")
                ],
                new EvidenceDisposalReconciliationResult(
                    Confirmed: true,
                    ResurrectionDetected: false,
                    EvidencePhysicallyDeleted: false,
                    "Synthetic reconciliation confirmed."));
        }

        public static TestDisposalObjectStore FailingOnceThenSucceeding()
        {
            return new TestDisposalObjectStore(
                [
                    new EvidenceDisposalObjectResult(
                        EvidenceDisposalObjectOutcomes.Failed,
                        "SyntheticStorageFailure",
                        EvidencePhysicallyDeleted: false,
                        "Synthetic partial storage failure."),
                    new EvidenceDisposalObjectResult(
                        EvidenceDisposalObjectOutcomes.Succeeded,
                        "SyntheticNonProductionDisposed",
                        EvidencePhysicallyDeleted: false,
                        "Synthetic retry completed.")
                ],
                new EvidenceDisposalReconciliationResult(
                    Confirmed: true,
                    ResurrectionDetected: false,
                    EvidencePhysicallyDeleted: false,
                    "Synthetic reconciliation confirmed."));
        }

        public static TestDisposalObjectStore SucceedingWithResurrection()
        {
            return new TestDisposalObjectStore(
                [
                    new EvidenceDisposalObjectResult(
                        EvidenceDisposalObjectOutcomes.Succeeded,
                        "SyntheticNonProductionDisposed",
                        EvidencePhysicallyDeleted: false,
                        "Synthetic test disposal completed.")
                ],
                new EvidenceDisposalReconciliationResult(
                    Confirmed: false,
                    ResurrectionDetected: true,
                    EvidencePhysicallyDeleted: false,
                    "Synthetic resurrection detected."));
        }

        public Task<EvidenceDisposalObjectResult> DisposeEvidenceContentAsync(
            EvidenceDisposalObjectRequest request,
            CancellationToken cancellationToken)
        {
            DisposeCallCount += 1;
            if (disposalResults.Count == 0)
            {
                return Task.FromResult(new EvidenceDisposalObjectResult(
                    EvidenceDisposalObjectOutcomes.Succeeded,
                    "SyntheticNonProductionDisposed",
                    EvidencePhysicallyDeleted: false,
                    "Synthetic fallback disposal completed."));
            }

            return Task.FromResult(disposalResults.Dequeue());
        }

        public Task<EvidenceDisposalReconciliationResult> ReconcileEvidenceContentAsync(
            EvidenceDisposalReconciliationRequest request,
            CancellationToken cancellationToken)
        {
            ReconcileCallCount += 1;
            return Task.FromResult(reconciliationResult);
        }
    }

    private sealed record RetentionScenario(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        RequestContextAccessor Accessor,
        MutableClock Clock)
    {
        public void SetActor(string actor, string? correlationId = null)
        {
            Accessor.Current = new RequestContext(
                TenantId,
                WorkspaceId,
                new PrincipalSubject(actor),
                correlationId ?? $"corr-{Guid.NewGuid():N}");
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}