using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using DataLooMStudio.Dls.Migrate;
using DataLooMStudio.Runtime.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace DataLooMStudio.Persistence.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private const string ApplicationRole = "dls_app";

    private const string ApplicationPassword = "postgres";

    private readonly string containerName = $"dls-persistence-{Guid.NewGuid():N}";

    private readonly int hostPort = GetFreeTcpPort();

    public string AdminConnectionString =>
        $"Host=127.0.0.1;Port={hostPort};Database=dataloomstudio;Username=postgres;Password=postgres";

    public string ApplicationConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
            {
                Username = ApplicationRole,
                Password = ApplicationPassword
            };

            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await RunDockerAsync(
            "run",
            "--rm",
            "-d",
            "--name",
            containerName,
            "-e",
            "POSTGRES_PASSWORD=postgres",
            "-e",
            "POSTGRES_DB=dataloomstudio",
            "-p",
            $"127.0.0.1:{hostPort}:5432",
            "postgres:18-alpine");
        await WaitForPostgresAsync();

        await using var dbContext = CreateDbContext();
        var result = await new MigrationRunner(dbContext).ApplyAsync(CancellationToken.None);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        await CreateApplicationRoleAsync();
    }

    public async Task DisposeAsync()
    {
        await RunDockerAsync("rm", "-f", containerName);
    }

    public DataLooMDbContext CreateDbContext()
    {
        return new DataLooMDbContext(CreateDbContextOptions());
    }

    public DbContextOptions<DataLooMDbContext> CreateDbContextOptions()
    {
        return new DbContextOptionsBuilder<DataLooMDbContext>()
            .UseNpgsql(AdminConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(DataLooMDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "foundation");
            })
            .Options;
    }

    public DataLooMDbContext CreateDbContext(DataLooMStudio.SharedKernel.RequestContext.IRequestContextAccessor accessor)
    {
        return new DataLooMDbContext(CreateDbContextOptions(), accessor);
    }

    private async Task CreateApplicationRoleAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            do $$
            begin
                if not exists (select 1 from pg_roles where rolname = 'dls_app') then
                    create role dls_app login password 'postgres';
                end if;
            end $$;

            grant usage on schema identity_access, workspace_weave, evidence, audit_lineage, foundation, retention, commercial, lifecycle, workflow, ai_governance to dls_app;
            grant select, insert, update, delete on all tables in schema identity_access, workspace_weave, evidence, audit_lineage, foundation, retention, commercial, lifecycle, workflow, ai_governance to dls_app;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private async Task WaitForPostgresAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await using var connection = new NpgsqlConnection(AdminConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception exception) when (exception is NpgsqlException or SocketException or TimeoutException)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        throw new TimeoutException("PostgreSQL 18 test container did not become ready.", lastException);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task RunDockerAsync(params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "docker";
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;

        process.Start();
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.Join(' ', arguments).StartsWith("rm -f", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Docker command failed: {standardOutput}{standardError}");
        }
    }
}