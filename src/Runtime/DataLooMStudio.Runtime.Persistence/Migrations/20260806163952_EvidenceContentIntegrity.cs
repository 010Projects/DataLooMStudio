using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceContentIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidence_content_verifications",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageObjectReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ReceiptIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceiptRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeclaredSize = table.Column<long>(type: "bigint", nullable: false),
                    ActualSize = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedSha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActualSha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntegrityOutcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScanOutcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScannerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScannerVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResultLifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_content_verifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evidence_upload_allocations",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageObjectReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    UploadAuthorityHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PermittedOperation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaxSize = table.Column<long>(type: "bigint", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_upload_allocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_content_verifications_TenantId",
                schema: "evidence",
                table: "evidence_content_verifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_content_verifications_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_content_verifications",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_content_verifications_TenantId_WorkspaceId_Allocat~",
                schema: "evidence",
                table: "evidence_content_verifications",
                columns: new[] { "TenantId", "WorkspaceId", "AllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_content_verifications_TenantId_WorkspaceId_Evidenc~",
                schema: "evidence",
                table: "evidence_content_verifications",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "VersionId", "ReceiptIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_content_verifications_TenantId_WorkspaceId_ResultL~",
                schema: "evidence",
                table: "evidence_content_verifications",
                columns: new[] { "TenantId", "WorkspaceId", "ResultLifecycleState" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_upload_allocations_TenantId",
                schema: "evidence",
                table: "evidence_upload_allocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_upload_allocations_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_upload_allocations",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_upload_allocations_TenantId_WorkspaceId_EvidenceId~",
                schema: "evidence",
                table: "evidence_upload_allocations",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "VersionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_upload_allocations_TenantId_WorkspaceId_Status_Exp~",
                schema: "evidence",
                table: "evidence_upload_allocations",
                columns: new[] { "TenantId", "WorkspaceId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_upload_allocations_TenantId_WorkspaceId_StorageObj~",
                schema: "evidence",
                table: "evidence_upload_allocations",
                columns: new[] { "TenantId", "WorkspaceId", "StorageObjectReference" },
                unique: true);

            migrationBuilder.Sql(
                """
                alter table evidence.evidence_upload_allocations enable row level security;
                alter table evidence.evidence_upload_allocations force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_upload_allocations
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_content_verifications enable row level security;
                alter table evidence.evidence_content_verifications force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_content_verifications
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_upload_allocations
                    add constraint CK_evidence_upload_allocations_no_public_reference
                    check ("StorageObjectReference" !~* '^https?://');

                alter table evidence.evidence_upload_allocations
                    add constraint CK_evidence_upload_allocations_positive_size
                    check ("MaxSize" > 0);

                alter table evidence.evidence_upload_allocations
                    add constraint CK_evidence_upload_allocations_allowed_status
                    check ("Status" in ('Active', 'Expired', 'Consumed'));

                alter table evidence.evidence_upload_allocations
                    add constraint CK_evidence_upload_allocations_write_only
                    check ("PermittedOperation" = 'Write');

                alter table evidence.evidence_content_verifications
                    add constraint CK_evidence_content_verifications_no_public_reference
                    check ("StorageObjectReference" !~* '^https?://');

                alter table evidence.evidence_content_verifications
                    add constraint CK_evidence_content_verifications_non_negative_size
                    check ("DeclaredSize" >= 0 and "ActualSize" >= 0);

                alter table evidence.evidence_content_verifications
                    add constraint CK_evidence_content_verifications_integrity_outcome
                    check ("IntegrityOutcome" in ('Succeeded', 'SizeMismatch', 'HashMismatch', 'NotRun'));

                alter table evidence.evidence_content_verifications
                    add constraint CK_evidence_content_verifications_scan_outcome
                    check ("ScanOutcome" in ('Clean', 'Malicious', 'Suspicious', 'Failed', 'Unavailable', 'Unsupported', 'NotRun'));

                alter table evidence.evidence_content_verifications
                    add constraint CK_evidence_content_verifications_lifecycle_result
                    check ("ResultLifecycleState" in ('Available', 'Quarantined'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_content_verifications",
                schema: "evidence");

            migrationBuilder.DropTable(
                name: "evidence_upload_allocations",
                schema: "evidence");
        }
    }
}