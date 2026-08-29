using System.Text.Json.Serialization;

using DataLooMStudio.Api.Endpoints;
using DataLooMStudio.Api.Middleware;
using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.DependencyInjection;
using DataLooMStudio.Runtime.DependencyInjection;
using DataLooMStudio.Runtime.Persistence;
using DataLooMStudio.Runtime.Persistence.DependencyInjection;

using Microsoft.AspNetCore.Authentication.JwtBearer;

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

ProductionConfigurationValidator.ValidateAndThrow(
    builder.Configuration,
    builder.Environment.EnvironmentName,
    "DataLooMStudio.Api",
    requireHttpSurface: true,
    requireWorkerIdentity: false);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var allowedOrigins = ProductionConfigurationValidator.ResolveAllowedOrigins(builder.Configuration);
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DataLooMAllowedOrigins", policy =>
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

builder.Services.AddDataLooMModules();
builder.Services.AddDataLooMInfrastructure(builder.Configuration);
builder.Services.AddDataLooMPersistence(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var entraSection = builder.Configuration.GetSection("EntraId");
        var authority = entraSection["Authority"];
        var tenantId = entraSection["TenantId"];
        var instance = entraSection["Instance"];

        if (string.IsNullOrWhiteSpace(authority)
            && !string.IsNullOrWhiteSpace(instance)
            && !string.IsNullOrWhiteSpace(tenantId))
        {
            authority = $"{instance.TrimEnd('/')}/{tenantId}/v2.0";
        }

        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
        }

        options.Audience = entraSection["Audience"] ?? entraSection["ClientId"];
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("WorkspaceScoped", policy => policy.RequireAuthenticatedUser());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DataLooMDbContext>("postgresql");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DataLooMStudio.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantWorkspaceContextMiddleware>();
if (allowedOrigins.Length > 0)
{
    app.UseCors("DataLooMAllowedOrigins");
}

app.UseAuthorization();

app.MapFoundationEndpoints();
app.MapEvidenceEndpoints();
app.MapRetentionEndpoints();
app.MapHealthChecks("/readyz");

app.Run();

public partial class Program;