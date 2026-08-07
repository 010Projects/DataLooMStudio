using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Infrastructure.SecurityScanning;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataLooMStudio.Persistence.Tests;

public sealed class EvidenceContentApiTests(
    PostgresFixture fixture,
    WebApplicationFactory<Program> factory) : IClassFixture<PostgresFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Api_allocates_upload_and_confirms_received_content()
    {
        var content = Encoding.UTF8.GetBytes("api content clean");
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        var store = new DevelopmentEvidenceObjectStore();
        using var client = CreateClient(store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-content-001");

        var registration = await RegisterEvidenceAsync(client, workspaceId, content, "api-content");
        var allocationResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/upload-allocation",
            new EvidenceUploadAllocationApiRequest("api-allocation-001"),
            CancellationToken.None);
        var allocation = await allocationResponse.Content.ReadFromJsonAsync<EvidenceUploadAllocationApiResponse>(
            cancellationToken: CancellationToken.None);
        await store.StoreObjectAsync(allocation!.StorageObjectReference, content, "text/plain", CancellationToken.None);

        var receiptResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/versions/{allocation.VersionId}/content-received",
            new EvidenceContentReceivedApiRequest(allocation.StorageObjectReference, "api-receipt-001"),
            CancellationToken.None);
        var receipt = await receiptResponse.Content.ReadFromJsonAsync<EvidenceContentReceivedApiResponse>(
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, allocationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, receiptResponse.StatusCode);
        Assert.Equal("Available", receipt!.LifecycleState);
        Assert.Equal("Succeeded", receipt.IntegrityOutcome);
        Assert.Equal("Clean", receipt.ScanOutcome);
        Assert.False(receipt.IdempotentReplay);
    }

    [Fact]
    public async Task Api_rejects_upload_allocation_for_route_workspace_outside_context()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var routeWorkspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        using var client = CreateClient(new DevelopmentEvidenceObjectStore(), new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean));
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-content-002");
        var registration = await RegisterEvidenceAsync(client, workspaceId, Encoding.UTF8.GetBytes("route mismatch"), "api-forbidden");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{routeWorkspaceId}/evidence/{registration.EvidenceId}/upload-allocation",
            new EvidenceUploadAllocationApiRequest("api-forbidden-allocation-001"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Api_rejects_expired_allocation()
    {
        var content = Encoding.UTF8.GetBytes("api expired content");
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId);
        var store = new DevelopmentEvidenceObjectStore();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        using var client = CreateClient(store, new FakeMalwareScanner(EvidenceMalwareScanOutcome.Clean), clock);
        AddContextHeaders(client, tenantId, workspaceId, "actor-api-content-003");

        var registration = await RegisterEvidenceAsync(client, workspaceId, content, "api-expired");
        var allocationResponse = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/upload-allocation",
            new EvidenceUploadAllocationApiRequest("api-expired-allocation-001"),
            CancellationToken.None);
        var allocation = await allocationResponse.Content.ReadFromJsonAsync<EvidenceUploadAllocationApiResponse>(
            cancellationToken: CancellationToken.None);
        await store.StoreObjectAsync(allocation!.StorageObjectReference, content, "text/plain", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(16));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/evidence/{registration.EvidenceId}/versions/{allocation.VersionId}/content-received",
            new EvidenceContentReceivedApiRequest(allocation.StorageObjectReference, "api-expired-receipt-001"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private HttpClient CreateClient(
        DevelopmentEvidenceObjectStore store,
        IEvidenceMalwareScanner scanner,
        IClock? clock = null)
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
                services.AddSingleton(scanner);
                if (clock is not null)
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton(clock);
                }

                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }).CreateClient();
    }

    private static async Task<EvidenceRegistrationApiResponse> RegisterEvidenceAsync(
        HttpClient client,
        WorkspaceId workspaceId,
        byte[] content,
        string scenarioName)
    {
        var response = await client.PostAsJsonAsync(
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

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EvidenceRegistrationApiResponse>(
            cancellationToken: CancellationToken.None))!;
    }

    private static void AddContextHeaders(HttpClient client, TenantId tenantId, WorkspaceId workspaceId, string actor)
    {
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.WorkspaceHeader, workspaceId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, actor);
    }

    private async Task SeedTenantAndWorkspaceAsync(TenantId tenantId, WorkspaceId workspaceId)
    {
        var accessor = new RequestContextAccessor
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
            DisplayName = "Synthetic API Tenant",
            ExternalAuthority = $"synthetic-api-content-{tenantId}-{Guid.NewGuid():N}",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = $"Synthetic API Workspace {Guid.NewGuid():N}",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
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

    private sealed record EvidenceContentReceivedApiResponse(
        string EvidenceId,
        string VersionId,
        string LifecycleState,
        string IntegrityOutcome,
        string ScanOutcome,
        string? FailureReason,
        long ActualSize,
        string ActualSha256Hash,
        DateTimeOffset VerifiedAt,
        bool IdempotentReplay);

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
        public Task<EvidenceMalwareScanResult> ScanAsync(
            EvidenceMalwareScanRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new EvidenceMalwareScanResult(
                outcome,
                "fake-api-scanner",
                "1.0",
                outcome == EvidenceMalwareScanOutcome.Clean ? null : $"Synthetic {outcome} result."));
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestContent";

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