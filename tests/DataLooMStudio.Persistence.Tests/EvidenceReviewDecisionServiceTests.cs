using System.Security.Cryptography;
using System.Text;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Infrastructure.SecurityScanning;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Persistence.Tests;

public sealed class EvidenceReviewDecisionServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Review_request_for_available_evidence_creates_audit_lineage_and_outbox()
    {
        var scenario = await CreateAvailableEvidenceAsync("review-request");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateReviewDecisionService(dbContext, scenario.Accessor, scenario.Clock);

        var result = await RequestReviewAsync(service, scenario, "review-request-001");

        Assert.False(result.IdempotentReplay);
        Assert.Equal("Requested", result.State);
        Assert.Equal(1, await dbContext.EvidenceReviewRequests.CountAsync(review => review.Id == result.ReviewId));
        Assert.True(await dbContext.AuditEntries.AnyAsync(audit => audit.Action == "EvidenceReview.Requested"));
        Assert.True(await dbContext.LineageRelationships.AnyAsync(relationship => relationship.RelationshipType == "ReviewRequested"));
        Assert.True(await dbContext.OutboxMessages.AnyAsync(message => message.MessageType == "EvidenceReviewRequested"));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("BillingAdministrator")]
    [InlineData("Support")]
    public async Task Reviewer_assignment_denies_non_evidence_roles(string role)
    {
        var scenario = await CreateAvailableEvidenceAsync($"assignment-{role}");
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        var service = CreateReviewDecisionService(dbContext, scenario.Accessor, scenario.Clock);
        var review = await RequestReviewAsync(service, scenario, $"review-{role}-001");

        await Assert.ThrowsAsync<EvidenceReviewDecisionForbiddenException>(() => service.AssignReviewerAsync(
            new EvidenceReviewerAssignmentCommand(review.ReviewId, "reviewer-denied", role, $"assignment-{role}-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Unassigned_reviewer_cannot_create_candidate_decision()
    {
        var scenario = await CreateAvailableEvidenceAsync("unassigned-candidate");
        await using var ownerDbContext = fixture.CreateDbContext(scenario.Accessor);
        var ownerService = CreateReviewDecisionService(ownerDbContext, scenario.Accessor, scenario.Clock);
        var review = await RequestReviewAsync(ownerService, scenario, "unassigned-review-001");

        var reviewerAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "unassigned-reviewer");
        await using var reviewerDbContext = fixture.CreateDbContext(reviewerAccessor);
        var reviewerService = CreateReviewDecisionService(reviewerDbContext, reviewerAccessor, scenario.Clock);

        await Assert.ThrowsAsync<EvidenceReviewDecisionForbiddenException>(() => reviewerService.CreateCandidateDecisionAsync(
            new EvidenceCandidateDecisionCommand(review.ReviewId, EvidenceDecisionTypes.Accept, "approve evidence", null, "unassigned-candidate-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Assigned_reviewer_creates_candidate_decision_with_idempotent_replay()
    {
        var scenario = await CreateAvailableEvidenceAsync("candidate-replay");
        var review = await CreateReviewWithAssignmentsAsync(scenario, assignReviewer: true, assignApprover: false);
        var reviewerAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "evidence-reviewer");
        await using var reviewerDbContext = fixture.CreateDbContext(reviewerAccessor);
        var reviewerService = CreateReviewDecisionService(reviewerDbContext, reviewerAccessor, scenario.Clock);
        var command = new EvidenceCandidateDecisionCommand(
            review.ReviewId,
            EvidenceDecisionTypes.Accept,
            "candidate accepts evidence",
            null,
            "candidate-replay-001");

        var first = await reviewerService.CreateCandidateDecisionAsync(command, CancellationToken.None);
        var second = await reviewerService.CreateCandidateDecisionAsync(command, CancellationToken.None);

        Assert.False(first.IdempotentReplay);
        Assert.True(second.IdempotentReplay);
        Assert.Equal(first.CandidateDecisionId, second.CandidateDecisionId);
        Assert.Equal(EvidenceCandidateDecisionStates.Candidate, first.State);
        Assert.Equal(1, await reviewerDbContext.EvidenceCandidateDecisions.CountAsync(candidate => candidate.ReviewRequestId == review.ReviewId));
    }

    [Fact]
    public async Task Candidate_creator_cannot_apply_authoritative_decision()
    {
        var scenario = await CreateAvailableEvidenceAsync("creator-cannot-approve");
        var review = await CreateReviewWithAssignmentsAsync(
            scenario,
            assignReviewer: true,
            assignApprover: true,
            reviewerSubject: "candidate-creator",
            approverSubject: "candidate-creator");
        var actorAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "candidate-creator");
        await using var actorDbContext = fixture.CreateDbContext(actorAccessor);
        var actorService = CreateReviewDecisionService(actorDbContext, actorAccessor, scenario.Clock);
        var candidate = await actorService.CreateCandidateDecisionAsync(
            new EvidenceCandidateDecisionCommand(review.ReviewId, EvidenceDecisionTypes.Accept, "self approval candidate", null, "self-candidate-001"),
            CancellationToken.None);

        await Assert.ThrowsAsync<EvidenceReviewDecisionForbiddenException>(() => actorService.ApplyDecisionAsync(
            new EvidenceApplyDecisionCommand(review.ReviewId, candidate.CandidateDecisionId, EvidenceDecisionTypes.Accept, candidate.Version, null, "self-approve-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Assigned_approver_accepts_candidate_and_records_authoritative_state()
    {
        var scenario = await CreateAvailableEvidenceAsync("approver-accepts");
        var review = await CreateReviewWithAssignmentsAsync(scenario, assignReviewer: true, assignApprover: true);
        var candidate = await CreateCandidateAsync(scenario, review.ReviewId, EvidenceDecisionTypes.Accept, "acceptance candidate", "accept-candidate-001");
        var approverAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "evidence-approver");
        await using var approverDbContext = fixture.CreateDbContext(approverAccessor);
        var approverService = CreateReviewDecisionService(approverDbContext, approverAccessor, scenario.Clock);

        var result = await approverService.ApplyDecisionAsync(
            new EvidenceApplyDecisionCommand(review.ReviewId, candidate.CandidateDecisionId, EvidenceDecisionTypes.Accept, candidate.Version, null, "accept-apply-001"),
            CancellationToken.None);

        var storedReview = await approverDbContext.EvidenceReviewRequests.SingleAsync(item => item.Id == review.ReviewId);
        var storedCandidate = await approverDbContext.EvidenceCandidateDecisions.SingleAsync(item => item.Id == candidate.CandidateDecisionId);

        Assert.False(result.IdempotentReplay);
        Assert.Equal(EvidenceReviewStates.Accepted, result.ReviewState);
        Assert.Equal(EvidenceCandidateDecisionStates.Accepted, result.CandidateState);
        Assert.Equal(2, result.CandidateVersion);
        Assert.Equal(EvidenceReviewStates.Accepted, storedReview.State);
        Assert.Equal(EvidenceCandidateDecisionStates.Accepted, storedCandidate.State);
        Assert.True(await approverDbContext.AuditEntries.AnyAsync(audit => audit.Action == "EvidenceReview.Accepted"));
        Assert.True(await approverDbContext.OutboxMessages.AnyAsync(message => message.MessageType == "EvidenceDecisionApplied"));
    }

    [Fact]
    public async Task Stale_candidate_version_is_rejected()
    {
        var scenario = await CreateAvailableEvidenceAsync("stale-version");
        var review = await CreateReviewWithAssignmentsAsync(scenario, assignReviewer: true, assignApprover: true);
        var candidate = await CreateCandidateAsync(scenario, review.ReviewId, EvidenceDecisionTypes.Accept, "stale candidate", "stale-candidate-001");
        var approverAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "evidence-approver");
        await using var approverDbContext = fixture.CreateDbContext(approverAccessor);
        var approverService = CreateReviewDecisionService(approverDbContext, approverAccessor, scenario.Clock);

        await Assert.ThrowsAsync<EvidenceReviewDecisionConflictException>(() => approverService.ApplyDecisionAsync(
            new EvidenceApplyDecisionCommand(review.ReviewId, candidate.CandidateDecisionId, EvidenceDecisionTypes.Accept, ExpectedCandidateVersion: 2, null, "stale-apply-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Reject_and_correction_decisions_require_reason()
    {
        var scenario = await CreateAvailableEvidenceAsync("reason-required");
        var review = await CreateReviewWithAssignmentsAsync(scenario, assignReviewer: true, assignApprover: true);
        var candidate = await CreateCandidateAsync(scenario, review.ReviewId, EvidenceDecisionTypes.Reject, "rejection candidate", "reject-candidate-001");
        var approverAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "evidence-approver");
        await using var approverDbContext = fixture.CreateDbContext(approverAccessor);
        var approverService = CreateReviewDecisionService(approverDbContext, approverAccessor, scenario.Clock);

        await Assert.ThrowsAsync<EvidenceReviewDecisionValidationException>(() => approverService.ApplyDecisionAsync(
            new EvidenceApplyDecisionCommand(review.ReviewId, candidate.CandidateDecisionId, EvidenceDecisionTypes.Reject, candidate.Version, null, "reject-apply-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Supersede_decision_preserves_previous_candidate_relationship()
    {
        var scenario = await CreateAvailableEvidenceAsync("supersede");
        var review = await CreateReviewWithAssignmentsAsync(scenario, assignReviewer: true, assignApprover: true);
        var firstCandidate = await CreateCandidateAsync(scenario, review.ReviewId, EvidenceDecisionTypes.Accept, "initial candidate", "supersede-first-001");
        var replacementCandidate = await CreateCandidateAsync(
            scenario,
            review.ReviewId,
            EvidenceDecisionTypes.Supersede,
            "supersede the initial candidate",
            "supersede-second-001",
            firstCandidate.CandidateDecisionId);
        var approverAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "evidence-approver");
        await using var approverDbContext = fixture.CreateDbContext(approverAccessor);
        var approverService = CreateReviewDecisionService(approverDbContext, approverAccessor, scenario.Clock);

        var result = await approverService.ApplyDecisionAsync(
            new EvidenceApplyDecisionCommand(review.ReviewId, replacementCandidate.CandidateDecisionId, EvidenceDecisionTypes.Supersede, replacementCandidate.Version, "superseded by corrected candidate", "supersede-apply-001"),
            CancellationToken.None);

        var superseded = await approverDbContext.EvidenceCandidateDecisions.SingleAsync(candidate => candidate.Id == firstCandidate.CandidateDecisionId);
        var replacement = await approverDbContext.EvidenceCandidateDecisions.SingleAsync(candidate => candidate.Id == replacementCandidate.CandidateDecisionId);

        Assert.Equal(EvidenceReviewStates.Superseded, result.ReviewState);
        Assert.Equal(EvidenceCandidateDecisionStates.Superseded, superseded.State);
        Assert.Equal(EvidenceCandidateDecisionStates.Superseded, replacement.State);
        Assert.Equal(firstCandidate.CandidateDecisionId, replacement.SupersedesDecisionId);
    }

    [Fact]
    public async Task Cross_workspace_actor_cannot_request_review_for_another_workspace_evidence()
    {
        var scenario = await CreateAvailableEvidenceAsync("cross-workspace-review");
        var otherWorkspaceId = WorkspaceId.New();
        var otherAccessor = CreateRequestContext(scenario.TenantId, otherWorkspaceId, "other-workspace-actor");
        await SeedTenantAndWorkspaceAsync(scenario.TenantId, otherWorkspaceId, otherAccessor, seedTenant: false);
        await using var otherDbContext = fixture.CreateDbContext(otherAccessor);
        var otherService = CreateReviewDecisionService(otherDbContext, otherAccessor, scenario.Clock);

        await Assert.ThrowsAsync<EvidenceReviewDecisionForbiddenException>(() => otherService.RequestReviewAsync(
            new EvidenceReviewRequestCommand(scenario.EvidenceId, scenario.VersionId, "EvidenceReview", null, "cross-workspace-review-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Review_request_rolls_back_when_outbox_write_fails()
    {
        var scenario = await CreateAvailableEvidenceAsync("review-rollback");
        await using var failingDbContext = fixture.CreateDbContext(scenario.Accessor);
        var failingService = CreateReviewDecisionService(
            failingDbContext,
            scenario.Accessor,
            scenario.Clock,
            new ThrowingOutboxWriter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.RequestReviewAsync(
            new EvidenceReviewRequestCommand(scenario.EvidenceId, scenario.VersionId, "EvidenceReview", null, "review-rollback-001"),
            CancellationToken.None));

        await using var verificationDbContext = fixture.CreateDbContext(scenario.Accessor);
        Assert.Equal(0, await verificationDbContext.EvidenceReviewRequests.CountAsync());
    }

    private async Task<EvidenceReviewRequestResult> CreateReviewWithAssignmentsAsync(
        AvailableEvidence scenario,
        bool assignReviewer,
        bool assignApprover,
        string reviewerSubject = "evidence-reviewer",
        string approverSubject = "evidence-approver")
    {
        await using var ownerDbContext = fixture.CreateDbContext(scenario.Accessor);
        var ownerService = CreateReviewDecisionService(ownerDbContext, scenario.Accessor, scenario.Clock);
        var review = await RequestReviewAsync(ownerService, scenario, $"review-{Guid.NewGuid():N}");
        await SeedProductAuthorityAsync(
            scenario,
            review.ReviewId,
            ("evidence-owner", ProductAuthorityPermissions.ManageEvidenceReviewAssignments),
            (reviewerSubject, ProductAuthorityPermissions.CreateEvidenceCandidateDecision),
            (approverSubject, ProductAuthorityPermissions.ApplyEvidenceDecision));

        if (assignReviewer)
        {
            await ownerService.AssignReviewerAsync(
                new EvidenceReviewerAssignmentCommand(review.ReviewId, reviewerSubject, ProductAuthorityPermissions.CreateEvidenceCandidateDecision, $"assign-reviewer-{Guid.NewGuid():N}"),
                CancellationToken.None);
        }

        if (assignApprover)
        {
            await ownerService.AssignReviewerAsync(
                new EvidenceReviewerAssignmentCommand(review.ReviewId, approverSubject, ProductAuthorityPermissions.ApplyEvidenceDecision, $"assign-approver-{Guid.NewGuid():N}"),
                CancellationToken.None);
        }

        return review;
    }

    private async Task SeedProductAuthorityAsync(
        AvailableEvidence scenario,
        Guid reviewId,
        params (string Subject, string PermissionKey)[] assignments)
    {
        await using var dbContext = fixture.CreateDbContext(scenario.Accessor);
        foreach (var assignment in assignments.Distinct())
        {
            var actor = dbContext.ProductActors.Local
                .SingleOrDefault(item => item.Subject == assignment.Subject);
            actor ??= await dbContext.ProductActors
                .SingleOrDefaultAsync(item => item.Subject == assignment.Subject);
            if (actor is null)
            {
                actor = new ProductActor
                {
                    TenantId = scenario.TenantId,
                    WorkspaceId = scenario.WorkspaceId,
                    Subject = assignment.Subject,
                    DisplayName = assignment.Subject,
                    State = ProductActorStates.Active,
                    CreatedBy = "authority-test-seeder",
                    CreatedAt = scenario.Clock.UtcNow
                };
                dbContext.ProductActors.Add(actor);
            }

            var resourceId = assignment.PermissionKey == ProductAuthorityPermissions.ManageEvidenceReviewAssignments
                ? ProductAuthorityResourceIds.Any
                : reviewId.ToString("D");
            var exists = await dbContext.ProductPermissionAssignments.AnyAsync(item =>
                item.ActorSubject == assignment.Subject
                && item.PermissionKey == assignment.PermissionKey
                && item.ResourceType == ProductAuthorityResourceTypes.EvidenceReview
                && item.ResourceId == resourceId
                && item.State == ProductPermissionAssignmentStates.Active);
            if (!exists)
            {
                dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
                {
                    TenantId = scenario.TenantId,
                    WorkspaceId = scenario.WorkspaceId,
                    ActorId = actor.Id,
                    ActorSubject = assignment.Subject,
                    PermissionKey = assignment.PermissionKey,
                    ResourceType = ProductAuthorityResourceTypes.EvidenceReview,
                    ResourceId = resourceId,
                    State = ProductPermissionAssignmentStates.Active,
                    AssignedBy = "authority-test-seeder",
                    AssignedAt = scenario.Clock.UtcNow,
                    EffectiveFrom = scenario.Clock.UtcNow,
                    IdempotencyKey = $"authority-{Guid.NewGuid():N}",
                    RequestHash = Sha256(Encoding.UTF8.GetBytes($"{assignment.Subject}|{assignment.PermissionKey}|{resourceId}"))
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<EvidenceCandidateDecisionResult> CreateCandidateAsync(
        AvailableEvidence scenario,
        Guid reviewId,
        string decisionType,
        string summary,
        string idempotencyKey,
        Guid? supersedesDecisionId = null)
    {
        var reviewerAccessor = CreateRequestContext(scenario.TenantId, scenario.WorkspaceId, "evidence-reviewer");
        await using var reviewerDbContext = fixture.CreateDbContext(reviewerAccessor);
        var reviewerService = CreateReviewDecisionService(reviewerDbContext, reviewerAccessor, scenario.Clock);

        return await reviewerService.CreateCandidateDecisionAsync(
            new EvidenceCandidateDecisionCommand(reviewId, decisionType, summary, supersedesDecisionId, idempotencyKey),
            CancellationToken.None);
    }

    private static Task<EvidenceReviewRequestResult> RequestReviewAsync(
        EvidenceReviewDecisionService service,
        AvailableEvidence scenario,
        string idempotencyKey)
    {
        return service.RequestReviewAsync(
            new EvidenceReviewRequestCommand(scenario.EvidenceId, scenario.VersionId, "EvidenceReview", null, idempotencyKey),
            CancellationToken.None);
    }

    private async Task<AvailableEvidence> CreateAvailableEvidenceAsync(
        string scenarioName,
        string actor = "evidence-owner")
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId, actor);
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        var content = Encoding.UTF8.GetBytes($"available evidence {scenarioName}");
        EvidenceRegistrationResult registration;

        await using (var registrationDbContext = fixture.CreateDbContext(accessor))
        {
            var registrationService = CreateRegistrationService(registrationDbContext, accessor, clock);
            registration = await registrationService.RegisterInitialVersionAsync(
                new EvidenceRegistrationRequest(
                    "Document",
                    "Internal",
                    $"{scenarioName}.txt",
                    "text/plain",
                    content.Length,
                    Sha256(content),
                    $"registered/{scenarioName}/{Guid.NewGuid():N}",
                    "default",
                    $"registration-{scenarioName}-{Guid.NewGuid():N}"),
                CancellationToken.None);
        }

        await using (var contentDbContext = fixture.CreateDbContext(accessor))
        {
            var store = new DevelopmentEvidenceObjectStore();
            var contentService = CreateContentService(contentDbContext, accessor, store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean), clock);
            var allocation = await contentService.AllocateUploadAsync(
                new EvidenceUploadAllocationRequest(registration.EvidenceId, $"allocation-{scenarioName}-{Guid.NewGuid():N}"),
                CancellationToken.None);
            await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);
            await contentService.ConfirmContentReceivedAsync(
                new EvidenceContentReceiptRequest(registration.EvidenceId, registration.VersionId, allocation.StorageObjectReference, $"receipt-{scenarioName}-{Guid.NewGuid():N}"),
                CancellationToken.None);
        }

        return new AvailableEvidence(tenantId, workspaceId, registration.EvidenceId, registration.VersionId, accessor, clock);
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
                DisplayName = "Synthetic Review Tenant",
                ExternalAuthority = $"synthetic-review-{tenantId}-{Guid.NewGuid():N}",
                LifecycleState = "Active",
                CreatedBy = "test",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Synthetic Review Workspace {Guid.NewGuid():N}",
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
        return new EvidenceRegistrationService(dbContext, accessor, clock, outboxWriter, rls);
    }

    private static EvidenceContentService CreateContentService(
        DataLooMStudio.Runtime.Persistence.DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        DevelopmentEvidenceObjectStore store,
        IEvidenceMalwareScanner scanner,
        IClock clock)
    {
        var rls = new PostgresRlsSessionContext(dbContext, accessor);
        return new EvidenceContentService(
            dbContext,
            accessor,
            clock,
            new EfOutboxWriter(dbContext),
            rls,
            store,
            scanner);
    }

    private static EvidenceReviewDecisionService CreateReviewDecisionService(
        DataLooMStudio.Runtime.Persistence.DataLooMDbContext dbContext,
        IRequestContextAccessor accessor,
        IClock clock,
        IOutboxWriter? outboxWriter = null)
    {
        var rls = new PostgresRlsSessionContext(dbContext, accessor);
        return new EvidenceReviewDecisionService(
            dbContext,
            accessor,
            clock,
            outboxWriter ?? new EfOutboxWriter(dbContext),
            new ProductAuthorityService(dbContext, accessor, clock),
            rls);
    }

    private static string Sha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed record AvailableEvidence(
        TenantId TenantId,
        WorkspaceId WorkspaceId,
        EvidenceId EvidenceId,
        EvidenceVersionId VersionId,
        RequestContextAccessor Accessor,
        MutableClock Clock);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;
    }

    private sealed class FakeMalwareScanner(EvidenceMalwareScanOutcome outcome) : IEvidenceMalwareScanner
    {
        public Task<EvidenceMalwareScanResult> ScanAsync(
            EvidenceMalwareScanRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new EvidenceMalwareScanResult(
                outcome,
                "fake-review-scanner",
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