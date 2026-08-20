using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentionReleaseDeletionEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReleaseReason",
                schema: "retention",
                table: "legal_holds",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deletion_eligibility_evaluations",
                schema: "retention",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetentionPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetentionPolicyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RetentionCommencedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetentionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HasActiveLegalHold = table.Column<bool>(type: "boolean", nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EvaluatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AuthorityVersion = table.Column<long>(type: "bigint", nullable: false),
                    PolicyIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deletion_eligibility_evaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legal_hold_release_requests",
                schema: "retention",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalHoldId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestAuthorityVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestPolicyIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestPolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApprovalReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovalAuthorityVersion = table.Column<long>(type: "bigint", nullable: true),
                    ApprovalPolicyIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovalPolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovalIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovalRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_hold_release_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_deletion_eligibility_evaluations_TenantId",
                schema: "retention",
                table: "deletion_eligibility_evaluations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_deletion_eligibility_evaluations_TenantId_WorkspaceId",
                schema: "retention",
                table: "deletion_eligibility_evaluations",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_deletion_eligibility_evaluations_TenantId_WorkspaceId_Evide~",
                schema: "retention",
                table: "deletion_eligibility_evaluations",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_deletion_eligibility_evaluations_TenantId_WorkspaceId_Idemp~",
                schema: "retention",
                table: "deletion_eligibility_evaluations",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_hold_release_requests_TenantId",
                schema: "retention",
                table: "legal_hold_release_requests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_legal_hold_release_requests_TenantId_WorkspaceId",
                schema: "retention",
                table: "legal_hold_release_requests",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_legal_hold_release_requests_TenantId_WorkspaceId_ApprovalId~",
                schema: "retention",
                table: "legal_hold_release_requests",
                columns: new[] { "TenantId", "WorkspaceId", "ApprovalIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_hold_release_requests_TenantId_WorkspaceId_Idempotenc~",
                schema: "retention",
                table: "legal_hold_release_requests",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_hold_release_requests_TenantId_WorkspaceId_LegalHoldI~",
                schema: "retention",
                table: "legal_hold_release_requests",
                columns: new[] { "TenantId", "WorkspaceId", "LegalHoldId", "State" });

            migrationBuilder.Sql(
                """
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_permission_key;
                alter table identity_access.product_authority_elevations
                    drop constraint if exists CK_product_authority_elevations_permission_key;

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
                        'Governance.LegalHold.Release.Request',
                        'Governance.LegalHold.Release.Approve',
                        'Governance.Retention.DeletionEligibility.Evaluate',
                        'Workload.Outbox.Process',
                        'Workload.EvidenceContent.Scan',
                        'Workload.Outbox.Reconcile'
                    ));

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
                        'Governance.LegalHold.Release.Request',
                        'Governance.LegalHold.Release.Approve',
                        'Governance.Retention.DeletionEligibility.Evaluate',
                        'Workload.Outbox.Process',
                        'Workload.EvidenceContent.Scan',
                        'Workload.Outbox.Reconcile'
                    ));

                alter table retention.legal_hold_release_requests enable row level security;
                alter table retention.legal_hold_release_requests force row level security;
                create policy tenant_workspace_context_isolation on retention.legal_hold_release_requests
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table retention.legal_hold_release_requests
                    add constraint CK_legal_hold_release_requests_state
                    check ("State" in ('Pending', 'Approved'));

                alter table retention.legal_hold_release_requests
                    add constraint CK_legal_hold_release_requests_authority_versions
                    check ("RequestAuthorityVersion" > 0 and ("ApprovalAuthorityVersion" is null or "ApprovalAuthorityVersion" > 0));

                alter table retention.legal_hold_release_requests
                    add constraint CK_legal_hold_release_requests_approval_state
                    check (
                        ("State" = 'Pending'
                            and "ApprovedBy" is null
                            and "ApprovalReason" is null
                            and "ApprovedAt" is null
                            and "ApprovalAuthorityVersion" is null
                            and "ApprovalPolicyIdentifier" is null
                            and "ApprovalPolicyVersion" is null
                            and "ApprovalIdempotencyKey" is null
                            and "ApprovalRequestHash" is null)
                        or
                        ("State" = 'Approved'
                            and "ApprovedBy" is not null
                            and "ApprovalReason" is not null
                            and "ApprovedAt" is not null
                            and "ApprovalAuthorityVersion" is not null
                            and "ApprovalPolicyIdentifier" is not null
                            and "ApprovalPolicyVersion" is not null
                            and "ApprovalIdempotencyKey" is not null
                            and "ApprovalRequestHash" is not null)
                    );

                alter table retention.deletion_eligibility_evaluations enable row level security;
                alter table retention.deletion_eligibility_evaluations force row level security;
                create policy tenant_workspace_context_isolation on retention.deletion_eligibility_evaluations
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table retention.deletion_eligibility_evaluations
                    add constraint CK_deletion_eligibility_evaluations_reason_code
                    check ("ReasonCode" in ('Eligible', 'ActiveLegalHold', 'RetentionPolicyMissing', 'RetentionNotExpired', 'LifecycleRestricted'));

                alter table retention.deletion_eligibility_evaluations
                    add constraint CK_deletion_eligibility_evaluations_authority_version
                    check ("AuthorityVersion" > 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop policy if exists tenant_workspace_context_isolation on retention.deletion_eligibility_evaluations;
                drop policy if exists tenant_workspace_context_isolation on retention.legal_hold_release_requests;

                alter table retention.deletion_eligibility_evaluations
                    drop constraint if exists CK_deletion_eligibility_evaluations_authority_version;
                alter table retention.deletion_eligibility_evaluations
                    drop constraint if exists CK_deletion_eligibility_evaluations_reason_code;
                alter table retention.legal_hold_release_requests
                    drop constraint if exists CK_legal_hold_release_requests_approval_state;
                alter table retention.legal_hold_release_requests
                    drop constraint if exists CK_legal_hold_release_requests_authority_versions;
                alter table retention.legal_hold_release_requests
                    drop constraint if exists CK_legal_hold_release_requests_state;

                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_permission_key;
                alter table identity_access.product_authority_elevations
                    drop constraint if exists CK_product_authority_elevations_permission_key;

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
                """);

            migrationBuilder.DropTable(
                name: "deletion_eligibility_evaluations",
                schema: "retention");

            migrationBuilder.DropTable(
                name: "legal_hold_release_requests",
                schema: "retention");

            migrationBuilder.DropColumn(
                name: "ReleaseReason",
                schema: "retention",
                table: "legal_holds");
        }
    }
}