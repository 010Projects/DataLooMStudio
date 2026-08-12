using DataLooMStudio.Api.Middleware;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.SharedKernel.Integrity;
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

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence/{evidenceId:guid}/upload-allocation",
            async (
                Guid workspaceId,
                Guid evidenceId,
                EvidenceUploadAllocationApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceContentService contentService,
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
                    var result = await contentService.AllocateUploadAsync(
                        new EvidenceUploadAllocationRequest(
                            new EvidenceId(evidenceId),
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/evidence/{result.EvidenceId}/upload-allocation/{result.AllocationId:D}",
                        new EvidenceUploadAllocationApiResponse(
                            result.EvidenceId.ToString(),
                            result.VersionId.ToString(),
                            result.AllocationId,
                            result.StorageObjectReference,
                            result.UploadAuthority,
                            result.ExpiresAt,
                            result.PermittedOperation,
                            result.MaxSize,
                            result.MediaType,
                            result.IdempotentReplay));
                }
                catch (EvidenceContentValidationException exception)
                {
                    return Results.ValidationProblem(exception.Errors);
                }
                catch (EvidenceContentConflictException exception)
                {
                    return Results.Problem(
                        title: "Evidence content operation conflict.",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status409Conflict);
                }
                catch (EvidenceContentForbiddenException)
                {
                    return Results.Forbid();
                }
                catch (UnauthorizedAccessException exception)
                {
                    return Results.Problem(
                        title: "Evidence content operation is not authorized.",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status401Unauthorized);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("AllocateEvidenceUpload")
            .WithSummary("Allocate short-lived upload authority for a registered Evidence version");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence/{evidenceId:guid}/versions/{versionId:guid}/content-received",
            async (
                Guid workspaceId,
                Guid evidenceId,
                Guid versionId,
                EvidenceContentReceivedApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceContentService contentService,
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
                    var result = await contentService.ConfirmContentReceivedAsync(
                        new EvidenceContentReceiptRequest(
                            new EvidenceId(evidenceId),
                            new EvidenceVersionId(versionId),
                            request.StorageObjectReference,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Ok(new EvidenceContentReceivedApiResponse(
                        result.EvidenceId.ToString(),
                        result.VersionId.ToString(),
                        result.LifecycleState,
                        result.IntegrityOutcome,
                        result.ScanOutcome,
                        result.FailureReason,
                        result.ActualSize,
                        result.ActualSha256Hash,
                        result.VerifiedAt,
                        result.IdempotentReplay));
                }
                catch (EvidenceContentValidationException exception)
                {
                    return Results.ValidationProblem(exception.Errors);
                }
                catch (EvidenceContentConflictException exception)
                {
                    return Results.Problem(
                        title: "Evidence content operation conflict.",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status409Conflict);
                }
                catch (EvidenceContentForbiddenException)
                {
                    return Results.Forbid();
                }
                catch (UnauthorizedAccessException exception)
                {
                    return Results.Problem(
                        title: "Evidence content operation is not authorized.",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status401Unauthorized);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("ConfirmEvidenceContentReceived")
            .WithSummary("Confirm Evidence content receipt and run integrity and scanning checks");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence/{evidenceId:guid}/versions/{versionId:guid}/reviews",
            async (
                Guid workspaceId,
                Guid evidenceId,
                Guid versionId,
                EvidenceReviewRequestApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
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
                    var result = await reviewDecisionService.RequestReviewAsync(
                        new EvidenceReviewRequestCommand(
                            new EvidenceId(evidenceId),
                            new EvidenceVersionId(versionId),
                            request.ReviewKind,
                            request.DueAt,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/evidence-reviews/{result.ReviewId:D}",
                        new EvidenceReviewRequestApiResponse(
                            result.ReviewId,
                            result.EvidenceId.ToString(),
                            result.EvidenceVersionId.ToString(),
                            result.State,
                            result.Version,
                            result.RequestedAt,
                            result.IdempotentReplay));
                }
                catch (Exception exception) when (exception is EvidenceReviewDecisionValidationException
                    or EvidenceReviewDecisionConflictException
                    or EvidenceReviewDecisionForbiddenException
                    or UnauthorizedAccessException)
                {
                    return ToReviewDecisionError(exception);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("RequestEvidenceReview")
            .WithSummary("Request review for an available Evidence version");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence-reviews/{reviewId:guid}/assignments",
            async (
                Guid workspaceId,
                Guid reviewId,
                EvidenceReviewerAssignmentApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
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
                    var result = await reviewDecisionService.AssignReviewerAsync(
                        new EvidenceReviewerAssignmentCommand(
                            reviewId,
                            request.ReviewerSubject,
                            request.Role,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/evidence-reviews/{result.ReviewId:D}/assignments/{result.AssignmentId:D}",
                        new EvidenceReviewerAssignmentApiResponse(
                            result.AssignmentId,
                            result.ReviewId,
                            result.ReviewerSubject,
                            result.Role,
                            result.IdempotentReplay));
                }
                catch (Exception exception) when (exception is EvidenceReviewDecisionValidationException
                    or EvidenceReviewDecisionConflictException
                    or EvidenceReviewDecisionForbiddenException
                    or UnauthorizedAccessException)
                {
                    return ToReviewDecisionError(exception);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("AssignEvidenceReviewer")
            .WithSummary("Assign explicit Evidence review authority");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence-reviews/{reviewId:guid}/candidate-decisions",
            async (
                Guid workspaceId,
                Guid reviewId,
                EvidenceCandidateDecisionApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
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
                    var result = await reviewDecisionService.CreateCandidateDecisionAsync(
                        new EvidenceCandidateDecisionCommand(
                            reviewId,
                            request.DecisionType,
                            request.Summary,
                            request.SupersedesDecisionId,
                            ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/workspaces/{workspaceId:D}/evidence-reviews/{result.ReviewId:D}/candidate-decisions/{result.CandidateDecisionId:D}",
                        new EvidenceCandidateDecisionApiResponse(
                            result.CandidateDecisionId,
                            result.ReviewId,
                            result.DecisionType,
                            result.State,
                            result.Version,
                            result.IdempotentReplay));
                }
                catch (Exception exception) when (exception is EvidenceReviewDecisionValidationException
                    or EvidenceReviewDecisionConflictException
                    or EvidenceReviewDecisionForbiddenException
                    or UnauthorizedAccessException)
                {
                    return ToReviewDecisionError(exception);
                }
            })
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("CreateEvidenceCandidateDecision")
            .WithSummary("Create a candidate decision for Evidence review");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence-reviews/{reviewId:guid}/candidate-decisions/{candidateDecisionId:guid}/accept",
            async (
                Guid workspaceId,
                Guid reviewId,
                Guid candidateDecisionId,
                EvidenceApplyDecisionApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
                CancellationToken cancellationToken) =>
                await ApplyReviewDecisionAsync(
                    workspaceId,
                    reviewId,
                    candidateDecisionId,
                    "Accept",
                    request,
                    httpContext,
                    contextAccessor,
                    reviewDecisionService,
                    cancellationToken))
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("AcceptEvidenceCandidateDecision")
            .WithSummary("Apply an authoritative accept decision");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence-reviews/{reviewId:guid}/candidate-decisions/{candidateDecisionId:guid}/reject",
            async (
                Guid workspaceId,
                Guid reviewId,
                Guid candidateDecisionId,
                EvidenceApplyDecisionApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
                CancellationToken cancellationToken) =>
                await ApplyReviewDecisionAsync(
                    workspaceId,
                    reviewId,
                    candidateDecisionId,
                    "Reject",
                    request,
                    httpContext,
                    contextAccessor,
                    reviewDecisionService,
                    cancellationToken))
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("RejectEvidenceCandidateDecision")
            .WithSummary("Apply an authoritative reject decision");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence-reviews/{reviewId:guid}/candidate-decisions/{candidateDecisionId:guid}/request-correction",
            async (
                Guid workspaceId,
                Guid reviewId,
                Guid candidateDecisionId,
                EvidenceApplyDecisionApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
                CancellationToken cancellationToken) =>
                await ApplyReviewDecisionAsync(
                    workspaceId,
                    reviewId,
                    candidateDecisionId,
                    "RequestCorrection",
                    request,
                    httpContext,
                    contextAccessor,
                    reviewDecisionService,
                    cancellationToken))
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("RequestEvidenceCorrection")
            .WithSummary("Apply an authoritative correction request");

        endpoints.MapPost(
            "/api/v1/workspaces/{workspaceId:guid}/evidence-reviews/{reviewId:guid}/candidate-decisions/{candidateDecisionId:guid}/supersede",
            async (
                Guid workspaceId,
                Guid reviewId,
                Guid candidateDecisionId,
                EvidenceApplyDecisionApiRequest request,
                HttpContext httpContext,
                IRequestContextAccessor contextAccessor,
                IEvidenceReviewDecisionService reviewDecisionService,
                CancellationToken cancellationToken) =>
                await ApplyReviewDecisionAsync(
                    workspaceId,
                    reviewId,
                    candidateDecisionId,
                    "Supersede",
                    request,
                    httpContext,
                    contextAccessor,
                    reviewDecisionService,
                    cancellationToken))
            .RequireAuthorization("WorkspaceScoped")
            .WithMetadata(RequiresWorkspaceScopeMetadata.Instance)
            .WithName("SupersedeEvidenceDecision")
            .WithSummary("Apply an authoritative supersede decision");

        return endpoints;
    }

    private static async Task<IResult> ApplyReviewDecisionAsync(
        Guid workspaceId,
        Guid reviewId,
        Guid candidateDecisionId,
        string decisionType,
        EvidenceApplyDecisionApiRequest request,
        HttpContext httpContext,
        IRequestContextAccessor contextAccessor,
        IEvidenceReviewDecisionService reviewDecisionService,
        CancellationToken cancellationToken)
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
            var result = await reviewDecisionService.ApplyDecisionAsync(
                new EvidenceApplyDecisionCommand(
                    reviewId,
                    candidateDecisionId,
                    decisionType,
                    request.ExpectedCandidateVersion,
                    request.Reason,
                    ResolveIdempotencyKey(httpContext, request.IdempotencyKey)),
                cancellationToken);

            return Results.Ok(new EvidenceAppliedDecisionApiResponse(
                result.ReviewId,
                result.CandidateDecisionId,
                result.ReviewState,
                result.CandidateState,
                result.CandidateVersion,
                result.DecidedAt,
                result.IdempotentReplay));
        }
        catch (Exception exception) when (exception is EvidenceReviewDecisionValidationException
            or EvidenceReviewDecisionConflictException
            or EvidenceReviewDecisionForbiddenException
            or UnauthorizedAccessException)
        {
            return ToReviewDecisionError(exception);
        }
    }

    private static IResult ToReviewDecisionError(Exception exception)
    {
        return exception switch
        {
            EvidenceReviewDecisionValidationException validation => Results.ValidationProblem(validation.Errors),
            EvidenceReviewDecisionConflictException conflict => Results.Problem(
                title: "Evidence review decision operation conflict.",
                detail: conflict.Message,
                statusCode: StatusCodes.Status409Conflict),
            EvidenceReviewDecisionForbiddenException => Results.Forbid(),
            UnauthorizedAccessException unauthorized => Results.Problem(
                title: "Evidence review decision operation is not authorized.",
                detail: unauthorized.Message,
                statusCode: StatusCodes.Status401Unauthorized),
            _ => throw exception
        };
    }

    private static string? ResolveIdempotencyKey(HttpContext httpContext, EvidenceRegistrationApiRequest request)
    {
        return ResolveIdempotencyKey(httpContext, request.IdempotencyKey);
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

public sealed record EvidenceUploadAllocationApiRequest(string? IdempotencyKey);

public sealed record EvidenceUploadAllocationApiResponse(
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

public sealed record EvidenceContentReceivedApiRequest(
    string StorageObjectReference,
    string? IdempotencyKey);

public sealed record EvidenceContentReceivedApiResponse(
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

public sealed record EvidenceReviewRequestApiRequest(
    string ReviewKind,
    DateTimeOffset? DueAt,
    string? IdempotencyKey);

public sealed record EvidenceReviewRequestApiResponse(
    Guid ReviewId,
    string EvidenceId,
    string EvidenceVersionId,
    string State,
    int Version,
    DateTimeOffset RequestedAt,
    bool IdempotentReplay);

public sealed record EvidenceReviewerAssignmentApiRequest(
    string ReviewerSubject,
    string Role,
    string? IdempotencyKey);

public sealed record EvidenceReviewerAssignmentApiResponse(
    Guid AssignmentId,
    Guid ReviewId,
    string ReviewerSubject,
    string Role,
    bool IdempotentReplay);

public sealed record EvidenceCandidateDecisionApiRequest(
    string DecisionType,
    string Summary,
    Guid? SupersedesDecisionId,
    string? IdempotencyKey);

public sealed record EvidenceCandidateDecisionApiResponse(
    Guid CandidateDecisionId,
    Guid ReviewId,
    string DecisionType,
    string State,
    int Version,
    bool IdempotentReplay);

public sealed record EvidenceApplyDecisionApiRequest(
    int ExpectedCandidateVersion,
    string? Reason,
    string? IdempotencyKey);

public sealed record EvidenceAppliedDecisionApiResponse(
    Guid ReviewId,
    Guid CandidateDecisionId,
    string ReviewState,
    string CandidateState,
    int CandidateVersion,
    DateTimeOffset DecidedAt,
    bool IdempotentReplay);