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

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence/{evidenceId:guid}/legal-holds/{legalHoldId:guid}/release-requests",
            async (
                Guid workspaceId,
                Guid evidenceId,
                Guid legalHoldId,
                LegalHoldReleaseRequestApiRequest request,
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
                    var result = await retentionService.RequestLegalHoldReleaseAsync(
                        new LegalHoldReleaseRequestCommand(
                            new EvidenceId(evidenceId),
                            legalHoldId,
                            request.Reason,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/legal-hold-release-requests/{result.ReleaseRequestId:D}",
                        new LegalHoldReleaseRequestApiResponse(
                            result.ReleaseRequestId,
                            result.LegalHoldId,
                            result.EvidenceId.ToString(),
                            result.State,
                            result.RequestedAt,
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
            .WithName("RequestLegalHoldRelease")
            .WithSummary("Request release of an active Legal Hold under Product Authority");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/legal-hold-release-requests/{releaseRequestId:guid}/approve",
            async (
                Guid workspaceId,
                Guid releaseRequestId,
                LegalHoldReleaseApprovalApiRequest request,
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
                    var result = await retentionService.ApproveLegalHoldReleaseAsync(
                        new LegalHoldReleaseApprovalCommand(
                            releaseRequestId,
                            request.Reason,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Ok(new LegalHoldReleaseApprovalApiResponse(
                        result.ReleaseRequestId,
                        result.LegalHoldId,
                        result.EvidenceId.ToString(),
                        result.State,
                        result.ReleasedAt,
                        result.EvidenceUnderLegalHold,
                        result.EvidencePhysicallyDeleted,
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
            .WithName("ApproveLegalHoldRelease")
            .WithSummary("Approve a Legal Hold release under Product Authority and SoD");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence/{evidenceId:guid}/deletion-eligibility-evaluations",
            async (
                Guid workspaceId,
                Guid evidenceId,
                DeletionEligibilityApiRequest request,
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
                    var result = await retentionService.EvaluateDeletionEligibilityAsync(
                        new DeletionEligibilityCommand(
                            new EvidenceId(evidenceId),
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Ok(new DeletionEligibilityApiResponse(
                        result.EvaluationId,
                        result.EvidenceId.ToString(),
                        result.IsEligible,
                        result.ReasonCode,
                        result.Reason,
                        result.RetentionCommencedAt,
                        result.RetentionExpiresAt,
                        result.HasActiveLegalHold,
                        result.LifecycleState,
                        result.EvidencePhysicallyDeleted,
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
            .WithName("EvaluateEvidenceDeletionEligibility")
            .WithSummary("Evaluate governed Evidence deletion eligibility without physical deletion");

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

public sealed record LegalHoldReleaseRequestApiRequest(
    string Reason,
    string? IdempotencyKey);

public sealed record LegalHoldReleaseRequestApiResponse(
    Guid ReleaseRequestId,
    Guid LegalHoldId,
    string EvidenceId,
    string State,
    DateTimeOffset RequestedAt,
    bool IdempotentReplay);

public sealed record LegalHoldReleaseApprovalApiRequest(
    string Reason,
    string? IdempotencyKey);

public sealed record LegalHoldReleaseApprovalApiResponse(
    Guid ReleaseRequestId,
    Guid LegalHoldId,
    string EvidenceId,
    string State,
    DateTimeOffset ReleasedAt,
    bool EvidenceUnderLegalHold,
    bool EvidencePhysicallyDeleted,
    bool IdempotentReplay);

public sealed record DeletionEligibilityApiRequest(string? IdempotencyKey);

public sealed record DeletionEligibilityApiResponse(
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