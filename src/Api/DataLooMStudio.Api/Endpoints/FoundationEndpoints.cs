using DataLooMStudio.Api.Middleware;
using DataLooMStudio.SharedKernel.Modules;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.AspNetCore.Http.HttpResults;

namespace DataLooMStudio.Api.Endpoints;

public static class FoundationEndpoints
{
    public static IEndpointRouteBuilder MapFoundationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", () => Results.Ok(new
        {
            service = "DataLooMStudio.Api",
            status = "healthy"
        })).AllowAnonymous();

        endpoints.MapGet("/openapi/v1.json", () => Results.Json(new
        {
            openapi = "3.1.0",
            info = new
            {
                title = "DataLooM Studio Foundation API",
                version = "1.0.0"
            },
            paths = new Dictionary<string, object>
            {
                ["/healthz"] = new
                {
                    get = new
                    {
                        summary = "Liveness probe",
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "API process is healthy" }
                        }
                    }
                },
                ["/api/modules"] = new
                {
                    get = new
                    {
                        summary = "Module manifests",
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Registered module boundary manifests" }
                        }
                    }
                },
                ["/api/governance/ai-boundary"] = new
                {
                    get = new
                    {
                        summary = "AI governance boundary",
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "AI execution is outside the engineering boundary" }
                        }
                    }
                },
                ["/api/foundation/boundaries"] = new
                {
                    get = new
                    {
                        summary = "Foundation boundary assertions",
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new { description = "Approved architectural boundary assertions" }
                        }
                    }
                },
                ["/api/v1/workspaces/{workspaceId}/evidence"] = new
                {
                    post = new
                    {
                        summary = "Register initial evidence metadata and immutable version",
                        parameters = new object[]
                        {
                            new
                            {
                                name = "workspaceId",
                                @in = "path",
                                required = true,
                                schema = new { type = "string", format = "uuid" }
                            },
                            new
                            {
                                name = "Idempotency-Key",
                                @in = "header",
                                required = false,
                                schema = new { type = "string" }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["201"] = new { description = "Evidence registration committed" },
                            ["400"] = new { description = "Invalid command or missing context" },
                            ["401"] = new { description = "Actor context is invalid" },
                            ["403"] = new { description = "Workspace is outside the active tenant context" },
                            ["409"] = new { description = "Idempotency key was used with a different request" }
                        }
                    }
                }
            }
        })).AllowAnonymous();

        endpoints.MapGet("/api/modules", Ok<IReadOnlyList<ModuleManifest>> (
            IEnumerable<IDataLooMModule> modules) =>
        {
            var manifests = modules
                .Select(module => module.Manifest)
                .OrderBy(manifest => manifest.Name, StringComparer.Ordinal)
                .ToArray();

            return TypedResults.Ok<IReadOnlyList<ModuleManifest>>(manifests);
        }).AllowAnonymous();

        endpoints.MapGet("/api/governance/ai-boundary", () => Results.Ok(new
        {
            boundary = "AiGovernance",
            containsAiExecution = false,
            executionAuthority = "OutsideEngineering",
            allowedResponsibilities = new[]
            {
                "policy metadata",
                "governance evidence",
                "audit traceability",
                "commercial capability checks"
            }
        })).AllowAnonymous();

        endpoints.MapGet("/api/foundation/boundaries", () => Results.Ok(new
        {
            modularMonolith = true,
            modulesExcluded = new[] { "Operations" },
            lifecycleWorkflowSeparation = true,
            immutableLineageIds = true,
            versionedRelationships = true,
            transactionalOutboxOwnedByApplication = true,
            evidenceConsistencyBoundary = "ADR-014",
            tenantWorkspaceIsolation = "required for workspace-scoped endpoints"
        })).AllowAnonymous();

        endpoints.MapGet("/api/foundation/context", Ok<object> (IRequestContextAccessor accessor) =>
        {
            var context = accessor.Current;
            return TypedResults.Ok<object>(new
            {
                tenantId = context?.TenantId.ToString(),
                workspaceId = context?.WorkspaceId.ToString(),
                principalSubject = context?.PrincipalSubject.ToString(),
                correlationId = context?.CorrelationId
            });
        })
        .RequireAuthorization("WorkspaceScoped")
        .WithMetadata(RequiresWorkspaceScopeMetadata.Instance);

        return endpoints;
    }
}