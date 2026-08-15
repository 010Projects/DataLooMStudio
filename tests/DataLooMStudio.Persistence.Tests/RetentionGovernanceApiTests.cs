using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Retention;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataLooMStudio.Persistence.Tests;

public sealed class RetentionGovernanceApiTests(
    PostgresFixture fixture,
    WebApplicationFactory<Program> factory) : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Api_defines_retention_policy_through_product_authority_boundary()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantWorkspaceAndAuthorityAsync(tenantId, workspaceId, "api-retention-admin", "api-retain-seven-years");
        using var client = CreateClient();
        AddContextHeaders(client, tenantId, workspaceId, "api-retention-admin");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/retention-policies",
            new RetentionPolicyApiRequest("api-retain-seven-years", "API seven year retention", 2555, true, "api-ret-policy-001"),
            CancellationToken.None);
        var body = await response.Content.ReadFromJsonAsync<RetentionPolicyApiResponse>(
            cancellationToken: CancellationToken.None);
        var accessor = CreateRequestContext(tenantId, workspaceId, "api-retention-admin");
        await using var dbContext = fixture.CreateDbContext(accessor);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("api-retain-seven-years", body!.PolicyKey);
        Assert.False(body.IdempotentReplay);
        Assert.Equal(1, await dbContext.RetentionPolicies.CountAsync());
        Assert.Equal(1, await dbContext.OutboxMessages.CountAsync(message => message.MessageType == "RetentionPolicyDefined"));
        Assert.Contains(await dbContext.AuditEntries.Select(audit => audit.Action).ToArrayAsync(), action => action == "Retention.PolicyDefined");
    }

    [Fact]
    public async Task Api_releases_legal_hold_and_evaluates_deletion_eligibility_without_physical_deletion()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var evidenceId = EvidenceId.New();
        var versionId = EvidenceVersionId.New();
        var legalHoldId = Guid.NewGuid();
        await SeedReleaseEligibilityScenarioAsync(tenantId, workspaceId, evidenceId, versionId, legalHoldId);
        using var requesterClient = CreateClient();
        AddContextHeaders(requesterClient, tenantId, workspaceId, "api-release-requester");

        var requestResponse = await requesterClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{evidenceId}/legal-holds/{legalHoldId}/release-requests",
            new LegalHoldReleaseRequestApiRequest("Legal matter closed", "api-release-request-001"),
            CancellationToken.None);
        var requestBody = await requestResponse.Content.ReadFromJsonAsync<LegalHoldReleaseRequestApiResponse>(
            cancellationToken: CancellationToken.None);
        using var approverClient = CreateClient();
        AddContextHeaders(approverClient, tenantId, workspaceId, "api-release-approver");

        var approvalResponse = await approverClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/legal-hold-release-requests/{requestBody!.ReleaseRequestId}/approve",
            new LegalHoldReleaseApprovalApiRequest("Independent approval", "api-release-approval-001"),
            CancellationToken.None);
        var approvalBody = await approvalResponse.Content.ReadFromJsonAsync<LegalHoldReleaseApprovalApiResponse>(
            cancellationToken: CancellationToken.None);
        using var evaluatorClient = CreateClient();
        AddContextHeaders(evaluatorClient, tenantId, workspaceId, "api-retention-evaluator");

        var eligibilityResponse = await evaluatorClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{evidenceId}/deletion-eligibility-evaluations",
            new DeletionEligibilityApiRequest("api-deletion-eligibility-001"),
            CancellationToken.None);
        var eligibilityBody = await eligibilityResponse.Content.ReadFromJsonAsync<DeletionEligibilityApiResponse>(
            cancellationToken: CancellationToken.None);
        var accessor = CreateRequestContext(tenantId, workspaceId, "api-retention-evaluator");
        await using var dbContext = fixture.CreateDbContext(accessor);
        var evidence = await dbContext.EvidenceRecords.SingleAsync(item => item.Id == evidenceId);

        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, eligibilityResponse.StatusCode);
        Assert.NotNull(requestBody);
        Assert.NotNull(approvalBody);
        Assert.NotNull(eligibilityBody);
        Assert.False(approvalBody!.EvidenceUnderLegalHold);
        Assert.False(approvalBody.EvidencePhysicallyDeleted);
        Assert.True(eligibilityBody!.IsEligible);
        Assert.False(eligibilityBody.EvidencePhysicallyDeleted);
        Assert.False(evidence.IsUnderLegalHold);
        Assert.NotEqual("Deleted", evidence.LifecycleState);
        Assert.Equal(1, await dbContext.EvidenceRecords.CountAsync(item => item.Id == evidenceId));
        Assert.Equal(1, await dbContext.EvidenceVersions.CountAsync(version => version.EvidenceId == evidenceId));
    }

    private HttpClient CreateClient()
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
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }).CreateClient();
    }

    private async Task SeedTenantWorkspaceAndAuthorityAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        string actorSubject,
        string policyKey)
    {
        var accessor = CreateRequestContext(tenantId, workspaceId, actorSubject);
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = "Retention API Tenant",
            ExternalAuthority = $"retention-api-{tenantId}",
            LifecycleState = "Active",
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Retention API Workspace",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var actor = new ProductActor
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Subject = actorSubject,
            DisplayName = actorSubject,
            ActorType = ProductActorTypes.Human,
            State = ProductActorStates.Active,
            AuthorityVersion = 1,
            AuthorityChangedAt = DateTimeOffset.UtcNow,
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ProductActors.Add(actor);
        dbContext.ProductTenantMemberships.Add(new ProductTenantMembership
        {
            TenantId = tenantId,
            ActorId = actor.Id,
            ActorSubject = actorSubject,
            State = ProductMembershipStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            GrantedBy = "retention-api-test",
            GrantedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"tenant-membership-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actorSubject}|tenant")
        });
        dbContext.ProductWorkspaceMemberships.Add(new ProductWorkspaceMembership
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ActorId = actor.Id,
            ActorSubject = actorSubject,
            State = ProductMembershipStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            GrantedBy = "retention-api-test",
            GrantedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"workspace-membership-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actorSubject}|workspace")
        });
        dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ActorId = actor.Id,
            ActorSubject = actorSubject,
            PermissionKey = ProductAuthorityPermissions.ManageRetentionPolicy,
            ResourceType = ProductAuthorityResourceTypes.GovernanceRetention,
            ResourceId = policyKey,
            State = ProductPermissionAssignmentStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            AssignedBy = "retention-api-test",
            AssignedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
            IdempotencyKey = $"permission-assignment-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actorSubject}|{policyKey}")
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedReleaseEligibilityScenarioAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        EvidenceId evidenceId,
        EvidenceVersionId versionId,
        Guid legalHoldId)
    {
        var accessor = CreateRequestContext(tenantId, workspaceId, "api-release-seed");
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = "Retention Release API Tenant",
            ExternalAuthority = $"retention-release-api-{tenantId}",
            LifecycleState = "Active",
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Retention Release API Workspace",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.RetentionPolicies.Add(new RetentionPolicy
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            PolicyKey = "api-expired-policy",
            Description = "API expired policy",
            RetainForDays = 1,
            LegalHoldOverridesDeletion = true,
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"retention-policy-{Guid.NewGuid():N}",
            RequestHash = Sha256("api-expired-policy")
        });
        dbContext.EvidenceRecords.Add(new EvidenceRecord
        {
            Id = evidenceId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            LineageId = LineageId.New(),
            CurrentVersionId = versionId,
            EvidenceType = "Document",
            Classification = "Internal",
            LifecycleState = "Available",
            RegisteredBy = "retention-api-test",
            BlobName = $"retention-api/{evidenceId}",
            ContentType = "text/plain",
            ContentLength = 42,
            Sha256Hash = Sha256($"evidence|{evidenceId}"),
            VerificationStatus = EvidenceVerificationStatus.Verified,
            Version = 1,
            IsImmutable = true,
            IsUnderLegalHold = true,
            RetentionPolicyKey = "api-expired-policy",
            RegistrationIdempotencyKey = $"evidence-{Guid.NewGuid():N}",
            RegistrationRequestHash = Sha256($"registration|{evidenceId}"),
            CapturedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });
        dbContext.EvidenceVersions.Add(new EvidenceVersion
        {
            Id = versionId,
            EvidenceId = evidenceId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Sequence = 1,
            OriginalFileName = "retention-api.txt",
            MediaType = "text/plain",
            DeclaredSize = 42,
            ContentHash = Sha256($"evidence|{evidenceId}"),
            StorageObjectReference = $"retention-api/{evidenceId}",
            IntegrityState = "Verified",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            CreatedBy = "retention-api-test"
        });
        dbContext.LegalHolds.Add(new LegalHold
        {
            Id = legalHoldId,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            EvidenceId = evidenceId,
            Reason = "API preservation",
            PlacedBy = "retention-api-test",
            PlacedAt = DateTimeOffset.UtcNow.AddDays(-5),
            IdempotencyKey = $"legal-hold-{Guid.NewGuid():N}",
            RequestHash = Sha256($"legal-hold|{evidenceId}")
        });
        AddActorWithPermission(
            dbContext,
            tenantId,
            workspaceId,
            "api-release-requester",
            ProductAuthorityPermissions.RequestLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            legalHoldId.ToString("D"));
        AddActorWithPermission(
            dbContext,
            tenantId,
            workspaceId,
            "api-release-approver",
            ProductAuthorityPermissions.ApproveLegalHoldRelease,
            ProductAuthorityResourceTypes.GovernanceLegalHold,
            legalHoldId.ToString("D"));
        AddActorWithPermission(
            dbContext,
            tenantId,
            workspaceId,
            "api-retention-evaluator",
            ProductAuthorityPermissions.EvaluateDeletionEligibility,
            ProductAuthorityResourceTypes.GovernanceRetention,
            evidenceId.ToString());
        await dbContext.SaveChangesAsync();
    }

    private static void AddActorWithPermission(
        DataLooMDbContext dbContext,
        TenantId tenantId,
        WorkspaceId workspaceId,
        string actorSubject,
        string permissionKey,
        string resourceType,
        string resourceId)
    {
        var actor = new ProductActor
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Subject = actorSubject,
            DisplayName = actorSubject,
            ActorType = ProductActorTypes.Human,
            State = ProductActorStates.Active,
            AuthorityVersion = 1,
            AuthorityChangedAt = DateTimeOffset.UtcNow,
            CreatedBy = "retention-api-test",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ProductActors.Add(actor);
        dbContext.ProductTenantMemberships.Add(new ProductTenantMembership
        {
            TenantId = tenantId,
            ActorId = actor.Id,
            ActorSubject = actorSubject,
            State = ProductMembershipStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            GrantedBy = "retention-api-test",
            GrantedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"tenant-membership-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actorSubject}|tenant")
        });
        dbContext.ProductWorkspaceMemberships.Add(new ProductWorkspaceMembership
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ActorId = actor.Id,
            ActorSubject = actorSubject,
            State = ProductMembershipStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            GrantedBy = "retention-api-test",
            GrantedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"workspace-membership-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actorSubject}|workspace")
        });
        dbContext.ProductPermissionAssignments.Add(new ProductPermissionAssignment
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            ActorId = actor.Id,
            ActorSubject = actorSubject,
            PermissionKey = permissionKey,
            ResourceType = resourceType,
            ResourceId = resourceId,
            State = ProductPermissionAssignmentStates.Active,
            AuthorityVersion = actor.AuthorityVersion,
            AssignedBy = "retention-api-test",
            AssignedAt = DateTimeOffset.UtcNow,
            EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
            IdempotencyKey = $"permission-assignment-{Guid.NewGuid():N}",
            RequestHash = Sha256($"{actorSubject}|{permissionKey}|{resourceId}")
        });
    }

    private static void AddContextHeaders(HttpClient client, TenantId tenantId, WorkspaceId workspaceId, string actor)
    {
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.WorkspaceHeader, workspaceId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, actor);
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

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

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

    private sealed record RetentionPolicyApiRequest(
        string PolicyKey,
        string Description,
        int RetainForDays,
        bool LegalHoldOverridesDeletion,
        string? IdempotencyKey);

    private sealed record RetentionPolicyApiResponse(
        Guid PolicyId,
        string PolicyKey,
        int RetainForDays,
        bool LegalHoldOverridesDeletion,
        DateTimeOffset CreatedAt,
        bool IdempotentReplay);

    private sealed record LegalHoldReleaseRequestApiRequest(
        string Reason,
        string? IdempotencyKey);

    private sealed record LegalHoldReleaseRequestApiResponse(
        Guid ReleaseRequestId,
        Guid LegalHoldId,
        string EvidenceId,
        string State,
        DateTimeOffset RequestedAt,
        bool IdempotentReplay);

    private sealed record LegalHoldReleaseApprovalApiRequest(
        string Reason,
        string? IdempotencyKey);

    private sealed record LegalHoldReleaseApprovalApiResponse(
        Guid ReleaseRequestId,
        Guid LegalHoldId,
        string EvidenceId,
        string State,
        DateTimeOffset ReleasedAt,
        bool EvidenceUnderLegalHold,
        bool EvidencePhysicallyDeleted,
        bool IdempotentReplay);

    private sealed record DeletionEligibilityApiRequest(string? IdempotencyKey);

    private sealed record DeletionEligibilityApiResponse(
        Guid EvaluationId,
        string EvidenceId,
        bool IsEligible,
        string ReasonCode,
        string Reason,
        DateTimeOffset RetentionCommencedAt,
        DateTimeOffset? RetentionExpiresAt,
        bool HasActiveLegalHold,
        string LifecycleState,
        bool EvidencePhysicallyDeleted,
        bool IdempotentReplay);
}