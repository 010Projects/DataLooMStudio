using System.Security.Cryptography;
using System.Text;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Infrastructure.SecurityScanning;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Persistence.Tests;

public sealed class EvidenceContentServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Upload_allocation_creates_short_lived_authority_and_audit_lineage_outbox()
    {
        var scenario = await CreateRegisteredEvidenceAsync("allocation");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));

        var result = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "upload-allocation-001"),
            CancellationToken.None);

        var evidence = await dbContext.EvidenceRecords.SingleAsync(evidence => evidence.Id == scenario.EvidenceId);
        var allocation = await dbContext.EvidenceUploadAllocations.SingleAsync(item => item.Id == result.AllocationId);

        Assert.False(result.IdempotentReplay);
        Assert.Equal("UploadAllocated", evidence.LifecycleState);
        Assert.Equal("Write", result.PermittedOperation);
        Assert.True(result.ExpiresAt > scenario.Clock.UtcNow);
        Assert.StartsWith("dls-dev-upload:", result.UploadAuthority, StringComparison.Ordinal);
        Assert.Equal(result.StorageObjectReference, allocation.StorageObjectReference);
        Assert.NotEqual(result.UploadAuthority, allocation.UploadAuthorityHash);
        Assert.Equal(2, await dbContext.AuditEntries.CountAsync());
        Assert.Equal(2, await dbContext.LineageRelationships.CountAsync());
        Assert.Equal(2, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Upload_allocation_replays_duplicate_idempotency_key_without_duplicate_rows()
    {
        var scenario = await CreateRegisteredEvidenceAsync("allocation-replay");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        var request = new EvidenceUploadAllocationRequest(scenario.EvidenceId, "upload-allocation-replay-001");

        var first = await service.AllocateUploadAsync(request, CancellationToken.None);
        var second = await service.AllocateUploadAsync(request, CancellationToken.None);

        Assert.False(first.IdempotentReplay);
        Assert.True(second.IdempotentReplay);
        Assert.Equal(first.AllocationId, second.AllocationId);
        Assert.Equal(1, await dbContext.EvidenceUploadAllocations.CountAsync());
    }

    [Fact]
    public async Task Content_receipt_with_matching_size_hash_and_clean_scan_marks_evidence_available()
    {
        var content = Encoding.UTF8.GetBytes("clean evidence payload");
        var scenario = await CreateRegisteredEvidenceAsync("available", content);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var scanner = new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean);
        var service = CreateContentService(dbContext, scenario.Accessor, store, scanner);
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "content-clean-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);

        var result = await service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(
                scenario.EvidenceId,
                scenario.VersionId,
                allocation.StorageObjectReference,
                "content-clean-receipt-001"),
            CancellationToken.None);

        var evidence = await dbContext.EvidenceRecords.SingleAsync(evidence => evidence.Id == scenario.EvidenceId);
        var verification = await dbContext.EvidenceContentVerifications.SingleAsync();
        var auditActions = await dbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync();
        var auditMetadata = string.Join(Environment.NewLine, await dbContext.AuditEntries.Select(audit => audit.MetadataJson).ToArrayAsync());

        Assert.False(result.IdempotentReplay);
        Assert.Equal("Available", result.LifecycleState);
        Assert.Equal("Succeeded", result.IntegrityOutcome);
        Assert.Equal("Clean", result.ScanOutcome);
        Assert.Equal(EvidenceVerificationStatus.Verified, evidence.VerificationStatus);
        Assert.Equal(content.Length, verification.ActualSize);
        Assert.Equal(Sha256(content), verification.ActualSha256Hash);
        Assert.Contains("Evidence.ContentReceived", auditActions);
        Assert.Contains("Evidence.IntegrityVerificationSucceeded", auditActions);
        Assert.Contains("Evidence.ScanRequested", auditActions);
        Assert.Contains("Evidence.ScanCompleted", auditActions);
        Assert.Contains("Evidence.Available", auditActions);
        Assert.DoesNotContain("clean evidence payload", auditMetadata, StringComparison.Ordinal);
        Assert.Equal(1, scanner.CallCount);
        Assert.Equal(4, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Content_receipt_replays_duplicate_confirmation_without_duplicate_verification()
    {
        var content = Encoding.UTF8.GetBytes("duplicate receipt");
        var scenario = await CreateRegisteredEvidenceAsync("receipt-replay", content);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "content-replay-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);
        var request = new EvidenceContentReceiptRequest(
            scenario.EvidenceId,
            scenario.VersionId,
            allocation.StorageObjectReference,
            "content-replay-receipt-001");

        var first = await service.ConfirmContentReceivedAsync(request, CancellationToken.None);
        var second = await service.ConfirmContentReceivedAsync(request, CancellationToken.None);

        Assert.False(first.IdempotentReplay);
        Assert.True(second.IdempotentReplay);
        Assert.Equal("Available", second.LifecycleState);
        Assert.Equal(1, await dbContext.EvidenceContentVerifications.CountAsync());
    }

    [Fact]
    public async Task Hash_mismatch_quarantines_evidence_without_calling_scanner()
    {
        var declared = Encoding.UTF8.GetBytes("declared payload");
        var actual = Encoding.UTF8.GetBytes("mutated- payload");
        var scenario = await CreateRegisteredEvidenceAsync("hash-mismatch", declared);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var scanner = new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean);
        var service = CreateContentService(dbContext, scenario.Accessor, store, scanner);
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "hash-mismatch-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, actual, "text/plain", CancellationToken.None);

        var result = await service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, "hash-mismatch-receipt-001"),
            CancellationToken.None);

        Assert.Equal("Quarantined", result.LifecycleState);
        Assert.Equal("HashMismatch", result.IntegrityOutcome);
        Assert.Equal("NotRun", result.ScanOutcome);
        Assert.Equal(0, scanner.CallCount);
        Assert.True(await store.IsQuarantinedAsync(allocation.StorageObjectReference, CancellationToken.None));
    }

    [Fact]
    public async Task Size_mismatch_quarantines_evidence()
    {
        var declared = Encoding.UTF8.GetBytes("declared");
        var actual = Encoding.UTF8.GetBytes("declared-plus-extra");
        var scenario = await CreateRegisteredEvidenceAsync("size-mismatch", declared);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "size-mismatch-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, actual, "text/plain", CancellationToken.None);

        var result = await service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, "size-mismatch-receipt-001"),
            CancellationToken.None);

        Assert.Equal("Quarantined", result.LifecycleState);
        Assert.Equal("SizeMismatch", result.IntegrityOutcome);
        Assert.True(await store.IsQuarantinedAsync(allocation.StorageObjectReference, CancellationToken.None));
    }

    [Theory]
    [InlineData(EvidenceMalwareScanOutcome.Malicious)]
    [InlineData(EvidenceMalwareScanOutcome.Suspicious)]
    [InlineData(EvidenceMalwareScanOutcome.Unavailable)]
    [InlineData(EvidenceMalwareScanOutcome.Failed)]
    public async Task Non_clean_scan_outcome_quarantines_evidence(EvidenceMalwareScanOutcome scanOutcome)
    {
        var content = Encoding.UTF8.GetBytes($"scan outcome {scanOutcome}");
        var scenario = await CreateRegisteredEvidenceAsync($"scan-{scanOutcome}", content);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(scanOutcome));
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, $"scan-{scanOutcome}-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);

        var result = await service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, $"scan-{scanOutcome}-receipt-001"),
            CancellationToken.None);

        Assert.Equal("Quarantined", result.LifecycleState);
        Assert.Equal("Succeeded", result.IntegrityOutcome);
        Assert.Equal(scanOutcome.ToString(), result.ScanOutcome);
        Assert.True(await store.IsQuarantinedAsync(allocation.StorageObjectReference, CancellationToken.None));
    }

    [Fact]
    public async Task Expired_allocation_is_rejected_and_audited()
    {
        var content = Encoding.UTF8.GetBytes("expired allocation");
        var scenario = await CreateRegisteredEvidenceAsync("expired", content);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean), scenario.Clock);
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "expired-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);
        scenario.Clock.Advance(TimeSpan.FromMinutes(16));

        await Assert.ThrowsAsync<EvidenceContentConflictException>(() => service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, "expired-receipt-001"),
            CancellationToken.None));

        Assert.Equal("Expired", (await dbContext.EvidenceUploadAllocations.SingleAsync()).Status);
        Assert.Contains("Evidence.UploadAllocationExpired", await dbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync());
    }

    [Fact]
    public async Task Cross_tenant_actor_cannot_allocate_upload_for_another_tenant_evidence()
    {
        var scenario = await CreateRegisteredEvidenceAsync("cross-tenant");
        var otherTenantContext = CreateRequestContext(TenantId.New(), scenario.WorkspaceId, "actor-cross-tenant");
        await using var dbContext = fixture.CreateDbContext(otherTenantContext);
        var service = CreateContentService(dbContext, otherTenantContext, new DevelopmentEvidenceObjectStore(), new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));

        await Assert.ThrowsAsync<EvidenceContentForbiddenException>(() => service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "cross-tenant-allocation-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Cross_workspace_actor_cannot_confirm_receipt_for_another_workspace_evidence()
    {
        var content = Encoding.UTF8.GetBytes("cross workspace");
        var scenario = await CreateRegisteredEvidenceAsync("cross-workspace", content);
        await using var ownerDbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var ownerService = CreateContentService(ownerDbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        var allocation = await ownerService.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "cross-workspace-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);

        var otherWorkspaceId = WorkspaceId.New();
        var otherWorkspaceContext = CreateRequestContext(scenario.TenantId, otherWorkspaceId, "actor-cross-workspace");
        await SeedTenantAndWorkspaceAsync(scenario.TenantId, otherWorkspaceId, otherWorkspaceContext, seedTenant: false);
        await using var dbContext = fixture.CreateDbContext(otherWorkspaceContext);
        var service = CreateContentService(dbContext, otherWorkspaceContext, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));

        await Assert.ThrowsAsync<EvidenceContentForbiddenException>(() => service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, "cross-workspace-receipt-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Missing_request_context_is_denied()
    {
        await using var dbContext = fixture.CreateDbContext();
        var service = CreateContentService(
            dbContext,
            new RequestContextAccessor(),
            new DevelopmentEvidenceObjectStore(),
            new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(EvidenceId.New(), "missing-context-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Background_integrity_process_preserves_tenant_and_workspace_context()
    {
        var content = Encoding.UTF8.GetBytes("background integrity");
        var scenario = await CreateRegisteredEvidenceAsync("background", content, actor: "integrity-worker");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var service = CreateContentService(dbContext, scenario.Accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "background-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);

        await service.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, "background-receipt-001"),
            CancellationToken.None);

        var verification = await dbContext.EvidenceContentVerifications.SingleAsync();
        Assert.Equal(scenario.TenantId, verification.TenantId);
        Assert.Equal(scenario.WorkspaceId, verification.WorkspaceId);
    }

    [Fact]
    public async Task Content_receipt_rolls_back_database_changes_when_outbox_write_fails()
    {
        var content = Encoding.UTF8.GetBytes("rollback content");
        var scenario = await CreateRegisteredEvidenceAsync("rollback", content);
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var store = new DevelopmentEvidenceObjectStore();
        var scanner = new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean);
        var service = CreateContentService(dbContext, scenario.Accessor, store, scanner);
        var allocation = await service.AllocateUploadAsync(
            new EvidenceUploadAllocationRequest(scenario.EvidenceId, "rollback-allocation-001"),
            CancellationToken.None);
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);

        await using var failingDbContext = fixture.CreateDbContext(scenario.Accessor);
        var failingService = CreateContentService(
            failingDbContext,
            scenario.Accessor,
            store,
            scanner,
            scenario.Clock,
            new ThrowingOutboxWriter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.ConfirmContentReceivedAsync(
            new EvidenceContentReceiptRequest(scenario.EvidenceId, scenario.VersionId, allocation.StorageObjectReference, "rollback-receipt-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        var evidence = await verificationDbContext.EvidenceRecords.SingleAsync(evidence => evidence.Id == scenario.EvidenceId);
        Assert.Equal("UploadAllocated", evidence.LifecycleState);
        Assert.Equal(0, await verificationDbContext.EvidenceContentVerifications.CountAsync());
    }

    private async Task<RegisteredEvidence> CreateRegisteredEvidenceAsync(
        string scenarioName,
        byte[]? content = null,
        string actor = "test-actor")
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId, actor);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var registrationService = CreateRegistrationService(dbContext, accessor, clock);
        var declaredContent = content ?? Encoding.UTF8.GetBytes($"evidence content {scenarioName}");
        var result = await registrationService.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                $"{scenarioName}.txt",
                "text/plain",
                declaredContent.Length,
                Sha256(declaredContent),
                $"registered/{scenarioName}/{Guid.NewGuid():N}",
                "default",
                $"registration-{scenarioName}-{Guid.NewGuid():N}"),
            CancellationToken.None);

        return new RegisteredEvidence(tenantId, workspaceId, result.EvidenceId, result.VersionId, accessor, clock);
    }

    private async Task SeedTenantAndWorkspaceAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        RequestContextAccessor accessor,
        bool seedTenant = true)
    {
        await using var dbContext = fixture.CreateDbContext(accessor);
        if (seedTenant)
        {
            dbContext.Tenants.Add(new Tenant
            {
                Id = tenantId,
                DisplayName = "Synthetic Tenant",
                ExternalAuthority = $"synthetic-content-{tenantId}-{Guid.NewGuid():N}",
                LifecycleState = "Active",
                CreatedBy = "test",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Synthetic Workspace {Guid.NewGuid():N}",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
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

    private static EvidenceRegistrationService CreateRegistrationService(
        DataLooMStudio.Runtime.Persistence.DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        IClock clock)
    {
        var rls = new PostgresRlsSessionContext(dbContext, accessor);
        IOutboxWriter outboxWriter = new EfOutboxWriter(dbContext);
        return new EvidenceRegistrationService(
            dbContext,
            accessor,
            clock,
            outboxWriter,
            new TestProductAuthorityService(),
            rls);
    }

    private static EvidenceContentService CreateContentService(
        DataLooMStudio.Runtime.Persistence.DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        DevelopmentEvidenceObjectStore store,
        IEvidenceMalwareScanner scanner,
        IClock? clock = null,
        IOutboxWriter? outboxWriter = null)
    {
        var rls = new PostgresRlsSessionContext(dbContext, accessor);
        return new EvidenceContentService(
            dbContext,
            accessor,
            clock ?? new MutableClock(DateTimeOffset.UtcNow),
            outboxWriter ?? new EfOutboxWriter(dbContext),
            rls,
            new TestProductAuthorityService(),
            store,
            scanner);
    }

    private static string Sha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed record RegisteredEvidence(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        EvidenceId EvidenceId,
        EvidenceVersionId VersionId,
        RequestContextAccessor Accessor,
        MutableClock Clock);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration)
        {
            UtcNow = UtcNow.Add(duration);
        }
    }

    private sealed class FakeMalwareScanner(EvidenceMalwareScanOutcome outcome) : IEvidenceMalwareScanner
    {
        public int CallCount { get; private set; }

        public Task<EvidenceMalwareScanResult> ScanAsync(
            EvidenceMalwareScanRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new EvidenceMalwareScanResult(
                outcome,
                "fake-test-scanner",
                "1.0",
                outcome == EvidenceMalwareScanOutcome.Clean ? null : $"Synthetic {outcome} result."));
        }
    }

    private sealed class ThrowingOutboxWriter : IOutboxWriter
    {
        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Synthetic outbox failure.");
        }
    }
}