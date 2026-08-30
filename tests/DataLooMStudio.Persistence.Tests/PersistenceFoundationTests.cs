using DataLooMStudio.Dls.Migrate;
using DataLooMStudio.Infrastructure.Clock;
using DataLooMStudio.Infrastructure.Database;
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
using Microsoft.Extensions.Configuration;

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
    public async Task Worker_outbox_functions_are_execute_only_scope_preserving_and_lease_safe()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var message = new OutboxMessage
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            OwningModule = "Evidence",
            MessageType = "SyntheticScopePreservation",
            PayloadJson = "{\"synthetic\":true}",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UnixEpoch,
            AvailableAt = DateTimeOffset.UnixEpoch
        };
        await using (var dbContext = fixture.CreateDbContext(CreateRequestContext(tenantId, workspaceId)))
        {
            dbContext.OutboxMessages.Add(message);
            await dbContext.SaveChangesAsync();
        }

        var apiRole = $"dls_api_test_{Guid.NewGuid():N}"[..32];
        var workerRole = $"dls_worker_test_{Guid.NewGuid():N}"[..32];
        var quotedApiRole = new NpgsqlCommandBuilder().QuoteIdentifier(apiRole);
        var quotedWorkerRole = new NpgsqlCommandBuilder().QuoteIdentifier(workerRole);
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var createRoles = admin.CreateCommand();
            createRoles.CommandText = $"""
                create role {quotedApiRole} login password 'postgres';
                create role {quotedWorkerRole} login password 'postgres';
                """;
            await createRoles.ExecuteNonQueryAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DataLooM"] = fixture.AdminConnectionString,
                ["DataLooM:DatabaseRoles:ApiName"] = apiRole,
                ["DataLooM:DatabaseRoles:ApiObjectId"] = Guid.NewGuid().ToString("D"),
                ["DataLooM:DatabaseRoles:WorkerName"] = workerRole,
                ["DataLooM:DatabaseRoles:WorkerObjectId"] = Guid.NewGuid().ToString("D")
            })
            .Build();
        await new RuntimeDatabaseRoleBootstrapper(configuration, new UnexpectedDatabaseTokenProvider())
            .ApplyGrantsAsync(CancellationToken.None);

        var workerConnectionString = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString)
        {
            Username = workerRole,
            Password = "postgres"
        }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(workerConnectionString);
        var store = new PostgresOutboxDispatchStore(dataSource);

        var tableDenied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("select count(*) from foundation.outbox_messages;", connection);
            await command.ExecuteScalarAsync();
        });
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, tableDenied.SqlState);

        var staleLease = Guid.NewGuid();
        var firstClaim = await store.ClaimAsync(1, staleLease, DateTimeOffset.UtcNow.AddSeconds(-1), CancellationToken.None);
        var currentLease = Guid.NewGuid();
        var reclaimed = await store.ClaimAsync(1, currentLease, DateTimeOffset.UtcNow.AddMinutes(2), CancellationToken.None);

        Assert.Single(firstClaim);
        Assert.Single(reclaimed);
        Assert.Equal(message.Id, reclaimed[0].Id);
        Assert.Equal(tenantId, reclaimed[0].TenantId);
        Assert.Equal(workspaceId, reclaimed[0].WorkspaceId);
        Assert.Equal(2, reclaimed[0].Attempts);
        Assert.False(await store.CompleteAsync(message.Id, staleLease, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.True(await store.CompleteAsync(message.Id, currentLease, DateTimeOffset.UtcNow, CancellationToken.None));

        await using var verification = fixture.CreateDbContext(CreateRequestContext(tenantId, workspaceId));
        Assert.Equal(OutboxMessageStatus.Published, (await verification.OutboxMessages.SingleAsync(item => item.Id == message.Id)).Status);
    }

    [Fact]
    public async Task Api_role_is_table_specific_non_destructive_and_immutable_evidence_is_database_enforced()
    {
        var apiRole = $"dls_api_least_{Guid.NewGuid():N}"[..32];
        var workerRole = $"dls_worker_least_{Guid.NewGuid():N}"[..32];
        var quotedApiRole = new NpgsqlCommandBuilder().QuoteIdentifier(apiRole);
        var quotedWorkerRole = new NpgsqlCommandBuilder().QuoteIdentifier(workerRole);
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var createRoles = admin.CreateCommand();
            createRoles.CommandText = $"""
                create role {quotedApiRole} login password 'postgres';
                create role {quotedWorkerRole} login password 'postgres';
                """;
            await createRoles.ExecuteNonQueryAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DataLooM"] = fixture.AdminConnectionString,
                ["DataLooM:DatabaseRoles:ApiName"] = apiRole,
                ["DataLooM:DatabaseRoles:ApiObjectId"] = Guid.NewGuid().ToString("D"),
                ["DataLooM:DatabaseRoles:WorkerName"] = workerRole,
                ["DataLooM:DatabaseRoles:WorkerObjectId"] = Guid.NewGuid().ToString("D")
            })
            .Build();
        await new RuntimeDatabaseRoleBootstrapper(configuration, new UnexpectedDatabaseTokenProvider())
            .ApplyGrantsAsync(CancellationToken.None);

        Assert.True(await HasTablePrivilegeAsync(apiRole, "evidence.evidence_records", "SELECT"));
        Assert.True(await HasTablePrivilegeAsync(apiRole, "evidence.evidence_records", "INSERT"));
        Assert.True(await HasTablePrivilegeAsync(apiRole, "evidence.evidence_records", "UPDATE"));
        Assert.False(await HasTablePrivilegeAsync(apiRole, "evidence.evidence_records", "DELETE"));
        Assert.True(await HasTablePrivilegeAsync(apiRole, "evidence.evidence_versions", "INSERT"));
        Assert.False(await HasTablePrivilegeAsync(apiRole, "evidence.evidence_versions", "UPDATE"));
        Assert.False(await HasTablePrivilegeAsync(apiRole, "audit_lineage.audit_entries", "UPDATE"));
        Assert.False(await HasTablePrivilegeAsync(apiRole, "audit_lineage.audit_entries", "DELETE"));
        Assert.False(await HasTablePrivilegeAsync(apiRole, "retention.disposal_records", "DELETE"));
        Assert.False(await RoleBypassesRlsAsync(apiRole));
        Assert.True(await TriggerExistsAsync("retention", "disposal_records", "protect_disposal_request_evidence"));
        Assert.True(await TriggerExistsAsync("identity_access", "product_permission_assignments", "protect_permission_assignment_evidence"));

        var futureTable = $"future_security_{Guid.NewGuid():N}";
        var quotedFutureTable = new NpgsqlCommandBuilder().QuoteIdentifier(futureTable);
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var createFutureTable = admin.CreateCommand();
            createFutureTable.CommandText = $"create table evidence.{quotedFutureTable} (id uuid primary key);";
            await createFutureTable.ExecuteNonQueryAsync();
        }

        try
        {
            Assert.False(await HasTablePrivilegeAsync(apiRole, $"evidence.{futureTable}", "SELECT"));
            Assert.False(await HasTablePrivilegeAsync(apiRole, $"evidence.{futureTable}", "INSERT"));
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var dropFutureTable = admin.CreateCommand();
            dropFutureTable.CommandText = $"drop table evidence.{quotedFutureTable};";
            await dropFutureTable.ExecuteNonQueryAsync();
        }

        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var registration = await CreateEvidenceRegistrationService(dbContext, accessor).RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                "immutable.txt",
                "text/plain",
                9,
                new string('a', 64),
                "registered/immutable",
                "default",
                $"immutable-{Guid.NewGuid():N}"),
            CancellationToken.None);

        await using var triggerConnection = new NpgsqlConnection(fixture.AdminConnectionString);
        await triggerConnection.OpenAsync();
        var actorId = Guid.NewGuid();
        await using (var insertActor = triggerConnection.CreateCommand())
        {
            insertActor.CommandText =
                """
                insert into identity_access.product_actors
                    ("Id", "TenantId", "WorkspaceId", "Subject", "DisplayName", "ActorType", "State", "AuthorityVersion", "AuthorityChangedAt", "CreatedBy", "CreatedAt", "ConcurrencyToken")
                values
                    (@id, @tenantId, @workspaceId, @subject, 'Immutable Actor', 'Human', 'Active', 1, now(), 'test', now(), @token);
                """;
            insertActor.Parameters.AddWithValue("id", actorId);
            insertActor.Parameters.AddWithValue("tenantId", tenantId.Value);
            insertActor.Parameters.AddWithValue("workspaceId", workspaceId.Value);
            insertActor.Parameters.AddWithValue("subject", $"immutable-actor-{actorId:N}");
            insertActor.Parameters.AddWithValue("token", Guid.NewGuid());
            await insertActor.ExecuteNonQueryAsync();
        }

        var authorityEvidenceUpdateDenied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var update = triggerConnection.CreateCommand();
            update.CommandText = "update identity_access.product_actors set \"Subject\" = 'substituted-actor' where \"Id\" = @id;";
            update.Parameters.AddWithValue("id", actorId);
            await update.ExecuteNonQueryAsync();
        });
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, authorityEvidenceUpdateDenied.SqlState);

        var updateDenied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var update = triggerConnection.CreateCommand();
            update.CommandText = "update evidence.evidence_versions set \"OriginalFileName\" = 'tampered.txt' where \"Id\" = @id;";
            update.Parameters.AddWithValue("id", registration.VersionId.Value);
            await update.ExecuteNonQueryAsync();
        });
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, updateDenied.SqlState);

        var deleteDenied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var delete = triggerConnection.CreateCommand();
            delete.CommandText = "delete from audit_lineage.audit_entries where \"TargetId\" = @targetId;";
            delete.Parameters.AddWithValue("targetId", registration.EvidenceId.ToString());
            await delete.ExecuteNonQueryAsync();
        });
        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, deleteDenied.SqlState);
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
                "default",
                "evidence-reg-001"),
            CancellationToken.None);

        Assert.NotEqual(default, result.EvidenceId);
        Assert.False(result.IdempotentReplay);
        Assert.Equal(1, await dbContext.EvidenceRecords.CountAsync());
        Assert.Equal(1, await dbContext.EvidenceVersions.CountAsync());
        Assert.Equal(1, await dbContext.AuditEntries.CountAsync());
        Assert.Equal(1, await dbContext.LineageRelationships.CountAsync());
        Assert.Equal(1, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Evidence_registration_replays_duplicate_idempotency_key()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var service = CreateEvidenceRegistrationService(dbContext, accessor);
        var request = new EvidenceRegistrationRequest(
            "Document",
            "Internal",
            "synthetic.txt",
            "text/plain",
            14,
            "3123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "tenant/workspace/synthetic-idempotent.txt",
            "default",
            "evidence-reg-idempotent-001");

        var first = await service.RegisterInitialVersionAsync(request, CancellationToken.None);
        var second = await service.RegisterInitialVersionAsync(request, CancellationToken.None);

        Assert.False(first.IdempotentReplay);
        Assert.True(second.IdempotentReplay);
        Assert.Equal(first.EvidenceId, second.EvidenceId);
        Assert.Equal(first.VersionId, second.VersionId);
        Assert.Equal(1, await dbContext.EvidenceRecords.CountAsync());
        Assert.Equal(1, await dbContext.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Evidence_registration_rejects_idempotency_key_conflict()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
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
                "4123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "tenant/workspace/synthetic-conflict-a.txt",
                "default",
                "evidence-reg-conflict-001"),
            CancellationToken.None);

        await Assert.ThrowsAsync<EvidenceRegistrationConflictException>(() => service.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                "different.txt",
                "text/plain",
                14,
                "5123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "tenant/workspace/synthetic-conflict-b.txt",
                "default",
                "evidence-reg-conflict-001"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Evidence_registration_rejects_invalid_command()
    {
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var accessor = CreateRequestContext(tenantId, workspaceId);
        await SeedTenantAndWorkspaceAsync(tenantId, workspaceId, accessor);
        await using var dbContext = fixture.CreateDbContext(accessor);
        var service = CreateEvidenceRegistrationService(dbContext, accessor);

        var exception = await Assert.ThrowsAsync<EvidenceRegistrationValidationException>(() => service.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Unsupported",
                "Internal",
                "synthetic.txt",
                "text/plain",
                14,
                "not-a-sha256",
                "tenant/workspace/synthetic-invalid.txt",
                "default",
                "evidence-reg-invalid-001"),
            CancellationToken.None));

        Assert.Contains("EvidenceType", exception.Errors.Keys);
        Assert.Contains("ContentHash", exception.Errors.Keys);
    }

    [Fact]
    public async Task Evidence_registration_rejects_workspace_outside_active_tenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var workspaceB = WorkspaceId.New();
        var accessorB = CreateRequestContext(tenantB, workspaceB);
        await SeedTenantAndWorkspaceAsync(tenantB, workspaceB, accessorB);
        var accessorAWorkspaceB = CreateRequestContext(tenantA, workspaceB);
        await using var dbContext = fixture.CreateDbContext(accessorAWorkspaceB);
        var service = CreateEvidenceRegistrationService(dbContext, accessorAWorkspaceB);

        await Assert.ThrowsAsync<EvidenceRegistrationForbiddenException>(() => service.RegisterInitialVersionAsync(
            new EvidenceRegistrationRequest(
                "Document",
                "Internal",
                "synthetic.txt",
                "text/plain",
                14,
                "6123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "tenant/workspace/synthetic-forbidden.txt",
                "default",
                "evidence-reg-forbidden-001"),
            CancellationToken.None));
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
                new string('x', 600),
                "text/plain",
                14,
                "1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "tenant/workspace/synthetic-rollback.txt",
                "default",
                "evidence-reg-rollback-001"),
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
        return new EvidenceRegistrationService(
            dbContext,
            accessor,
            new SystemClock(),
            outboxWriter,
            new TestProductAuthorityService(),
            rls);
    }

    private sealed class UnexpectedDatabaseTokenProvider : IDatabaseAccessTokenProvider
    {
        public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Passwordless token acquisition is not expected in the local PostgreSQL fixture.");
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
                "default",
                "evidence-reg-immutable-001"),
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

    private async Task<bool> HasTablePrivilegeAsync(string role, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select has_table_privilege(@role, @table, @privilege);";
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("privilege", privilege);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private async Task<bool> RoleBypassesRlsAsync(string role)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select rolbypassrls from pg_roles where rolname = @role;";
        command.Parameters.AddWithValue("role", role);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private async Task<bool> TriggerExistsAsync(string schema, string table, string trigger)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select exists (
                select 1
                from pg_trigger trigger_definition
                join pg_class table_definition on table_definition.oid = trigger_definition.tgrelid
                join pg_namespace schema_definition on schema_definition.oid = table_definition.relnamespace
                where not trigger_definition.tgisinternal
                    and schema_definition.nspname = @schema
                    and table_definition.relname = @table
                    and trigger_definition.tgname = @trigger);
            """;
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("trigger", trigger);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
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