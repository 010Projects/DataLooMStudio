using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace DataLooMStudio.Api.Tests;

public sealed class FoundationEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_returns_healthy_status()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz", CancellationToken.None);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body?["status"]);
    }

    [Fact]
    public async Task Module_endpoint_exposes_ai_governance_without_ai_execution()
    {
        using var client = factory.CreateClient();

        var modules = await client.GetFromJsonAsync<ModuleManifestResponse[]>(
            "/api/modules",
            CancellationToken.None);

        var aiGovernance = Assert.Single(modules ?? [], module => module.Name == "AiGovernance");

        Assert.False(aiGovernance.ContainsAiExecution);
        Assert.Equal("AiGovernanceBoundary", aiGovernance.BoundaryKind);
    }

    [Fact]
    public async Task OpenApi_endpoint_returns_foundation_document()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("DataLooM Studio Foundation API", body, StringComparison.Ordinal);
        Assert.Contains("/api/modules", body, StringComparison.Ordinal);
        Assert.Contains("/api/v1/workspaces/{workspaceId}/evidence", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evidence_registration_requires_authenticated_actor()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{Guid.NewGuid():D}/evidence",
            new
            {
                evidenceType = "Document",
                classification = "Internal",
                originalFileName = "synthetic.txt",
                mediaType = "text/plain",
                declaredSize = 14,
                contentHash = "8123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                storageObjectReference = "tenant/workspace/synthetic.txt",
                retentionPolicyKey = "default",
                idempotencyKey = "api-unauthorized-001"
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ModuleManifestResponse(
        string Name,
        string Version,
        string BoundaryKind,
        bool RequiresTenantContext,
        bool RequiresWorkspaceContext,
        bool OwnsTransactionalOutbox,
        bool ContainsAiExecution,
        string[] Responsibilities,
        string[] DependsOn);
}