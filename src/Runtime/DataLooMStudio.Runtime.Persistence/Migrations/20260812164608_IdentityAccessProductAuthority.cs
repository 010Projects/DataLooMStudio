using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAccessProductAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId_ReviewR~1",
                schema: "evidence",
                table: "evidence_reviewer_assignments");

            migrationBuilder.Sql(
                """
                alter table evidence.evidence_reviewer_assignments
                    drop constraint CK_evidence_reviewer_assignments_allowed_role;
                """);

            migrationBuilder.RenameColumn(
                name: "Role",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                newName: "PermissionKey");

            migrationBuilder.AlterColumn<string>(
                name: "PermissionKey",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.Sql(
                """
                update evidence.evidence_reviewer_assignments
                set "PermissionKey" = case "PermissionKey"
                    when 'EvidenceReviewer' then 'EvidenceReview.CandidateDecision.Create'
                    when 'EvidenceApprover' then 'EvidenceReview.Decision.Apply'
                    else "PermissionKey"
                end;
                """);

            migrationBuilder.CreateTable(
                name: "product_actors",
                schema: "identity_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_actors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_permission_assignments",
                schema: "identity_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PermissionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_permission_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId_ReviewR~1",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewRequestId", "ReviewerSubject", "PermissionKey", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_product_actors_TenantId",
                schema: "identity_access",
                table: "product_actors",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_actors_TenantId_WorkspaceId",
                schema: "identity_access",
                table: "product_actors",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_product_actors_TenantId_WorkspaceId_State",
                schema: "identity_access",
                table: "product_actors",
                columns: new[] { "TenantId", "WorkspaceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_product_actors_TenantId_WorkspaceId_Subject",
                schema: "identity_access",
                table: "product_actors",
                columns: new[] { "TenantId", "WorkspaceId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_permission_assignments_TenantId",
                schema: "identity_access",
                table: "product_permission_assignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_permission_assignments_TenantId_WorkspaceId",
                schema: "identity_access",
                table: "product_permission_assignments",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_product_permission_assignments_TenantId_WorkspaceId_ActorSu~",
                schema: "identity_access",
                table: "product_permission_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "ActorSubject", "PermissionKey", "ResourceType", "ResourceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_product_permission_assignments_TenantId_WorkspaceId_Idempot~",
                schema: "identity_access",
                table: "product_permission_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(
                """
                insert into identity_access.product_actors
                    ("Id", "TenantId", "WorkspaceId", "Subject", "DisplayName", "State", "CreatedBy", "CreatedAt", "ConcurrencyToken")
                select distinct on (assignment."TenantId", assignment."WorkspaceId", assignment."ReviewerSubject")
                    (substr(md5('identity-access-actor|' || assignment."TenantId"::text || '|' || assignment."WorkspaceId"::text || '|' || assignment."ReviewerSubject"), 1, 8)
                        || '-' || substr(md5('identity-access-actor|' || assignment."TenantId"::text || '|' || assignment."WorkspaceId"::text || '|' || assignment."ReviewerSubject"), 9, 4)
                        || '-' || substr(md5('identity-access-actor|' || assignment."TenantId"::text || '|' || assignment."WorkspaceId"::text || '|' || assignment."ReviewerSubject"), 13, 4)
                        || '-' || substr(md5('identity-access-actor|' || assignment."TenantId"::text || '|' || assignment."WorkspaceId"::text || '|' || assignment."ReviewerSubject"), 17, 4)
                        || '-' || substr(md5('identity-access-actor|' || assignment."TenantId"::text || '|' || assignment."WorkspaceId"::text || '|' || assignment."ReviewerSubject"), 21, 12))::uuid,
                    assignment."TenantId",
                    assignment."WorkspaceId",
                    assignment."ReviewerSubject",
                    assignment."ReviewerSubject",
                    'Active',
                    assignment."AssignedBy",
                    assignment."AssignedAt",
                    (substr(md5('identity-access-actor-token|' || assignment."Id"::text), 1, 8)
                        || '-' || substr(md5('identity-access-actor-token|' || assignment."Id"::text), 9, 4)
                        || '-' || substr(md5('identity-access-actor-token|' || assignment."Id"::text), 13, 4)
                        || '-' || substr(md5('identity-access-actor-token|' || assignment."Id"::text), 17, 4)
                        || '-' || substr(md5('identity-access-actor-token|' || assignment."Id"::text), 21, 12))::uuid
                from evidence.evidence_reviewer_assignments assignment
                order by assignment."TenantId", assignment."WorkspaceId", assignment."ReviewerSubject", assignment."AssignedAt";

                insert into identity_access.product_permission_assignments
                    ("Id", "TenantId", "WorkspaceId", "ActorId", "ActorSubject", "PermissionKey", "ResourceType", "ResourceId", "State", "AssignedBy", "AssignedAt", "EffectiveFrom", "EffectiveTo", "IdempotencyKey", "RequestHash", "ConcurrencyToken")
                select
                    (substr(md5('identity-access-permission|' || assignment."Id"::text), 1, 8)
                        || '-' || substr(md5('identity-access-permission|' || assignment."Id"::text), 9, 4)
                        || '-' || substr(md5('identity-access-permission|' || assignment."Id"::text), 13, 4)
                        || '-' || substr(md5('identity-access-permission|' || assignment."Id"::text), 17, 4)
                        || '-' || substr(md5('identity-access-permission|' || assignment."Id"::text), 21, 12))::uuid,
                    assignment."TenantId",
                    assignment."WorkspaceId",
                    actor."Id",
                    assignment."ReviewerSubject",
                    assignment."PermissionKey",
                    'EvidenceReview',
                    assignment."ReviewRequestId"::text,
                    case when assignment."IsActive" then 'Active' else 'Revoked' end,
                    assignment."AssignedBy",
                    assignment."AssignedAt",
                    null,
                    assignment."RemovedAt",
                    'migration:evidence-review-assignment:' || assignment."Id"::text,
                    md5('identity-access-permission-request|' || assignment."Id"::text) || md5('identity-access-permission-request-v2|' || assignment."Id"::text),
                    (substr(md5('identity-access-permission-token|' || assignment."Id"::text), 1, 8)
                        || '-' || substr(md5('identity-access-permission-token|' || assignment."Id"::text), 9, 4)
                        || '-' || substr(md5('identity-access-permission-token|' || assignment."Id"::text), 13, 4)
                        || '-' || substr(md5('identity-access-permission-token|' || assignment."Id"::text), 17, 4)
                        || '-' || substr(md5('identity-access-permission-token|' || assignment."Id"::text), 21, 12))::uuid
                from evidence.evidence_reviewer_assignments assignment
                join identity_access.product_actors actor on actor."TenantId" = assignment."TenantId"
                    and actor."WorkspaceId" = assignment."WorkspaceId"
                    and actor."Subject" = assignment."ReviewerSubject";

                alter table evidence.evidence_reviewer_assignments
                    add constraint CK_evidence_reviewer_assignments_permission_key
                    check ("PermissionKey" in ('EvidenceReview.CandidateDecision.Create', 'EvidenceReview.Decision.Apply'));

                alter table identity_access.product_actors enable row level security;
                alter table identity_access.product_actors force row level security;
                create policy tenant_workspace_context_isolation on identity_access.product_actors
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table identity_access.product_permission_assignments enable row level security;
                alter table identity_access.product_permission_assignments force row level security;
                create policy tenant_workspace_context_isolation on identity_access.product_permission_assignments
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table identity_access.product_actors
                    add constraint CK_product_actors_state
                    check ("State" in ('Active', 'Disabled'));

                alter table identity_access.product_actors
                    add constraint CK_product_actors_human_subject
                    check ("Subject" <> '' and "Subject" !~* '^(system|shared:|group:)' and "Subject" !~* '@shared');

                alter table identity_access.product_actors
                    add constraint CK_product_actors_human_creator
                    check ("CreatedBy" <> '' and "CreatedBy" !~* '^(system|shared:|group:)' and "CreatedBy" !~* '@shared');

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_permission_key
                    check ("PermissionKey" in ('EvidenceReview.Assignments.Manage', 'EvidenceReview.CandidateDecision.Create', 'EvidenceReview.Decision.Apply', 'IdentityAccess.PermissionAssignments.Manage'));

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_state
                    check ("State" in ('Active', 'Revoked'));

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_human_actor
                    check ("ActorSubject" <> '' and "ActorSubject" !~* '^(system|shared:|group:)' and "ActorSubject" !~* '@shared');

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_human_assigner
                    check ("AssignedBy" <> '' and "AssignedBy" !~* '^(system|shared:|group:)' and "AssignedBy" !~* '@shared');

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_resource_scope
                    check ("ResourceType" in ('*', 'EvidenceReview') and "ResourceId" <> '');

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_effective_window
                    check ("EffectiveFrom" is null or "EffectiveTo" is null or "EffectiveFrom" < "EffectiveTo");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_permission_assignments",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "product_actors",
                schema: "identity_access");

            migrationBuilder.DropIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId_ReviewR~1",
                schema: "evidence",
                table: "evidence_reviewer_assignments");

            migrationBuilder.Sql(
                """
                alter table evidence.evidence_reviewer_assignments
                    drop constraint CK_evidence_reviewer_assignments_permission_key;

                update evidence.evidence_reviewer_assignments
                set "PermissionKey" = case "PermissionKey"
                    when 'EvidenceReview.CandidateDecision.Create' then 'EvidenceReviewer'
                    when 'EvidenceReview.Decision.Apply' then 'EvidenceApprover'
                    else "PermissionKey"
                end;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PermissionKey",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.RenameColumn(
                name: "PermissionKey",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                newName: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId_ReviewR~1",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewRequestId", "ReviewerSubject", "Role", "IsActive" });

            migrationBuilder.Sql(
                """
                alter table evidence.evidence_reviewer_assignments
                    add constraint CK_evidence_reviewer_assignments_allowed_role
                    check ("Role" in ('EvidenceReviewer', 'EvidenceApprover'));
                """);
        }
    }
}