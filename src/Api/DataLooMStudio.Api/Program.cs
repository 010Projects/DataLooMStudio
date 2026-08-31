using System.Text.Json.Serialization;

using DataLooMStudio.Api.Endpoints;
using DataLooMStudio.Api.Health;
using DataLooMStudio.Api.Middleware;
using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.DependencyInjection;
using DataLooMStudio.Infrastructure.Observability;
using DataLooMStudio.Runtime.DependencyInjection;
using DataLooMStudio.Runtime.Persistence;
using DataLooMStudio.Runtime.Persistence.DependencyInjection;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

using OpenTelemetry.Metrics;
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
            options.Authority = authority.TrimEnd('/');
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidIssuer = options.Authority;
        }

        var clientId = entraSection["ClientId"];
        var audience = entraSection["Audience"] ?? clientId;
        options.Audience = audience;
        options.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(audience);
        options.TokenValidationParameters.ValidAudiences = new[]
            {
                audience,
                clientId,
                string.IsNullOrWhiteSpace(clientId) ? null : $"api://{clientId}"
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        options.TokenValidationParameters.NameClaimType = "oid";
        options.MapInboundClaims = false;

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.Principal is null
                        || !EntraTokenIdentityValidator.HasCanonicalActorClaims(context.Principal, tenantId))
                    {
                        context.Fail("The access token does not contain canonical tenant and actor identity claims.");
                    }

                    return Task.CompletedTask;
                }
            };
        }
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("WorkspaceScoped", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var requiredScope = builder.Configuration["EntraId:RequiredScope"];
            if (string.IsNullOrWhiteSpace(requiredScope))
            {
                return true;
            }

            return context.User.FindAll("scp")
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains(requiredScope, StringComparer.Ordinal);
        });
    });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DataLooMDbContext>("postgresql")
    .AddCheck<MalwareScannerHealthCheck>("malware-scanner");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DataLooMStudio.Api"))
    .WithTracing(tracing => tracing
        .AddSource("Npgsql")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("DataLooMStudio.Api")
        .AddMeter(InfrastructureTelemetry.MeterName)
        .AddMeter("DataLooMStudio.Persistence")
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