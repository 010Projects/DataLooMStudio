using DataLooMStudio.Infrastructure.Configuration;
using DataLooMStudio.SharedKernel.Modules;

using Microsoft.Extensions.Options;

namespace DataLooMStudio.Dls.Worker;

public sealed class Worker(
    ILogger<Worker> logger,
    IEnumerable<IDataLooMModule> modules,
    OutboxDispatcher dispatcher,
    IOptionsMonitor<DataLooMInfrastructureOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var moduleNames = modules.Select(module => module.Manifest.Name).Order().ToArray();
        logger.LogInformation(
            "DataLooM worker started with {ModuleCount} module registrations: {Modules}",
            moduleNames.Length,
            string.Join(", ", moduleNames));

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.CurrentValue.WorkerProcessingEnabled)
            {
                logger.LogWarning("Background processing is suspended by governed configuration.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            int processed;
            try
            {
                processed = await dispatcher.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background processing cycle failed; retrying after a bounded delay.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            if (processed == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}