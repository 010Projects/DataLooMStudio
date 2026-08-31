using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;

using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.AspNetCore.Authorization;

namespace DataLooMStudio.Api.Middleware;

public sealed class TenantWorkspaceContextMiddleware(
    RequestDelegate next,
    ILogger<TenantWorkspaceContextMiddleware> logger)
{
    private static readonly Meter Meter = new("DataLooMStudio.Api");
    private static readonly Counter<long> AuthorizationDenials = Meter.CreateCounter<long>("dls.authorization.denials");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("dls.api.request.duration", "ms");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("dls.api.requests");
    private const string CorrelationHeader = "X-Correlation-Id";
    private const string WorkspaceHeader = "X-Workspace-Id";

    public async Task InvokeAsync(HttpContext httpContext, IRequestContextAccessor contextAccessor)
    {
        var started = Stopwatch.GetTimestamp();
        var correlationId = ResolveCorrelationId(httpContext);
        httpContext.Response.Headers[CorrelationHeader] = correlationId;

        var endpoint = httpContext.GetEndpoint();
        var allowsAnonymous = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var requiresWorkspaceScope = endpoint?.Metadata.GetMetadata<RequiresWorkspaceScopeMetadata>() is not null;
        var isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;
        RequestContext? requestContext = null;
        Exception? pipelineException = null;

        try
        {
            if (requiresWorkspaceScope
                && isAuthenticated
                && !TryCreateRequestContext(httpContext, correlationId, out requestContext))
            {
                logger.LogWarning(
                    "Rejected workspace-scoped request without complete tenant/workspace context. Path: {Path}, CorrelationId: {CorrelationId}",
                    httpContext.Request.Path,
                    correlationId);

                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                AuthorizationDenials.Add(1, new KeyValuePair<string, object?>("reason", "context_missing"));
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    error = "tenant_workspace_context_required",
                    correlationId
                });
                return;
            }

            if (requiresWorkspaceScope || !allowsAnonymous)
            {
                contextAccessor.Current = requestContext;
            }

            using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
            {
                await next(httpContext);
            }
        }
        catch (Exception exception)
        {
            pipelineException = exception;
            throw;
        }
        finally
        {
            var statusCode = pipelineException is null
                ? httpContext.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            if (statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            {
                AuthorizationDenials.Add(1, new KeyValuePair<string, object?>("status", statusCode));
            }

            var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            RequestDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("http.route", route),
                new KeyValuePair<string, object?>("http.method", httpContext.Request.Method),
                new KeyValuePair<string, object?>("http.status_code", statusCode));
            Requests.Add(
                1,
                new KeyValuePair<string, object?>("http.route", route),
                new KeyValuePair<string, object?>("http.method", httpContext.Request.Method),
                new KeyValuePair<string, object?>("http.status_code", statusCode));
        }
    }

    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue(CorrelationHeader, out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        return httpContext.TraceIdentifier;
    }

    private static bool TryCreateRequestContext(
        HttpContext httpContext,
        string correlationId,
        out RequestContext? requestContext)
    {
        var tenantClaim = httpContext.User.FindFirstValue("tid")
            ?? httpContext.User.FindFirstValue("tenant_id");

        var workspaceClaim = httpContext.User.FindFirstValue("workspace_id")
            ?? httpContext.Request.Headers[WorkspaceHeader].FirstOrDefault();

        var subject = httpContext.User.FindFirstValue("oid")
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.Identity?.Name;

        if (!TenantId.TryParse(tenantClaim, out var tenantId)
            || !WorkspaceId.TryParse(workspaceClaim, out var workspaceId))
        {
            requestContext = null;
            return false;
        }

        requestContext = new RequestContext(
            tenantId,
            workspaceId,
            PrincipalSubject.FromClaim(subject),
            correlationId);

        return true;
    }
}