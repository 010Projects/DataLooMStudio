using DataLooMStudio.Dls.Worker;
using DataLooMStudio.Dls.Worker.Disposal;
using DataLooMStudio.Infrastructure.DependencyInjection;
using DataLooMStudio.Runtime.DependencyInjection;
using DataLooMStudio.Runtime.Persistence.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDataLooMModules();
builder.Services.AddDataLooMInfrastructure(builder.Configuration);
builder.Services.AddDataLooMPersistence(builder.Configuration);
builder.Services.AddScoped<EvidenceDisposalWorkItemProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();