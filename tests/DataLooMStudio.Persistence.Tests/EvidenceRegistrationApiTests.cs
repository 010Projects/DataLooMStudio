using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using DataLooMStudio.Infrastructure.RequestContext;
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

public sealed class EvidenceRegistrationApiTests(
    PostgresFixture fixture,
    WebApplicationFactory<Program> factory) : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Api_registers_evidence_through_postgresql_boundary()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        using var client = CreateClient();
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-001");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence",
            CreateRequest("api-reg-001", "api-registration.txt", "tenant/workspace/api-registration.txt"),
            CancellationToken.None);
        var body = await response.Content.ReadFromJsonAsync<EvidenceRegistrationApiResponse>(
            cancellationToken: CancellationToken.None);
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await using var dbContext = fixture.CreateDbContext(accessor);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body!.IdempotentReplay);
        Assert.Equal("Registered", body.LifecycleState);
        Assert.Equal("Pending", body.IntegrityState);
        Assert.Equal(1, await dbContext.EvidenceRecords.CountAsync());
        Assert.Equal(1, await dbContext.EvidenceVersions.CountAsync());
        Assert.Equal(1, await dbContext.AuditEntries.CountAsync());
        Assert.Equal(1, await dbContext.LineageRelationships.CountAsync());
        Assert.Equal(1, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Api_replays_duplicate_idempotency_key()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        using var client = CreateClient();
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-002");
        var request = CreateRequest("api-reg-duplicate-001", "api-duplicate.txt", "tenant/workspace/api-duplicate.txt");

        var first = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/evidence", request, CancellationToken.None);
        var second = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/evidence", request, CancellationToken.None);
        var firstBody = await first.Content.ReadFromJsonAsync<EvidenceRegistrationApiResponse>(cancellationToken: CancellationToken.None);
        var secondBody = await second.Content.ReadFromJsonAsync<EvidenceRegistrationApiResponse>(cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(firstBody!.IdempotentReplay);
        Assert.True(secondBody!.IdempotentReplay);
        Assert.Equal(firstBody.EvidenceId, secondBody.EvidenceId);
    }

    [Fact]
    public async Task Api_rejects_invalid_registration_request()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        using var client = CreateClient();
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-003");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence",
            CreateRequest("api-reg-invalid-001", "api-invalid.txt", "tenant/workspace/api-invalid.txt") with
            {
                ContentHash = "invalid"
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_route_workspace_outside_context()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var routeWorkspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        using var client = CreateClient();
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-004");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{routeWorkspaceId}/evidence",
            CreateRequest("api-reg-forbidden-001", "api-forbidden.txt", "tenant/workspace/api-forbidden.txt"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_missing_workspace_context()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, "actor-api-005");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{Guid.NewGuid():D}/evidence",
            CreateRequest("api-reg-missing-context-001", "api-missing.txt", "tenant/workspace/api-missing.txt"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_invalid_actor_context()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.WorkspaceHeader, workspaceId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence",
            CreateRequest("api-reg-invalid-actor-001", "api-invalid-actor.txt", "tenant/workspace/api-invalid-actor.txt"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private static void AddContextHeaders(HttpClient client, TenantId tenantId, WorkspaceId workspaceId, string actor)
    {
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.WorkspaceHeader, workspaceId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, actor);
    }

    private static EvidenceRegistrationApiRequest CreateRequest(
        string idempotencyKey,
        string fileName,
        string storageReference)
    {
        return new EvidenceRegistrationApiRequest
        {
            EvidenceType = "Document",
            Classification = "Internal",
            OriginalFileName = fileName,
            MediaType = "text/plain",
            DeclaredSize = 14,
            ContentHash = "7123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            StorageObjectReference = storageReference,
            RetentionPolicyKey = "default",
            IdempotencyKey = idempotencyKey
        };
    }

    private async Task SeedTenantAndWorkspaceAsync(TenantId tenantId, WorkspaceId workspaceId)
    {
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = "Synthetic API Tenant",
            ExternalAuthority = $"synthetic-api-{tenantId}",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Synthetic API Workspace",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static RequestContextAccessor CreateRequestContext(TenantId tenantId, WorkspaceId workspaceId)
    {
        return new RequestContextAccessor
        {
            Current = new RequestContext(
                tenantId,
                workspaceId,
                new PrincipalSubject("test-actor"),
                $"corr-{Guid.NewGuid():N}")
        };
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

    private sealed record EvidenceRegistrationApiRequest
    {
        public string EvidenceType { get; init; } = string.Empty;

        public string Classification { get; init; } = string.Empty;

        public string OriginalFileName { get; init; } = string.Empty;

        public string MediaType { get; init; } = string.Empty;

        public long DeclaredSize { get; init; }

        public string ContentHash { get; init; } = string.Empty;

        public string StorageObjectReference { get; init; } = string.Empty;

        public string RetentionPolicyKey { get; init; } = string.Empty;

        public string? IdempotencyKey { get; init; }
    }

    private sealed record EvidenceRegistrationApiResponse(
        string EvidenceId,
        string VersionId,
        string LifecycleState,
        string IntegrityState,
        DateTimeOffset CreatedAt,
        bool IdempotentReplay);
}