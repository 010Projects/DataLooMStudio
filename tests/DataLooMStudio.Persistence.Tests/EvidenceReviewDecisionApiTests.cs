using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using DataLooMStudio.Infrastructure.SecurityScanning;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataLooMStudio.Persistence.Tests;

public sealed class EvidenceReviewDecisionApiTests(
    PostgresFixture fixture,
    WebApplicationFactory<Program> factory) : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Api_completes_evidence_review_decision_path()
    {
        var content = Encoding.UTF8.GetBytes("api review decision content");
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        var store = new DevelopmentEvidenceObjectStore();
        using var client = CreateClient(store);
        AddContextHeaders(client, tenantId, workspaceId, "api-review-owner");
        var registration = await RegisterAvailableEvidenceAsync(client, store, workspaceId, content, "api-review-happy");

        var reviewResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/versions/{registration.VersionId}/reviews",
            new EvidenceReviewRequestApiRequest("EvidenceReview", null, "api-review-request-001"),
            CancellationToken.None);
        var review = await reviewResponse.Content.ReadFromJsonAsync<EvidenceReviewRequestApiResponse>(
            cancellationToken: CancellationToken.None);
        await SeedProductAuthorityAsync(
            tenantId,
            workspaceId,
            review!.ReviewId,
            ("api-review-owner", ProductAuthorityPermissions.ManageEvidenceReviewAssignments),
            ("api-reviewer", ProductAuthorityPermissions.CreateEvidenceCandidateDecision),
            ("api-approver", ProductAuthorityPermissions.ApplyEvidenceDecision));
        var reviewerAssignment = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence-reviews/{review.ReviewId}/assignments",
            new EvidenceReviewerAssignmentApiRequest("api-reviewer", ProductAuthorityPermissions.CreateEvidenceCandidateDecision, "api-reviewer-assignment-001"),
            CancellationToken.None);
        var approverAssignment = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence-reviews/{review.ReviewId}/assignments",
            new EvidenceReviewerAssignmentApiRequest("api-approver", ProductAuthorityPermissions.ApplyEvidenceDecision, "api-approver-assignment-001"),
            CancellationToken.None);

        SetActorHeader(client, "api-reviewer");
        var candidateResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence-reviews/{review.ReviewId}/candidate-decisions",
            new EvidenceCandidateDecisionApiRequest("Accept", "candidate accepts the evidence", null, "api-candidate-001"),
            CancellationToken.None);
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<EvidenceCandidateDecisionApiResponse>(
            cancellationToken: CancellationToken.None);

        SetActorHeader(client, "api-approver");
        var applyResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence-reviews/{review.ReviewId}/candidate-decisions/{candidate!.CandidateDecisionId}/accept",
            new EvidenceApplyDecisionApiRequest(candidate.Version, null, "api-accept-001"),
            CancellationToken.None);
        var applied = await applyResponse.Content.ReadFromJsonAsync<EvidenceAppliedDecisionApiResponse>(
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, reviewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reviewerAssignment.StatusCode);
        Assert.Equal(HttpStatusCode.Created, approverAssignment.StatusCode);
        Assert.Equal(HttpStatusCode.Created, candidateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
        Assert.Equal("Accepted", applied!.ReviewState);
        Assert.Equal("Accepted", applied.CandidateState);
        Assert.False(applied.IdempotentReplay);
    }

    [Fact]
    public async Task Api_denies_billing_role_as_evidence_review_authority()
    {
        var content = Encoding.UTF8.GetBytes("api denied role content");
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        var store = new DevelopmentEvidenceObjectStore();
        using var client = CreateClient(store);
        AddContextHeaders(client, tenantId, workspaceId, "api-review-owner-denied");
        var registration = await RegisterAvailableEvidenceAsync(client, store, workspaceId, content, "api-review-denied");
        var reviewResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/versions/{registration.VersionId}/reviews",
            new EvidenceReviewRequestApiRequest("EvidenceReview", null, "api-denied-review-001"),
            CancellationToken.None);
        var review = await reviewResponse.Content.ReadFromJsonAsync<EvidenceReviewRequestApiResponse>(
            cancellationToken: CancellationToken.None);
        await SeedProductAuthorityAsync(
            tenantId,
            workspaceId,
            review!.ReviewId,
            ("api-review-owner-denied", ProductAuthorityPermissions.ManageEvidenceReviewAssignments));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence-reviews/{review.ReviewId}/assignments",
            new EvidenceReviewerAssignmentApiRequest("api-billing-admin", "BillingAdministrator", "api-denied-assignment-001"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(DevelopmentEvidenceObjectStore store)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DataLooM"] = fixture.ApplicationConnectionString
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEvidenceObjectStore>();
                services.RemoveAll<IEvidenceMalwareScanner>();
                services.AddSingleton<IEvidenceObjectStore>(store);
                services.AddSingleton<IEvidenceMalwareScanner>(new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }).CreateClient();
    }

    private static async Task<EvidenceRegistrationApiResponse> RegisterAvailableEvidenceAsync(
        HttpClient client,
        DevelopmentEvidenceObjectStore store,
        WorkspaceId workspaceId,
        byte[] content,
        string scenarioName)
    {
        var registrationResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence",
            new EvidenceRegistrationApiRequest(
                "Document",
                "Internal",
                $"{scenarioName}.txt",
                "text/plain",
                content.Length,
                Sha256(content),
                $"api/{scenarioName}/{Guid.NewGuid():N}",
                "default",
                $"api-registration-{scenarioName}-{Guid.NewGuid():N}"),
            CancellationToken.None);
        registrationResponse.EnsureSuccessStatusCode();
        var registration = (await registrationResponse.Content.ReadFromJsonAsync<EvidenceRegistrationApiResponse>(
            cancellationToken: CancellationToken.None))!;
        var allocationResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/upload-allocation",
            new EvidenceUploadAllocationApiRequest($"api-allocation-{scenarioName}-{Guid.NewGuid():N}"),
            CancellationToken.None);
        allocationResponse.EnsureSuccessStatusCode();
        var allocation = (await allocationResponse.Content.ReadFromJsonAsync<EvidenceUploadAllocationApiResponse>(
            cancellationToken: CancellationToken.None))!;
        await store.StoreObjectAsync(allocation.StorageObjectReference, content, "text/plain", CancellationToken.None);
        var receiptResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/versions/{allocation.VersionId}/content-received",
            new EvidenceContentReceivedApiRequest(allocation.StorageObjectReference, $"api-receipt-{scenarioName}-{Guid.NewGuid():N}"),
            CancellationToken.None);
        receiptResponse.EnsureSuccessStatusCode();

        return registration;
    }

    private static void AddContextHeaders(HttpClient client, TenantId tenantId, WorkspaceId workspaceId, string actor)
    {
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.WorkspaceHeader, workspaceId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, actor);
    }

    private static void SetActorHeader(HttpClient client, string actor)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.ActorHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, actor);
    }

    private async Task SeedTenantAndWorkspaceAsync(TenantId tenantId, WorkspaceId workspaceId)
    {
        var accessor = new DataLooMStudio.Infrastructure.RequestContext.RequestContextAccessor
        {
            Current = new RequestContext(
                tenantId,
                workspaceId,
                new PrincipalSubject("test-actor"),
                $"corr-{Guid.NewGuid():N}")
        };
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = "Synthetic Review API Tenant",
            ExternalAuthority = $"synthetic-api-review-{tenantId}-{Guid.NewGuid():N}",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Synthetic Review API Workspace {Guid.NewGuid():N}",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedProductAuthorityAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        Guid reviewId,
        params (string Subject, string PermissionKey)[] assignments)
    {
        var accessor = new DataLooMStudio.Infrastructure.RequestContext.RequestContextAccessor
        {
            Current = new RequestContext(
                tenantId,
                workspaceId,
                new PrincipalSubject("authority-test-seeder"),
                $"corr-{Guid.NewGuid():N}")
        };
        await using var dbContext = fixture.CreateDbContext(accessor);
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
                    TenantId = tenantId,
                    WorkspaceId = workspaceId,
                    Subject = assignment.Subject,
                    DisplayName = assignment.Subject,
                    State = ProductActorStates.Active,
                    AuthorityVersion = 1,
                    AuthorityChangedAt = DateTimeOffset.UtcNow,
                    CreatedBy = "authority-test-seeder",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.ProductActors.Add(actor);
            }

            if (!await dbContext.ProductTenantMemberships.AnyAsync(item =>
                item.ActorSubject == assignment.Subject
                && item.State == ProductMembershipStates.Active))
            {
                dbContext.ProductTenantMemberships.Add(new ProductTenantMembership
                {
                    TenantId = tenantId,
                    ActorId = actor.Id,
                    ActorSubject = assignment.Subject,
                    State = ProductMembershipStates.Active,
                    AuthorityVersion = actor.AuthorityVersion,
                    GrantedBy = "authority-test-seeder",
                    GrantedAt = DateTimeOffset.UtcNow,
                    IdempotencyKey = $"api-tenant-authority-{Guid.NewGuid():N}",
                    RequestHash = Sha256(Encoding.UTF8.GetBytes($"{assignment.Subject}|tenant"))
                });
            }

            if (!await dbContext.ProductWorkspaceMemberships.AnyAsync(item =>
                item.ActorSubject == assignment.Subject
                && item.State == ProductMembershipStates.Active))
            {
                dbContext.ProductWorkspaceMemberships.Add(new ProductWorkspaceMembership
                {
                    TenantId = tenantId,
                    WorkspaceId = workspaceId,
                    ActorId = actor.Id,
                    ActorSubject = assignment.Subject,
                    State = ProductMembershipStates.Active,
                    AuthorityVersion = actor.AuthorityVersion,
                    GrantedBy = "authority-test-seeder",
                    GrantedAt = DateTimeOffset.UtcNow,
                    IdempotencyKey = $"api-workspace-authority-{Guid.NewGuid():N}",
                    RequestHash = Sha256(Encoding.UTF8.GetBytes($"{assignment.Subject}|workspace"))
                });
            }

            var resourceId = assignment.PermissionKey == ProductAuthorityPermissions.ManageEvidenceReviewAssignments
                ? ProductAuthorityResourceIds.Any
                : reviewId.ToString("D");
            dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                ActorId = actor.Id,
                ActorSubject = assignment.Subject,
                PermissionKey = assignment.PermissionKey,
                ResourceType = ProductAuthorityResourceTypes.EvidenceReview,
                ResourceId = resourceId,
                State = ProductPermissionAssignmentStates.Active,
                AuthorityVersion = actor.AuthorityVersion,
                AssignedBy = "authority-test-seeder",
                AssignedAt = DateTimeOffset.UtcNow,
                EffectiveFrom = DateTimeOffset.UtcNow,
                IdempotencyKey = $"api-authority-{Guid.NewGuid():N}",
                RequestHash = Sha256(Encoding.UTF8.GetBytes($"{assignment.Subject}|{assignment.PermissionKey}|{resourceId}"))
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static string Sha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private sealed record EvidenceRegistrationApiRequest(
        string EvidenceType,
        string Classification,
        string OriginalFileName,
        string MediaType,
        long DeclaredSize,
        string ContentHash,
        string StorageObjectReference,
        string RetentionPolicyKey,
        string? IdempotencyKey);

    private sealed record EvidenceRegistrationApiResponse(
        string EvidenceId,
        string VersionId,
        string LifecycleState,
        string IntegrityState,
        DateTimeOffset CreatedAt,
        bool IdempotentReplay);

    private sealed record EvidenceUploadAllocationApiRequest(string? IdempotencyKey);

    private sealed record EvidenceUploadAllocationApiResponse(
        string EvidenceId,
        string VersionId,
        Guid AllocationId,
        string StorageObjectReference,
        string UploadAuthority,
        DateTimeOffset ExpiresAt,
        string PermittedOperation,
        long MaxSize,
        string MediaType,
        bool IdempotentReplay);

    private sealed record EvidenceContentReceivedApiRequest(
        string StorageObjectReference,
        string? IdempotencyKey);

    private sealed record EvidenceReviewRequestApiRequest(
        string ReviewKind,
        DateTimeOffset? DueAt,
        string? IdempotencyKey);

    private sealed record EvidenceReviewRequestApiResponse(
        Guid ReviewId,
        string EvidenceId,
        string EvidenceVersionId,
        string State,
        int Version,
        DateTimeOffset RequestedAt,
        bool IdempotentReplay);

    private sealed record EvidenceReviewerAssignmentApiRequest(
        string ReviewerSubject,
        string PermissionKey,
        string? IdempotencyKey);

    private sealed record EvidenceCandidateDecisionApiRequest(
        string DecisionType,
        string Summary,
        Guid? SupersedesDecisionId,
        string? IdempotencyKey);

    private sealed record EvidenceCandidateDecisionApiResponse(
        Guid CandidateDecisionId,
        Guid ReviewId,
        string DecisionType,
        string State,
        int Version,
        bool IdempotentReplay);

    private sealed record EvidenceApplyDecisionApiRequest(
        int ExpectedCandidateVersion,
        string? Reason,
        string? IdempotencyKey);

    private sealed record EvidenceAppliedDecisionApiResponse(
        Guid ReviewId,
        Guid CandidateDecisionId,
        string ReviewState,
        string CandidateState,
        int CandidateVersion,
        DateTimeOffset DecidedAt,
        bool IdempotentReplay);

    private sealed class FakeMalwareScanner(EvidenceMalwareScanOutcome outcome) : IEvidenceMalwareScanner
    {
        public Task<EvidenceMalwareScanResult> ScanAsync(
            EvidenceMalwareScanRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new EvidenceMalwareScanResult(
                outcome,
                "fake-review-api-scanner",
                "1.0",
                outcome == EvidenceMalwareScanOutcome.Clean ? null : $"Synthetic {outcome} result."));
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestReviewDecision";

        public const string TenantHeader = "X-Test-Tenant-Id";

        public const string WorkspaceHeader = "X-Test-Workspace-Id";

        public const string ActorHeader = "X-Test-Actor";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>();
            if (Request.Headers.TryGetValue(TenantHeader, out var tenantId))
            {
                claims.Add(new Claim("tid", tenantId.ToString()));
            }

            if (Request.Headers.TryGetValue(WorkspaceHeader, out var workspaceId))
            {
                claims.Add(new Claim("workspace_id", workspaceId.ToString()));
            }

            if (Request.Headers.TryGetValue(ActorHeader, out var actor))
            {
                claims.Add(new Claim("oid", actor.ToString()));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}