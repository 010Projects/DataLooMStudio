using System.Security.Cryptography;
using System.Text;

using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Lineage;
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

    private async Task SeedActorAsync(
        RetentionScenario scenario,
        string subject,
        string? permissionKey = null,
        string resourceType = ProductAuthorityResourceTypes.Any,
        string resourceId = ProductAuthorityResourceIds.Any)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var actor = new ProductActor
        {
            TenantId = scenario.TenantId,
            WorkspaceId = scenario.WorkspaceId,
            Subject = subject,
            DisplayName = subject,
            ActorType = ProductActorTypes.Human,
            State = ProductActorStates.Active,
            AuthorityVersion = 1,
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
                AssignedBy = "retention-test",
                AssignedAt = scenario.Clock.UtcNow,
                EffectiveFrom = scenario.Clock.UtcNow.AddMinutes(-1),
                IdempotencyKey = $"permission-assignment-{Guid.NewGuid():N}",
                RequestHash = Sha256($"{subject}|{permissionKey}|{resourceType}|{resourceId}")
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<EvidenceRecord> SeedEvidenceAsync(RetentionScenario scenario)
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
            LifecycleState = "Available",
            RegisteredBy = "retention-test",
            BlobName = $"retention/{evidenceId}",
            ContentType = "text/plain",
            ContentLength = 42,
            Sha256Hash = Sha256($"evidence|{evidenceId}"),
            VerificationStatus = EvidenceVerificationStatus.Verified,
            Version = 1,
            IsImmutable = true,
            IsUnderLegalHold = false,
            RetentionPolicyKey = "default",
            RegistrationIdempotencyKey = $"evidence-{Guid.NewGuid():N}",
            RegistrationRequestHash = Sha256($"registration|{evidenceId}"),
            CapturedAt = scenario.Clock.UtcNow
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
        IClock clock)
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
            new PostgresRlsSessionContext(dbContext, accessor));
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

    private static string Sha256(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private sealed record RetentionScenario(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        RequestContextAccessor Accessor,
        MutableClock Clock);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}