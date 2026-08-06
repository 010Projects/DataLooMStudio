using DataLooMStudio.SharedKernel.Modules;

namespace DataLooMStudio.Dls.Worker;

public sealed class Worker(
    ILogger<Worker> logger,
    IEnumerable<IDataLooMModule> modules) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var moduleNames = modules.Select(module => module.Manifest.Name).Order().ToArray();
        logger.LogInformation(
            "DataLooM worker runtime started with {ModuleCount} module boundary registrations: {Modules}",
            moduleNames.Length,
            string.Join(", ", moduleNames));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}