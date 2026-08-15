using DataLooMStudio.Api.Middleware;
using DataLooMStudio.Runtime.Persistence.Retention;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

namespace DataLooMStudio.Api.Endpoints;

public static class RetentionEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapRetentionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/retention-policies",
            async (
                Guid workspaceId,
                RetentionPolicyApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IRetentionGovernanceService retentionService,
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

                try
                {
                    var result = await retentionService.DefineRetentionPolicyAsync(
                        new RetentionPolicyCommand(
                            request.PolicyKey,
                            request.Description,
                            request.RetainForDays,
                            request.LegalHoldOverridesDeletion,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/retention-policies/{result.PolicyKey}",
                        new RetentionPolicyApiResponse(
                            result.PolicyId,
                            result.PolicyKey,
                            result.RetainForDays,
                            result.LegalHoldOverridesDeletion,
                            result.CreatedAt,
                            result.IdempotentReplay));
                }
                catch (Exception exception) when (exception is RetentionGovernanceValidationException
                    or RetentionGovernanceConflictException
                    or RetentionGovernanceForbiddenException
                    or UnauthorizedAccessException)
                {
                    return ToRetentionError(exception);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("DefineRetentionPolicy")
            .WithSummary("Define a workspace retention policy under Product Authority");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence/{evidenceId:guid}/legal-holds",
            async (
                Guid workspaceId,
                Guid evidenceId,
                LegalHoldApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IRetentionGovernanceService retentionService,
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

                try
                {
                    var result = await retentionService.PlaceLegalHoldAsync(
                        new PlaceLegalHoldCommand(
                            new EvidenceId(evidenceId),
                            request.Reason,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/evidence/{result.EvidenceId}/legal-holds/{result.LegalHoldId:D}",
                        new LegalHoldApiResponse(
                            result.LegalHoldId,
                            result.EvidenceId.ToString(),
                            result.PlacedAt,
                            result.EvidenceUnderLegalHold,
                            result.IdempotentReplay));
                }
                catch (Exception exception) when (exception is RetentionGovernanceValidationException
                    or RetentionGovernanceConflictException
                    or RetentionGovernanceForbiddenException
                    or UnauthorizedAccessException)
                {
                    return ToRetentionError(exception);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("PlaceEvidenceLegalHold")
            .WithSummary("Place a legal hold on Evidence under Product Authority");

        return endpoints;
    }

    private static IResult ToRetentionError(Exception exception)
    {
        return exception switch
        {
            RetentionGovernanceValidationException validation => Results.ValidationProblem(validation.Errors),
            RetentionGovernanceConflictException conflict => Results.Problem(
                title: "Retention governance operation conflict.",
                detail: conflict.Message,
                statusCode: StatusCodes.Status409Conflict),
            RetentionGovernanceForbiddenException => Results.Forbid(),
            UnauthorizedAccessException unauthorized => Results.Problem(
                title: "Retention governance operation is not authorized.",
                detail: unauthorized.Message,
                statusCode: StatusCodes.Status401Unauthorized),
            _ => throw exception
        };
    }

    private static string? ResolveIdempotencyKey(HttpContext httpContext, string? requestIdempotencyKey)
    {
        if (httpContext.Request.Headers.TryGetValue(IdempotencyHeader, out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        return requestIdempotencyKey;
    }
}

public sealed record RetentionPolicyApiRequest(
    string PolicyKey,
    string Description,
    int RetainForDays,
    bool LegalHoldOverridesDeletion,
    string? IdempotencyKey);

public sealed record RetentionPolicyApiResponse(
    Guid PolicyId,
    string PolicyKey,
    int RetainForDays,
    bool LegalHoldOverridesDeletion,
    DateTimeOffset CreatedAt,
    bool IdempotentReplay);

public sealed record LegalHoldApiRequest(
    string Reason,
    string? IdempotencyKey);

public sealed record LegalHoldApiResponse(
    Guid LegalHoldId,
    string EvidenceId,
    DateTimeOffset PlacedAt,
    bool EvidenceUnderLegalHold,
    bool IdempotentReplay);