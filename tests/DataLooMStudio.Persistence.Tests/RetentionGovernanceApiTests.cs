using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using DataLooMStudio.Infrastructure.RequestContext;
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
}