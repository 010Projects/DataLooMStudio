using DataLooMStudio.Dls.Worker;
using DataLooMStudio.Dls.Worker.Disposal;
using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.Infrastructure.DependencyInjection;
using DataLooMStudio.Infrastructure.Observability;
using DataLooMStudio.Runtime.DependencyInjection;
using DataLooMStudio.Runtime.Persistence.DependencyInjection;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

ProductionConfigurationValidator.ValidateAndThrow(
    builder.Configuration,
    builder.Environment.EnvironmentName,
    "DataLooMStudio.Dls.Worker",
    requireHttpSurface: false,
    requireWorkerIdentity: true);

builder.Services.AddDataLooMModules();
builder.Services.AddDataLooMInfrastructure(builder.Configuration);
builder.Services.AddDataLooMPersistence(builder.Configuration);
builder.Services.AddScoped<EvidenceDisposalWorkItemProcessor>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DataLooMStudio.Dls.Worker"))
    .WithTracing(tracing => tracing
        .AddSource("DataLooMStudio.Worker")
        .AddSource("Npgsql")
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddMeter("DataLooMStudio.Worker")
        .AddMeter(InfrastructureTelemetry.MeterName)
        .AddMeter("DataLooMStudio.Persistence")
        .AddOtlpExporter());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();