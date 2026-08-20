using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceDisposalControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "disposal_records",
                schema: "retention",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletionEligibilityEvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetentionPolicyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RetentionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageObjectReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpectedSha256Hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
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
                    QueuedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExecutionStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StorageDisposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutionAuthorityVersion = table.Column<long>(type: "bigint", nullable: true),
                    ExecutionPolicyIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExecutionPolicyVersion = table.Column<int>(type: "integer", nullable: true),
                    StorageDisposition = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EvidencePhysicallyDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ReconciledBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovalIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovalRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QueueIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    QueueRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExecutionIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExecutionRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReconciliationIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReconciliationRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disposal_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId",
                schema: "retention",
                table: "disposal_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_ApprovalIdempotencyKey",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "ApprovalIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_EvidenceId_State",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_ExecutionIdempotencyK~",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "ExecutionIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_IdempotencyKey",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_QueueIdempotencyKey",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "QueueIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_ReconciliationIdempot~",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "ReconciliationIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disposal_records_TenantId_WorkspaceId_State_QueuedAt",
                schema: "retention",
                table: "disposal_records",
                columns: new[] { "TenantId", "WorkspaceId", "State", "QueuedAt" });

            migrationBuilder.Sql(
                """
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_permission_key;
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_resource_scope;
                alter table identity_access.product_authority_elevations
                    drop constraint if exists CK_product_authority_elevations_permission_key;
                alter table identity_access.product_authority_elevations
                    drop constraint if exists CK_product_authority_elevations_resource_scope;

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
                        'Evidence.Disposal.Request',
                        'Evidence.Disposal.Approve',
                        'Evidence.Disposal.Queue',
                        'Workload.EvidenceDisposal.Execute',
                        'Workload.EvidenceDisposal.Reconcile',
                        'Workload.Outbox.Process',
                        'Workload.EvidenceContent.Scan',
                        'Workload.Outbox.Reconcile'
                    ));

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_resource_scope
                    check ("ResourceType" in ('*', 'Evidence', 'EvidenceReview', 'EvidenceLineage', 'Workflow', 'SupportDiagnostics', 'GovernanceRetention', 'GovernanceLegalHold', 'EvidenceDisposal') and "ResourceId" <> '');

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
                        'Evidence.Disposal.Request',
                        'Evidence.Disposal.Approve',
                        'Evidence.Disposal.Queue',
                        'Workload.EvidenceDisposal.Execute',
                        'Workload.EvidenceDisposal.Reconcile',
                        'Workload.Outbox.Process',
                        'Workload.EvidenceContent.Scan',
                        'Workload.Outbox.Reconcile'
                    ));

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_resource_scope
                    check ("ResourceType" in ('*', 'Evidence', 'EvidenceReview', 'EvidenceLineage', 'Workflow', 'SupportDiagnostics', 'GovernanceRetention', 'GovernanceLegalHold', 'EvidenceDisposal') and "ResourceId" <> '');

                alter table retention.disposal_records enable row level security;
                alter table retention.disposal_records force row level security;
                create policy tenant_workspace_context_isolation on retention.disposal_records
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table retention.disposal_records
                    add constraint CK_disposal_records_state
                    check ("State" in ('Requested', 'Approved', 'Queued', 'Executing', 'StorageDisposed', 'Reconciled', 'Completed', 'Denied', 'Failed', 'Suspended', 'Cancelled'));

                alter table retention.disposal_records
                    add constraint CK_disposal_records_attempt_count
                    check ("AttemptCount" >= 0);

                alter table retention.disposal_records
                    add constraint CK_disposal_records_authority_versions
                    check (
                        "RequestAuthorityVersion" > 0
                        and ("ApprovalAuthorityVersion" is null or "ApprovalAuthorityVersion" > 0)
                        and ("ExecutionAuthorityVersion" is null or "ExecutionAuthorityVersion" > 0)
                    );

                alter table retention.disposal_records
                    add constraint CK_disposal_records_no_physical_deletion_claim
                    check ("EvidencePhysicallyDeleted" = false);

                alter table retention.disposal_records
                    add constraint CK_disposal_records_approval_state
                    check (
                        ("State" = 'Requested'
                            and "ApprovedBy" is null
                            and "ApprovalReason" is null
                            and "ApprovedAt" is null
                            and "ApprovalAuthorityVersion" is null
                            and "ApprovalPolicyIdentifier" is null
                            and "ApprovalPolicyVersion" is null
                            and "ApprovalIdempotencyKey" is null
                            and "ApprovalRequestHash" is null)
                        or
                        ("State" <> 'Requested'
                            and "ApprovedBy" is not null
                            and "ApprovalReason" is not null
                            and "ApprovedAt" is not null
                            and "ApprovalAuthorityVersion" is not null
                            and "ApprovalPolicyIdentifier" is not null
                            and "ApprovalPolicyVersion" is not null
                            and "ApprovalIdempotencyKey" is not null
                            and "ApprovalRequestHash" is not null)
                    );

                alter table retention.disposal_records
                    add constraint CK_disposal_records_queue_state
                    check (
                        "State" not in ('Queued', 'Executing', 'StorageDisposed', 'Reconciled', 'Completed', 'Failed', 'Suspended')
                        or
                        ("QueuedBy" is not null
                            and "QueuedAt" is not null
                            and "QueueIdempotencyKey" is not null
                            and "QueueRequestHash" is not null)
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop policy if exists tenant_workspace_context_isolation on retention.disposal_records;

                alter table retention.disposal_records
                    drop constraint if exists CK_disposal_records_queue_state;
                alter table retention.disposal_records
                    drop constraint if exists CK_disposal_records_approval_state;
                alter table retention.disposal_records
                    drop constraint if exists CK_disposal_records_no_physical_deletion_claim;
                alter table retention.disposal_records
                    drop constraint if exists CK_disposal_records_authority_versions;
                alter table retention.disposal_records
                    drop constraint if exists CK_disposal_records_attempt_count;
                alter table retention.disposal_records
                    drop constraint if exists CK_disposal_records_state;

                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_permission_key;
                alter table identity_access.product_permission_assignments
                    drop constraint if exists CK_product_permission_assignments_resource_scope;
                alter table identity_access.product_authority_elevations
                    drop constraint if exists CK_product_authority_elevations_permission_key;
                alter table identity_access.product_authority_elevations
                    drop constraint if exists CK_product_authority_elevations_resource_scope;

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

                alter table identity_access.product_permission_assignments
                    add constraint CK_product_permission_assignments_resource_scope
                    check ("ResourceType" in ('*', 'Evidence', 'EvidenceReview', 'EvidenceLineage', 'Workflow', 'SupportDiagnostics', 'GovernanceRetention', 'GovernanceLegalHold') and "ResourceId" <> '');

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

                alter table identity_access.product_authority_elevations
                    add constraint CK_product_authority_elevations_resource_scope
                    check ("ResourceType" in ('*', 'Evidence', 'EvidenceReview', 'EvidenceLineage', 'Workflow', 'SupportDiagnostics', 'GovernanceRetention', 'GovernanceLegalHold') and "ResourceId" <> '');
                """);

            migrationBuilder.DropTable(
                name: "disposal_records",
                schema: "retention");
        }
    }
}