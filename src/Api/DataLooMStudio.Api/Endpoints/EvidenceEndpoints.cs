using DataLooMStudio.Api.Middleware;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.SharedKernel.RequestContext;

namespace DataLooMStudio.Api.Endpoints;

public static class EvidenceEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapEvidenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence",
            async (
                Guid workspaceId,
                EvidenceRegistrationApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceRegistrationService registrationService,
                CancellationToken cancellationToken) =>
            {
                var context = contextAccessor.Current;
                if (context is null)
                {
                    return Results.Problem(
                        title: "Tenant and workspace context is required.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                if (context.WorkspaceId.Value != workspaceId)
                {
                    return Results.Forbid();
                }

                var command = request.ToCommand(ResolveIdempotencyKey(httpContext, request));

                try
                {
                    var result = await registrationService.RegisterInitialVersionAsync(command, cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/evidence/{result.EvidenceId}",
                        new EvidenceRegistrationApiResponse(
                            result.EvidenceId.ToString(),
                            result.VersionId.ToString(),
                            result.LifecycleState,
                            result.IntegrityState,
                            result.CreatedAt,
                            result.IdempotentReplay));
                }
                catch (EvidenceRegistrationValidationException exception)
                {
                    return Results.ValidationProblem(exception.Errors);
                }
                catch (EvidenceRegistrationConflictException exception)
                {
                    return Results.Problem(
                        title: "Idempotency conflict.",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status409Conflict);
                }
                catch (EvidenceRegistrationForbiddenException)
                {
                    return Results.Forbid();
                }
                catch (UnauthorizedAccessException exception)
                {
                    return Results.Problem(
                        title: "Evidence registration is not authorized.",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status401Unauthorized);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("RegisterEvidence")
            .WithSummary("Register initial evidence metadata and immutable version");

        return endpoints;
    }

    private static string? ResolveIdempotencyKey(HttpContext httpContext, EvidenceRegistrationApiRequest request)
    {
        if (httpContext.Request.Headers.TryGetValue(IdempotencyHeader, out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        return request.IdempotencyKey;
    }
}

public sealed record EvidenceRegistrationApiRequest(
    string EvidenceType,
    string Classification,
    string OriginalFileName,
    string MediaType,
    long DeclaredSize,
    string ContentHash,
    string StorageObjectReference,
    string RetentionPolicyKey,
    string? IdempotencyKey)
{
    public EvidenceRegistrationRequest ToCommand(string? idempotencyKey)
    {
        return new EvidenceRegistrationRequest(
            EvidenceType,
            Classification,
            OriginalFileName,
            MediaType,
            DeclaredSize,
            ContentHash,
            StorageObjectReference,
            RetentionPolicyKey,
            idempotencyKey);
    }
}

public sealed record EvidenceRegistrationApiResponse(
    string EvidenceId,
    string VersionId,
    string LifecycleState,
    string IntegrityState,
    DateTimeOffset CreatedAt,
    bool IdempotentReplay);