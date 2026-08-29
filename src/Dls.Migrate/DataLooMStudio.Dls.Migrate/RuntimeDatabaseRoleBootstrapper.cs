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

        var apiSchemaGrants = string.Join(
            Environment.NewLine,
            ApiSchemas.Select(schema => BuildApiSchemaGrant(schema, apiRole)));

        command.CommandText = $"""
            grant connect on database {quotedDatabase} to {apiRole}, {workerRole};
            {apiSchemaGrants}
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

    private static string BuildApiSchemaGrant(string schema, string role)
    {
        var quotedSchema = QuoteIdentifier(schema);
        return $"""
            grant usage on schema {quotedSchema} to {role};
            grant select, insert, update, delete on all tables in schema {quotedSchema} to {role};
            grant usage, select on all sequences in schema {quotedSchema} to {role};
            alter default privileges in schema {quotedSchema} grant select, insert, update, delete on tables to {role};
            alter default privileges in schema {quotedSchema} grant usage, select on sequences to {role};
            """;
    }

    private static string QuoteIdentifier(string identifier) =>
        new NpgsqlCommandBuilder().QuoteIdentifier(identifier);

    private sealed record DatabaseRole(string Name, Guid ObjectId);

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