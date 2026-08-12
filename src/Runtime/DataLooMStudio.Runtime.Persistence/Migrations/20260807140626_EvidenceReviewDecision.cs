using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceReviewDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidence_candidate_decisions",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SupersedesDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppliedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AppliedIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AppliedRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_candidate_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evidence_review_requests",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_review_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evidence_reviewer_assignments",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_reviewer_assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_candidate_decisions_TenantId",
                schema: "evidence",
                table: "evidence_candidate_decisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_candidate_decisions_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_candidate_decisions",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_candidate_decisions_TenantId_WorkspaceId_ReviewReq~",
                schema: "evidence",
                table: "evidence_candidate_decisions",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewRequestId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_candidate_decisions_TenantId_WorkspaceId_ReviewRe~1",
                schema: "evidence",
                table: "evidence_candidate_decisions",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewRequestId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_candidate_decisions_TenantId_WorkspaceId_Supersede~",
                schema: "evidence",
                table: "evidence_candidate_decisions",
                columns: new[] { "TenantId", "WorkspaceId", "SupersedesDecisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_review_requests_TenantId",
                schema: "evidence",
                table: "evidence_review_requests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_review_requests_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_review_requests",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_review_requests_TenantId_WorkspaceId_EvidenceId_Ev~",
                schema: "evidence",
                table: "evidence_review_requests",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "EvidenceVersionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_review_requests_TenantId_WorkspaceId_State",
                schema: "evidence",
                table: "evidence_review_requests",
                columns: new[] { "TenantId", "WorkspaceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_reviewer_assignments_TenantId",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId_ReviewRe~",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewRequestId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_reviewer_assignments_TenantId_WorkspaceId_ReviewR~1",
                schema: "evidence",
                table: "evidence_reviewer_assignments",
                columns: new[] { "TenantId", "WorkspaceId", "ReviewRequestId", "ReviewerSubject", "Role", "IsActive" });

            migrationBuilder.Sql(
                """
                alter table evidence.evidence_review_requests enable row level security;
                alter table evidence.evidence_review_requests force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_review_requests
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_reviewer_assignments enable row level security;
                alter table evidence.evidence_reviewer_assignments force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_reviewer_assignments
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_candidate_decisions enable row level security;
                alter table evidence.evidence_candidate_decisions force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_candidate_decisions
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_review_requests
                    add constraint CK_evidence_review_requests_state
                    check ("State" in ('Requested', 'Assigned', 'CandidateProposed', 'Accepted', 'Rejected', 'CorrectionRequested', 'Superseded'));

                alter table evidence.evidence_review_requests
                    add constraint CK_evidence_review_requests_positive_version
                    check ("Version" > 0);

                alter table evidence.evidence_review_requests
                    add constraint CK_evidence_review_requests_human_requester
                    check ("RequestedBy" <> '' and "RequestedBy" !~* '^(system|shared:|group:)' and "RequestedBy" !~* '@shared');

                alter table evidence.evidence_review_requests
                    add constraint CK_evidence_review_requests_due_after_requested
                    check ("DueAt" is null or "DueAt" > "RequestedAt");

                alter table evidence.evidence_reviewer_assignments
                    add constraint CK_evidence_reviewer_assignments_allowed_role
                    check ("Role" in ('EvidenceReviewer', 'EvidenceApprover'));

                alter table evidence.evidence_reviewer_assignments
                    add constraint CK_evidence_reviewer_assignments_human_reviewer
                    check ("ReviewerSubject" <> '' and "ReviewerSubject" !~* '^(system|shared:|group:)' and "ReviewerSubject" !~* '@shared');

                alter table evidence.evidence_reviewer_assignments
                    add constraint CK_evidence_reviewer_assignments_human_assigner
                    check ("AssignedBy" <> '' and "AssignedBy" !~* '^(system|shared:|group:)' and "AssignedBy" !~* '@shared');

                alter table evidence.evidence_reviewer_assignments
                    add constraint CK_evidence_reviewer_assignments_removed_consistency
                    check (("IsActive" = true and "RemovedAt" is null and "RemovedBy" is null) or ("IsActive" = false and "RemovedAt" is not null and "RemovedBy" is not null));

                alter table evidence.evidence_candidate_decisions
                    add constraint CK_evidence_candidate_decisions_allowed_type
                    check ("DecisionType" in ('Accept', 'Reject', 'RequestCorrection', 'Supersede'));

                alter table evidence.evidence_candidate_decisions
                    add constraint CK_evidence_candidate_decisions_state
                    check ("State" in ('Candidate', 'Accepted', 'Rejected', 'CorrectionRequested', 'Superseded'));

                alter table evidence.evidence_candidate_decisions
                    add constraint CK_evidence_candidate_decisions_positive_version
                    check ("Version" > 0);

                alter table evidence.evidence_candidate_decisions
                    add constraint CK_evidence_candidate_decisions_human_creator
                    check ("CreatedBy" <> '' and "CreatedBy" !~* '^(system|shared:|group:)' and "CreatedBy" !~* '@shared');

                alter table evidence.evidence_candidate_decisions
                    add constraint CK_evidence_candidate_decisions_human_applier
                    check ("AppliedBy" is null or ("AppliedBy" <> '' and "AppliedBy" !~* '^(system|shared:|group:)' and "AppliedBy" !~* '@shared'));

                alter table evidence.evidence_candidate_decisions
                    add constraint CK_evidence_candidate_decisions_supersede_target
                    check (("DecisionType" = 'Supersede' and "SupersedesDecisionId" is not null) or ("DecisionType" <> 'Supersede' and "SupersedesDecisionId" is null));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_candidate_decisions",
                schema: "evidence");

            migrationBuilder.DropTable(
                name: "evidence_review_requests",
                schema: "evidence");

            migrationBuilder.DropTable(
                name: "evidence_reviewer_assignments",
                schema: "evidence");
        }
    }
}