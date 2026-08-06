using DataLooMStudio.Dls.Migrate;
using DataLooMStudio.Runtime.Persistence.DependencyInjection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var command = MigrationCommand.Parse(args);

if (!command.Apply)
{
    Console.Error.WriteLine("Usage: DataLooMStudio.Dls.Migrate --apply [--connection <connection-string>]");
    return MigrationExitCodes.UsageError;
}

var builder = Host.CreateApplicationBuilder(args);

if (!string.IsNullOrWhiteSpace(command.ConnectionString))
{
    builder.Configuration["ConnectionStrings:DataLooM"] = command.ConnectionString;
}

builder.Services.AddDataLooMPersistence(builder.Configuration);
builder.Services.AddScoped<MigrationRunner>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
var result = await runner.ApplyAsync(CancellationToken.None);

if (!result.Succeeded)
{
    Console.Error.WriteLine(result.ErrorMessage);
    return MigrationExitCodes.Failure;
}

Console.WriteLine(result.AppliedMigrationCount == 0
    ? "Database is already up to date."
    : $"Applied {result.AppliedMigrationCount} migration(s).");

return MigrationExitCodes.Success;