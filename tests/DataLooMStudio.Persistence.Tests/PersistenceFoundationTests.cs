using DataLooMStudio.Dls.Migrate;
using DataLooMStudio.Infrastructure.Clock;
using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Infrastructure.RequestContext;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence.Evidence;
using DataLooMStudio.Runtime.Persistence.Outbox;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace DataLooMStudio.Persistence.Tests;

public sealed class PersistenceFoundationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Clean_database_migration_creates_required_schemas()
    {
        var schemas = await QueryStringsAsync(
            fixture.AdminConnectionString,
            "select schema_name from information_schema.schemata where schema_name in ('identity_access', 'workspace_weave', 'evidence', 'audit_lineage', 'foundation') order by schema_name");

        Assert.Equal(
            ["audit_lineage", "evidence", "foundation", "identity_access", "workspace_weave"],
            schemas);
    }

    [Fact]
    public async Task Controlled_migration_execution_is_repeatable()
    {
        await using var dbContext = fixture.CreateDbContext();
        var result = await new MigrationRunner(dbContext).ApplyAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0, result.AppliedMigrationCount);
    }

    [Fact]
    public async Task Controlled_migration_execution_reports_failure()
    {
        var options = new DbContextOptionsBuilder<DataLooMStudio.Runtime.Persistence.DataLooMDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1")
            .Options;
        await using var dbContext = new DataLooMStudio.Runtime.Persistence.DataLooMDbContext(options);
        var result = await new MigrationRunner(dbContext).ApplyAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Tenant_and_workspace_records_persist_with_tenant_ownership()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await using var dbContext = fixture.CreateDbContext(accessor);

        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = "Synthetic Tenant",
            ExternalAuthority = $"synthetic-{tenantId}",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Synthetic Workspace",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        Assert.Equal(1, await dbContext.Tenants.CountAsync());
        var workspace = await dbContext.Workspaces.SingleAsync();
        Assert.Equal(tenantId, workspace.TenantId);
    }

    [Fact]
    public async Task Evidence_registration_commits_evidence_version_audit_lineage_and_outbox_atomically()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var service = CreateEvidenceRegistrationService(dbContext, accessor);

        var result = await service.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                "synthetic.txt",
                "text/plain",
                14,
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "tenant/workspace/synthetic.txt",
                "default"),
            CancellationToken.None);

        Assert.NotEqual(default, result.EvidenceId);
        Assert.Equal(1, await dbContext.EvidenceRecords.CountAsync());
        Assert.Equal(1, await dbContext.EvidenceVersions.CountAsync());
        Assert.Equal(1, await dbContext.AuditEntries.CountAsync());
        Assert.Equal(1, await dbContext.LineageRelationships.CountAsync());
        Assert.Equal(1, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Evidence_versions_are_immutable()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await SeedTenantWorkspaceAndEvidenceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var version = await dbContext.EvidenceVersions.SingleAsync();

        dbContext.Entry(version).State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Evidence_registration_transaction_rolls_back_on_failure()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var service = CreateEvidenceRegistrationService(dbContext, accessor);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                "synthetic.txt",
                "text/plain",
                14,
                "1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "https://public.example.invalid/synthetic.txt",
                "default"),
            CancellationToken.None));

        Assert.Equal(0, await dbContext.EvidenceRecords.CountAsync());
        Assert.Equal(0, await dbContext.EvidenceVersions.CountAsync());
        Assert.Equal(0, await dbContext.AuditEntries.CountAsync());
        Assert.Equal(0, await dbContext.LineageRelationships.CountAsync());
        Assert.Equal(0, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Row_level_security_denies_missing_cross_tenant_and_cross_workspace_access()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();

        await SeedRawWorkspacesAsync(tenantA, workspaceA, tenantB, workspaceB);

        Assert.Equal(0, await CountVisibleWorkspacesAsync((Guid?)null, (Guid?)null));
        Assert.Equal(1, await CountVisibleWorkspacesAsync(tenantA, workspaceA));
        Assert.Equal(0, await CountVisibleWorkspacesAsync(tenantB, workspaceA));
        Assert.Equal(0, await CountSpecificVisibleWorkspaceAsync(tenantA, workspaceB, workspaceA));

        await using (var connection = new NpgsqlConnection(fixture.ApplicationConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await SetLocalContextAsync(connection, transaction, tenantA, workspaceA);
            Assert.Equal(1, await CountVisibleWorkspacesAsync(connection, transaction));
            await transaction.CommitAsync();
        }

        Assert.Equal(0, await CountVisibleWorkspacesAsync((Guid?)null, (Guid?)null));
    }

    private static RequestContextAccessor CreateRequestContext(TenantId tenantId, WorkspaceId workspaceId)
    {
        return new RequestContextAccessor
        {
            Current = new RequestContext(
                tenantId,
                workspaceId,
                new PrincipalSubject("test-actor"),
                $"corr-{Guid.NewGuid():N}")
        };
    }

    private EvidenceRegistrationService CreateEvidenceRegistrationService(
        DataLooMStudio.Runtime.Persistence.DataLooMDbContext dbContext,
        IRequestContextAccessor accessor)
    {
        var rls = new PostgresRlsSessionContext(dbContext, accessor);
        IOutboxWriter outboxWriter = new EfOutboxWriter(dbContext);
        return new EvidenceRegistrationService(dbContext, accessor, new SystemClock(), outboxWriter, rls);
    }

    private async Task SeedTenantAndWorkspaceAsync(TenantId tenantId, WorkspaceId workspaceId, RequestContextAccessor accessor)
    {
        await using var dbContext = fixture.CreateDbContext(accessor);
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            DisplayName = "Synthetic Tenant",
            ExternalAuthority = $"synthetic-{tenantId}",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            TenantId = tenantId,
            Name = "Synthetic Workspace",
            DataResidencyRegion = "local",
            LifecycleState = "Active",
            CreatedBy = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedTenantWorkspaceAndEvidenceAsync(TenantId tenantId, WorkspaceId workspaceId, RequestContextAccessor accessor)
    {
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var service = CreateEvidenceRegistrationService(dbContext, accessor);
        await service.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                "synthetic.txt",
                "text/plain",
                14,
                "2123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "tenant/workspace/synthetic-immutable.txt",
                "default"),
            CancellationToken.None);
    }

    private async Task SeedRawWorkspacesAsync(Guid tenantA, Guid workspaceA, Guid tenantB, Guid workspaceB)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into identity_access.tenants ("Id", "DisplayName", "ExternalAuthority", "LifecycleState", "CreatedBy", "CreatedAt", "ConcurrencyToken")
            values
                (@tenantA, 'Tenant A', @authorityA, 'Active', 'test', now(), @tenantTokenA),
                (@tenantB, 'Tenant B', @authorityB, 'Active', 'test', now(), @tenantTokenB);

            insert into workspace_weave.workspaces ("Id", "TenantId", "Name", "DataResidencyRegion", "LifecycleState", "CreatedBy", "CreatedAt", "ConcurrencyToken")
            values
                (@workspaceA, @tenantA, 'Workspace A', 'local', 'Active', 'test', now(), @workspaceTokenA),
                (@workspaceB, @tenantA, 'Workspace B', 'local', 'Active', 'test', now(), @workspaceTokenB);
            """;
        command.Parameters.AddWithValue("tenantA", tenantA);
        command.Parameters.AddWithValue("tenantB", tenantB);
        command.Parameters.AddWithValue("workspaceA", workspaceA);
        command.Parameters.AddWithValue("workspaceB", workspaceB);
        command.Parameters.AddWithValue("authorityA", $"tenant-a-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("authorityB", $"tenant-b-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("tenantTokenA", Guid.NewGuid());
        command.Parameters.AddWithValue("tenantTokenB", Guid.NewGuid());
        command.Parameters.AddWithValue("workspaceTokenA", Guid.NewGuid());
        command.Parameters.AddWithValue("workspaceTokenB", Guid.NewGuid());

        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountVisibleWorkspacesAsync(Guid? tenantId, Guid? workspaceId)
    {
        await using var connection = new NpgsqlConnection(fixture.ApplicationConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        if (tenantId.HasValue && workspaceId.HasValue)
        {
            await SetLocalContextAsync(connection, transaction, tenantId.Value, workspaceId.Value);
        }

        var count = await CountVisibleWorkspacesAsync(connection, transaction);
        await transaction.CommitAsync();
        return count;
    }

    private async Task<int> CountSpecificVisibleWorkspaceAsync(Guid tenantId, Guid workspaceContextId, Guid targetWorkspaceId)
    {
        await using var connection = new NpgsqlConnection(fixture.ApplicationConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetLocalContextAsync(connection, transaction, tenantId, workspaceContextId);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """select count(*) from workspace_weave.workspaces where "Id" = @workspaceId""";
        command.Parameters.AddWithValue("workspaceId", targetWorkspaceId);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        await transaction.CommitAsync();
        return count;
    }

    private static async Task<int> CountVisibleWorkspacesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select count(*) from workspace_weave.workspaces";

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SetLocalContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid workspaceId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            select
                set_config('app.tenant_id', @tenantId, true),
                set_config('app.workspace_id', @workspaceId, true)
            """;
        command.Parameters.AddWithValue("tenantId", tenantId.ToString("D"));
        command.Parameters.AddWithValue("workspaceId", workspaceId.ToString("D"));

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(string connectionString, string sql)
    {
        var values = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}