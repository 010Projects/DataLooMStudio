using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TestEnvironmentOutboxDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastError",
                schema: "foundation",
                table: "outbox_messages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                schema: "foundation",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                schema: "foundation",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                create or replace function foundation.claim_outbox_messages(
                    p_batch_size integer,
                    p_lease_id uuid,
                    p_lease_expires_at timestamp with time zone)
                returns table(
                    id uuid,
                    tenant_id uuid,
                    workspace_id uuid,
                    owning_module text,
                    message_type text,
                    payload_json text,
                    correlation_id text,
                    occurred_at timestamp with time zone,
                    available_at timestamp with time zone,
                    attempts integer)
                language sql
                security definer
                set search_path = pg_catalog, foundation
                set row_security = off
                as $function$
                    with candidates as (
                        select message."Id"
                        from foundation.outbox_messages message
                        where (
                            message."Status" in ('Pending', 'Failed')
                            and message."AvailableAt" <= now()
                        ) or (
                            message."Status" = 'Processing'
                            and message."LeaseExpiresAt" < now()
                        )
                        order by message."AvailableAt", message."OccurredAt", message."Id"
                        for update skip locked
                        limit greatest(1, least(p_batch_size, 100))
                    )
                    update foundation.outbox_messages message
                    set "Status" = 'Processing',
                        "Attempts" = message."Attempts" + 1,
                        "LeaseId" = p_lease_id,
                        "LeaseExpiresAt" = p_lease_expires_at,
                        "LastError" = null
                    from candidates
                    where message."Id" = candidates."Id"
                    returning message."Id", message."TenantId", message."WorkspaceId",
                        message."OwningModule"::text, message."MessageType"::text,
                        message."PayloadJson"::text, message."CorrelationId"::text,
                        message."OccurredAt", message."AvailableAt", message."Attempts";
                $function$;

                create or replace function foundation.complete_outbox_message(
                    p_message_id uuid,
                    p_lease_id uuid,
                    p_published_at timestamp with time zone)
                returns boolean
                language sql
                security definer
                set search_path = pg_catalog, foundation
                set row_security = off
                as $function$
                    update foundation.outbox_messages
                    set "Status" = 'Published',
                        "PublishedAt" = p_published_at,
                        "LeaseId" = null,
                        "LeaseExpiresAt" = null,
                        "LastError" = null
                    where "Id" = p_message_id
                        and "LeaseId" = p_lease_id
                        and "Status" = 'Processing'
                    returning true;
                $function$;

                create or replace function foundation.fail_outbox_message(
                    p_message_id uuid,
                    p_lease_id uuid,
                    p_available_at timestamp with time zone,
                    p_last_error text,
                    p_dead_letter boolean)
                returns boolean
                language sql
                security definer
                set search_path = pg_catalog, foundation
                set row_security = off
                as $function$
                    update foundation.outbox_messages
                    set "Status" = case when p_dead_letter then 'DeadLettered' else 'Failed' end,
                        "AvailableAt" = p_available_at,
                        "LeaseId" = null,
                        "LeaseExpiresAt" = null,
                        "LastError" = left(p_last_error, 1024)
                    where "Id" = p_message_id
                        and "LeaseId" = p_lease_id
                        and "Status" = 'Processing'
                    returning true;
                $function$;

                create or replace function foundation.outbox_backlog_count()
                returns bigint
                language sql
                security definer
                set search_path = pg_catalog, foundation
                set row_security = off
                as $function$
                    select count(*) from foundation.outbox_messages
                    where "Status" in ('Pending', 'Failed', 'Processing');
                $function$;

                revoke all on function foundation.claim_outbox_messages(integer, uuid, timestamp with time zone) from public;
                revoke all on function foundation.complete_outbox_message(uuid, uuid, timestamp with time zone) from public;
                revoke all on function foundation.fail_outbox_message(uuid, uuid, timestamp with time zone, text, boolean) from public;
                revoke all on function foundation.outbox_backlog_count() from public;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop function if exists foundation.outbox_backlog_count();
                drop function if exists foundation.fail_outbox_message(uuid, uuid, timestamp with time zone, text, boolean);
                drop function if exists foundation.complete_outbox_message(uuid, uuid, timestamp with time zone);
                drop function if exists foundation.claim_outbox_messages(integer, uuid, timestamp with time zone);
                """);

            migrationBuilder.DropColumn(
                name: "LastError",
                schema: "foundation",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                schema: "foundation",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                schema: "foundation",
                table: "outbox_messages");
        }
    }
}