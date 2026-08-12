using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAccessSecurityControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AuthorityVersion",
                schema: "identity_access",
                table: "product_permission_assignments",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                schema: "identity_access",
                table: "product_permission_assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedBy",
                schema: "identity_access",
                table: "product_permission_assignments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                schema: "identity_access",
                table: "product_actors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Human");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorityChangedAt",
                schema: "identity_access",
                table: "product_actors",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<long>(
                name: "AuthorityVersion",
                schema: "identity_access",
                table: "product_actors",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisabledAt",
                schema: "identity_access",
                table: "product_actors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_authority_elevations",
                schema: "identity_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ElevationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedCapability = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorityVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RequiresExternalStrongAuthentication = table.Column<bool>(type: "boolean", nullable: false),
                    SecurityNotificationRequired = table.Column<bool>(type: "boolean", nullable: false),
                    PostEventReviewRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_authority_elevations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_tenant_memberships",
                schema: "identity_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorityVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    GrantedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_tenant_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_workspace_memberships",
                schema: "identity_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorityVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    GrantedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_workspace_memberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_authority_elevations_TenantId",
                schema: "identity_access",
                table: "product_authority_elevations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_authority_elevations_TenantId_WorkspaceId",
                schema: "identity_access",
                table: "product_authority_elevations",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_product_authority_elevations_TenantId_WorkspaceId_ActorSubj~",
                schema: "identity_access",
                table: "product_authority_elevations",
                columns: new[] { "TenantId", "WorkspaceId", "ActorSubject", "PermissionKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_product_authority_elevations_TenantId_WorkspaceId_Elevation~",
                schema: "identity_access",
                table: "product_authority_elevations",
                columns: new[] { "TenantId", "WorkspaceId", "ElevationType", "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_product_tenant_memberships_TenantId",
                schema: "identity_access",
                table: "product_tenant_memberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_tenant_memberships_TenantId_ActorSubject_State",
                schema: "identity_access",
                table: "product_tenant_memberships",
                columns: new[] { "TenantId", "ActorSubject", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_product_tenant_memberships_TenantId_IdempotencyKey",
                schema: "identity_access",
                table: "product_tenant_memberships",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_workspace_memberships_TenantId",
                schema: "identity_access",
                table: "product_workspace_memberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_workspace_memberships_TenantId_WorkspaceId",
                schema: "identity_access",
                table: "product_workspace_memberships",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_product_workspace_memberships_TenantId_WorkspaceId_ActorSub~",
                schema: "identity_access",
                table: "product_workspace_memberships",
                columns: new[] { "TenantId", "WorkspaceId", "ActorSubject", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_product_workspace_memberships_TenantId_WorkspaceId_Idempote~",
                schema: "identity_access",
                table: "product_workspace_memberships",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(
                """
                update identity_access.product_permission_assignments assignment
                set "RevokedAt" = assignment."EffectiveTo"
                where assignment."State" = 'Revoked'
                    and assignment."RevokedAt" is null
                    and assignment."EffectiveTo" is not null;

                insert into identity_access.product_tenant_memberships
                    ("Id", "TenantId", "ActorId", "ActorSubject", "State", "AuthorityVersion", "GrantedBy", "GrantedAt", "RevokedAt", "RevokedBy", "IdempotencyKey", "RequestHash", "ConcurrencyToken")
                select distinct on (actor."TenantId", actor."Subject")
                    (substr(md5('identity-access-tenant-membership|' || actor."TenantId"::text || '|' || actor."Subject"), 1, 8)
                        || '-' || substr(md5('identity-access-tenant-membership|' || actor."TenantId"::text || '|' || actor."Subject"), 9, 4)
                        || '-' || substr(md5('identity-access-tenant-membership|' || actor."TenantId"::text || '|' || actor."Subject"), 13, 4)
                        || '-' || substr(md5('identity-access-tenant-membership|' || actor."TenantId"::text || '|' || actor."Subject"), 17, 4)
                        || '-' || substr(md5('identity-access-tenant-membership|' || actor."TenantId"::text || '|' || actor."Subject"), 21, 12))::uuid,
                    actor."TenantId",
                    actor."Id",
                    actor."Subject",
                    case when actor."State" = 'Active' then 'Active' else 'Revoked' end,
                    actor."AuthorityVersion",
                    actor."CreatedBy",
                    actor."CreatedAt",
                    actor."DisabledAt",
                    case when actor."State" = 'Active' then null else actor."CreatedBy" end,
                    'migration:product-tenant-membership:' || actor."TenantId"::text || ':' || actor."Subject",
                    md5('identity-access-tenant-membership-request|' || actor."TenantId"::text || '|' || actor."Subject")
                        || md5('identity-access-tenant-membership-request-v2|' || actor."TenantId"::text || '|' || actor."Subject"),
                    (substr(md5('identity-access-tenant-membership-token|' || actor."TenantId"::text || '|' || actor."Subject"), 1, 8)
                        || '-' || substr(md5('identity-access-tenant-membership-token|' || actor."TenantId"::text || '|' || actor."Subject"), 9, 4)
                        || '-' || substr(md5('identity-access-tenant-membership-token|' || actor."TenantId"::text || '|' || actor."Subject"), 13, 4)
                        || '-' || substr(md5('identity-access-tenant-membership-token|' || actor."TenantId"::text || '|' || actor."Subject"), 17, 4)
                        || '-' || substr(md5('identity-access-tenant-membership-token|' || actor."TenantId"::text || '|' || actor."Subject"), 21, 12))::uuid
                from identity_access.product_actors actor
                order by actor."TenantId", actor."Subject", actor."CreatedAt"
                on conflict ("TenantId", "IdempotencyKey") do nothing;

                insert into identity_access.product_workspace_memberships
                    ("Id", "TenantId", "WorkspaceId", "ActorId", "ActorSubject", "State", "AuthorityVersion", "GrantedBy", "GrantedAt", "RevokedAt", "RevokedBy", "IdempotencyKey", "RequestHash", "ConcurrencyToken")
                select
                    (substr(md5('identity-access-workspace-membership|' || actor."TenantId"::text || '|' || actor."WorkspaceId"::text || '|' || actor."Subject"), 1, 8)
                        || '-' || substr(md5('identity-access-workspace-membership|' || actor."TenantId"::text || '|' || actor."WorkspaceId"::text || '|' || actor."Subject"), 9, 4)
                        || '-' || substr(md5('identity-access-workspace-membership|' || actor."TenantId"::text || '|' || actor."WorkspaceId"::text || '|' || actor."Subject"), 13, 4)
                        || '-' || substr(md5('identity-access-workspace-membership|' || actor."TenantId"::text || '|' || actor."WorkspaceId"::text || '|' || actor."Subject"), 17, 4)
                        || '-' || substr(md5('identity-access-workspace-membership|' || actor."TenantId"::text || '|' || actor."WorkspaceId"::text || '|' || actor."Subject"), 21, 12))::uuid,
                    actor."TenantId",
                    actor."WorkspaceId",
                    actor."Id",
                    actor."Subject",
                    case when actor."State" = 'Active' then 'Active' else 'Revoked' end,
                    actor."AuthorityVersion",
                    actor."CreatedBy",
                    actor."CreatedAt",
                    actor."DisabledAt",
                    case when actor."State" = 'Active' then null else actor."CreatedBy" end,
                    'migration:product-workspace-membership:' || actor."Id"::text,
                    md5('identity-access-workspace-membership-request|' || actor."Id"::text)
                        || md5('identity-access-workspace-membership-request-v2|' || actor."Id"::text),
                    (substr(md5('identity-access-workspace-membership-token|' || actor."Id"::text), 1, 8)
                        || '-' || substr(md5('identity-access-workspace-membership-token|' || actor."Id"::text), 9, 4)
                        || '-' || substr(md5('identity-access-workspace-membership-token|' || actor."Id"::text), 13, 4)
                        || '-' || substr(md5('identity-access-workspace-membership-token|' || actor."Id"::text), 17, 4)
                        || '-' || substr(md5('identity-access-workspace-membership-token|' || actor."Id"::text), 21, 12))::uuid
                from identity_access.product_actors actor
                on conflict ("TenantId", "WorkspaceId", "IdempotencyKey") do nothing;

                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_permission_key;
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_resource_scope;

                alter table identity_access.product_actors
                    add constraint CK_product_actors_actor_type
                    check ("ActorType" in ('Human', 'Workload', 'Support', 'Emergency'));

                alter table identity_access.product_actors
                    add constraint CK_product_actors_authority_version
                    check ("AuthorityVersion" > 0);

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_permission_key
                    check ("PermissionKey" in (
                        'Evidence.Register',
                        'Evidence.Read',
                        'Evidence.Read.Restricted',
                        'EvidenceReview.Assignments.Manage',
                        'EvidenceReview.CandidateDecision.Create',
                        'EvidenceReview.Decision.Apply',
                        'IdentityAccess.PermissionAssignments.Manage',
                        'Support.Diagnostics.Read',
                        'Support.Elevation.Activate',
                        'Security.BreakGlass.Activate',
                        'Governance.Retention.Manage',
                        'Governance.LegalHold.Manage',
                        'Workload.Outbox.Process',
                        'Workload.EvidenceContent.Scan',
                        'Workload.Outbox.Reconcile'
                    ));

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_resource_scope
                    check ("ResourceType" in ('*', 'Evidence', 'EvidenceReview', 'EvidenceLineage', 'Workflow', 'SupportDiagnostics', 'GovernanceRetention', 'GovernanceLegalHold') and "ResourceId" <> '');

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_authority_version
                    check ("AuthorityVersion" > 0);

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_revocation_state
                    check (("State" = 'Revoked' and "RevokedAt" is not null) or ("State" = 'Active' and "RevokedAt" is null));

                alter table identity_access.product_tenant_memberships enable row level security;
                alter table identity_access.product_tenant_memberships force row level security;
                create policy tenant_context_isolation on identity_access.product_tenant_memberships
                    using ("TenantId" = foundation.current_tenant_id())
                    with check ("TenantId" = foundation.current_tenant_id());

                alter table identity_access.product_tenant_memberships
                    add constraint CK_product_tenant_memberships_state
                    check ("State" in ('Active', 'Revoked'));

                alter table identity_access.product_tenant_memberships
                    add constraint CK_product_tenant_memberships_authority_version
                    check ("AuthorityVersion" > 0);

                alter table identity_access.product_tenant_memberships
                    add constraint CK_product_tenant_memberships_subject
                    check ("ActorSubject" <> '' and "ActorSubject" !~* '^(system|shared:|group:)' and "ActorSubject" !~* '@shared');

                alter table identity_access.product_tenant_memberships
                    add constraint CK_product_tenant_memberships_revocation_state
                    check (("State" = 'Revoked' and "RevokedAt" is not null) or ("State" = 'Active' and "RevokedAt" is null));

                alter table identity_access.product_workspace_memberships enable row level security;
                alter table identity_access.product_workspace_memberships force row level security;
                create policy tenant_workspace_context_isolation on identity_access.product_workspace_memberships
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table identity_access.product_workspace_memberships
                    add constraint CK_product_workspace_memberships_state
                    check ("State" in ('Active', 'Revoked'));

                alter table identity_access.product_workspace_memberships
                    add constraint CK_product_workspace_memberships_authority_version
                    check ("AuthorityVersion" > 0);

                alter table identity_access.product_workspace_memberships
                    add constraint CK_product_workspace_memberships_subject
                    check ("ActorSubject" <> '' and "ActorSubject" !~* '^(system|shared:|group:)' and "ActorSubject" !~* '@shared');

                alter table identity_access.product_workspace_memberships
                    add constraint CK_product_workspace_memberships_revocation_state
                    check (("State" = 'Revoked' and "RevokedAt" is not null) or ("State" = 'Active' and "RevokedAt" is null));

                alter table identity_access.product_authority_elevations enable row level security;
                alter table identity_access.product_authority_elevations force row level security;
                create policy tenant_workspace_context_isolation on identity_access.product_authority_elevations
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_type
                    check ("ElevationType" in ('PrivilegedAccess', 'BreakGlass', 'Support'));

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_state
                    check ("State" in ('Requested', 'Approved', 'Active', 'Expired', 'Revoked'));

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_permission_key
                    check ("PermissionKey" in (
                        'Evidence.Register',
                        'Evidence.Read',
                        'Evidence.Read.Restricted',
                        'EvidenceReview.Assignments.Manage',
                        'EvidenceReview.CandidateDecision.Create',
                        'EvidenceReview.Decision.Apply',
                        'IdentityAccess.PermissionAssignments.Manage',
                        'Support.Diagnostics.Read',
                        'Support.Elevation.Activate',
                        'Security.BreakGlass.Activate',
                        'Governance.Retention.Manage',
                        'Governance.LegalHold.Manage',
                        'Workload.Outbox.Process',
                        'Workload.EvidenceContent.Scan',
                        'Workload.Outbox.Reconcile'
                    ));

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_resource_scope
                    check ("ResourceType" in ('*', 'Evidence', 'EvidenceReview', 'EvidenceLineage', 'Workflow', 'SupportDiagnostics', 'GovernanceRetention', 'GovernanceLegalHold') and "ResourceId" <> '');

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_effective_window
                    check ("EffectiveFrom" < "ExpiresAt");

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_authority_version
                    check ("AuthorityVersion" > 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop policy if exists tenant_workspace_context_isolation on identity_access.product_authority_elevations;
                drop policy if exists tenant_context_isolation on identity_access.product_tenant_memberships;
                drop policy if exists tenant_workspace_context_isolation on identity_access.product_workspace_memberships;

                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_permission_key;
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_resource_scope;
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_authority_version;
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_revocation_state;

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_permission_key
                    check ("PermissionKey" in ('EvidenceReview.Assignments.Manage', 'EvidenceReview.CandidateDecision.Create', 'EvidenceReview.Decision.Apply', 'IdentityAccess.PermissionAssignments.Manage'));

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_resource_scope
                    check ("ResourceType" in ('*', 'EvidenceReview') and "ResourceId" <> '');

                alter table identity_access.product_actors
                    drop constraint if exists CK_product_actors_actor_type;
                alter table identity_access.product_actors
                    drop constraint if exists CK_product_actors_authority_version;
                """);

            migrationBuilder.DropTable(
                name: "product_authority_elevations",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "product_tenant_memberships",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "product_workspace_memberships",
                schema: "identity_access");

            migrationBuilder.DropColumn(
                name: "AuthorityVersion",
                schema: "identity_access",
                table: "product_permission_assignments");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                schema: "identity_access",
                table: "product_permission_assignments");

            migrationBuilder.DropColumn(
                name: "RevokedBy",
                schema: "identity_access",
                table: "product_permission_assignments");

            migrationBuilder.DropColumn(
                name: "ActorType",
                schema: "identity_access",
                table: "product_actors");

            migrationBuilder.DropColumn(
                name: "AuthorityChangedAt",
                schema: "identity_access",
                table: "product_actors");

            migrationBuilder.DropColumn(
                name: "AuthorityVersion",
                schema: "identity_access",
                table: "product_actors");

            migrationBuilder.DropColumn(
                name: "DisabledAt",
                schema: "identity_access",
                table: "product_actors");
        }
    }
}