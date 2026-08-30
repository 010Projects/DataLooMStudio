using DataLooMStudio.Infrastructure.Database;

using Microsoft.Extensions.Configuration;

using Npgsql;

namespace DataLooMStudio.Dls.Migrate;

public sealed class RuntimeDatabaseRoleBootstrapper(
    IConfiguration configuration,
    IDatabaseAccessTokenProvider tokenProvider)
{
    private static readonly string[] ApiSchemas =
    [
        "foundation",
        "identity_access",
        "workspace_weave",
        "evidence",
        "audit_lineage",
        "retention",
        "commercial",
        "lifecycle",
        "workflow",
        "ai_governance"
    ];

    private static readonly DatabaseTableGrant[] ApiTableGrants =
    [
        new("identity_access", "tenants", "select"),
        new("identity_access", "product_actors", "select, insert, update"),
        new("identity_access", "product_tenant_memberships", "select, insert, update"),
        new("identity_access", "product_workspace_memberships", "select, insert, update"),
        new("identity_access", "product_permission_assignments", "select, insert, update"),
        new("identity_access", "product_authority_elevations", "select, insert, update"),
        new("workspace_weave", "workspaces", "select"),
        new("evidence", "evidence_records", "select, insert, update"),
        new("evidence", "evidence_versions", "select, insert"),
        new("evidence", "evidence_upload_allocations", "select, insert, update"),
        new("evidence", "evidence_content_verifications", "select, insert"),
        new("evidence", "evidence_review_requests", "select, insert, update"),
        new("evidence", "evidence_reviewer_assignments", "select, insert, update"),
        new("evidence", "evidence_candidate_decisions", "select, insert, update"),
        new("audit_lineage", "audit_entries", "select, insert"),
        new("audit_lineage", "lineage_relationships", "select, insert"),
        new("retention", "retention_policies", "select, insert"),
        new("retention", "legal_holds", "select, insert, update"),
        new("retention", "legal_hold_release_requests", "select, insert, update"),
        new("retention", "deletion_eligibility_evaluations", "select, insert"),
        new("retention", "disposal_records", "select, insert, update"),
        new("commercial", "capability_entitlements", "select"),
        new("lifecycle", "lifecycle_records", "select, insert, update"),
        new("workflow", "workflow_runs", "select, insert, update"),
        new("ai_governance", "ai_governance_policies", "select"),
        new("foundation", "outbox_messages", "select, insert")
    ];

    public async Task EnsurePrincipalsAsync(CancellationToken cancellationToken)
    {
        var roles = ResolveRoles();
        await using var connection = await OpenConnectionAsync("postgres", cancellationToken);

        foreach (var role in roles)
        {
            if (await RoleExistsAsync(connection, role.Name, cancellationToken))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "select * from pgaadauth_create_principal_with_oid(@name, @objectId, 'service', false, false);";
            command.Parameters.AddWithValue("name", role.Name);
            command.Parameters.AddWithValue("objectId", role.ObjectId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task ApplyGrantsAsync(CancellationToken cancellationToken)
    {
        var roles = ResolveRoles();
        var apiRole = QuoteIdentifier(roles.Api.Name);
        var workerRole = QuoteIdentifier(roles.Worker.Name);
        var databaseName = new NpgsqlConnectionStringBuilder(ResolveConnectionString()).Database
            ?? "dataloomstudio";
        var quotedDatabase = QuoteIdentifier(databaseName);

        await using var connection = await OpenConnectionAsync(databaseName, cancellationToken);
        await using var command = connection.CreateCommand();

        var apiSchemaResets = string.Join(
            Environment.NewLine,
            ApiSchemas.Select(schema => BuildApiSchemaReset(schema, apiRole)));
        var apiTableGrants = string.Join(
            Environment.NewLine,
            ApiTableGrants.Select(grant => BuildApiTableGrant(grant, apiRole)));

        command.CommandText = $"""
            grant connect on database {quotedDatabase} to {apiRole}, {workerRole};
            {apiSchemaResets}
            {apiTableGrants}
            do $bootstrap$
            begin
                if not exists (select 1 from pg_roles where rolname = 'dls_outbox_executor') then
                    create role dls_outbox_executor nologin bypassrls;
                else
                    alter role dls_outbox_executor nologin bypassrls;
                end if;
            end
            $bootstrap$;
            grant dls_outbox_executor to current_user;
            grant usage on schema foundation to dls_outbox_executor;
            grant select, update on table foundation.outbox_messages to dls_outbox_executor;
            alter function foundation.claim_outbox_messages(integer, uuid, timestamp with time zone) owner to dls_outbox_executor;
            alter function foundation.complete_outbox_message(uuid, uuid, timestamp with time zone) owner to dls_outbox_executor;
            alter function foundation.fail_outbox_message(uuid, uuid, timestamp with time zone, text, boolean) owner to dls_outbox_executor;
            alter function foundation.outbox_backlog_count() owner to dls_outbox_executor;
            revoke all on function foundation.claim_outbox_messages(integer, uuid, timestamp with time zone) from public;
            revoke all on function foundation.complete_outbox_message(uuid, uuid, timestamp with time zone) from public;
            revoke all on function foundation.fail_outbox_message(uuid, uuid, timestamp with time zone, text, boolean) from public;
            revoke all on function foundation.outbox_backlog_count() from public;
            grant usage on schema foundation to {workerRole};
            revoke all on all tables in schema foundation from {workerRole};
            grant execute on function foundation.claim_outbox_messages(integer, uuid, timestamp with time zone) to {workerRole};
            grant execute on function foundation.complete_outbox_message(uuid, uuid, timestamp with time zone) to {workerRole};
            grant execute on function foundation.fail_outbox_message(uuid, uuid, timestamp with time zone, text, boolean) to {workerRole};
            grant execute on function foundation.outbox_backlog_count() to {workerRole};
            revoke dls_outbox_executor from current_user;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ResolveConnectionString())
        {
            Database = database
        };

        if (configuration.GetValue<bool>("DataLooM:PostgreSqlUseManagedIdentity"))
        {
            connectionString.Password = await tokenProvider.GetTokenAsync(cancellationToken);
        }

        var connection = new NpgsqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private string ResolveConnectionString()
    {
        return configuration.GetConnectionString("DataLooM")
            ?? configuration["DataLooM:PostgreSqlConnectionString"]
            ?? throw new InvalidOperationException("A migration database connection string is required.");
    }

    private DatabaseRoles ResolveRoles()
    {
        return new DatabaseRoles(
            ResolveRole("Api"),
            ResolveRole("Worker"));
    }

    private DatabaseRole ResolveRole(string kind)
    {
        var name = configuration[$"DataLooM:DatabaseRoles:{kind}Name"];
        var objectId = configuration[$"DataLooM:DatabaseRoles:{kind}ObjectId"];

        if (string.IsNullOrWhiteSpace(name) || !Guid.TryParse(objectId, out var parsedObjectId))
        {
            throw new InvalidOperationException(
                $"DataLooM:DatabaseRoles:{kind}Name and a valid {kind}ObjectId are required for role bootstrap.");
        }

        return new DatabaseRole(name, parsedObjectId);
    }

    private static async Task<bool> RoleExistsAsync(
        NpgsqlConnection connection,
        string roleName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select exists(select 1 from pg_roles where rolname = @name);";
        command.Parameters.AddWithValue("name", roleName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static string BuildApiSchemaReset(string schema, string role)
    {
        var quotedSchema = QuoteIdentifier(schema);
        return $"""
            revoke all on all tables in schema {quotedSchema} from {role};
            revoke all on all sequences in schema {quotedSchema} from {role};
            alter default privileges in schema {quotedSchema} revoke all on tables from {role};
            alter default privileges in schema {quotedSchema} revoke all on sequences from {role};
            grant usage on schema {quotedSchema} to {role};
            """;
    }

    private static string BuildApiTableGrant(DatabaseTableGrant grant, string role) =>
        $"grant {grant.Permissions} on table {QuoteIdentifier(grant.Schema)}.{QuoteIdentifier(grant.Table)} to {role};";

    private static string QuoteIdentifier(string identifier) =>
        new NpgsqlCommandBuilder().QuoteIdentifier(identifier);

    private sealed record DatabaseRole(string Name, Guid ObjectId);

    private sealed record DatabaseTableGrant(string Schema, string Table, string Permissions);

    private sealed record DatabaseRoles(DatabaseRole Api, DatabaseRole Worker) : IEnumerable<DatabaseRole>
    {
        public IEnumerator<DatabaseRole> GetEnumerator()
        {
            yield return Api;
            yield return Worker;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}