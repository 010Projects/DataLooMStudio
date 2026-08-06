using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProductPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai_governance");

            migrationBuilder.EnsureSchema(
                name: "audit_lineage");

            migrationBuilder.EnsureSchema(
                name: "commercial");

            migrationBuilder.EnsureSchema(
                name: "evidence");

            migrationBuilder.EnsureSchema(
                name: "retention");

            migrationBuilder.EnsureSchema(
                name: "lifecycle");

            migrationBuilder.EnsureSchema(
                name: "foundation");

            migrationBuilder.EnsureSchema(
                name: "identity_access");

            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.EnsureSchema(
                name: "workspace_weave");

            migrationBuilder.CreateTable(
                name: "ai_governance_policies",
                schema: "ai_governance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AllowsModelExecution = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutionAuthority = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_governance_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit_lineage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthorityContext = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "capability_entitlements",
                schema: "commercial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PlanKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capability_entitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evidence_records",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Classification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisteredBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsImmutable = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnderLegalHold = table.Column<bool>(type: "boolean", nullable: false),
                    RetentionPolicyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "evidence_versions",
                schema: "evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeclaredSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StorageObjectReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IntegrityState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SupersedesVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legal_holds",
                schema: "retention",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PlacedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_holds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_records",
                schema: "lifecycle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lifecycle_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lineage_relationships",
                schema: "audit_lineage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetLineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationshipType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorOrProcess = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lineage_relationships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "foundation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwningModule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "retention_policies",
                schema: "retention",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RetainForDays = table.Column<int>(type: "integer", nullable: false),
                    LegalHoldOverridesDeletion = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "identity_access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalAuthority = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                schema: "workspace_weave",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataResidencyRegion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_governance_policies_TenantId",
                schema: "ai_governance",
                table: "ai_governance_policies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_governance_policies_TenantId_WorkspaceId",
                schema: "ai_governance",
                table: "ai_governance_policies",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_governance_policies_TenantId_WorkspaceId_PolicyKey",
                schema: "ai_governance",
                table: "ai_governance_policies",
                columns: new[] { "TenantId", "WorkspaceId", "PolicyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId",
                schema: "audit_lineage",
                table: "audit_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId_WorkspaceId",
                schema: "audit_lineage",
                table: "audit_entries",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId_WorkspaceId_OccurredAt",
                schema: "audit_lineage",
                table: "audit_entries",
                columns: new[] { "TenantId", "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_capability_entitlements_TenantId",
                schema: "commercial",
                table: "capability_entitlements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_capability_entitlements_TenantId_WorkspaceId",
                schema: "commercial",
                table: "capability_entitlements",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_capability_entitlements_TenantId_WorkspaceId_CapabilityKey_~",
                schema: "commercial",
                table: "capability_entitlements",
                columns: new[] { "TenantId", "WorkspaceId", "CapabilityKey", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_records_TenantId",
                schema: "evidence",
                table: "evidence_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_records_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_records",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_records_TenantId_WorkspaceId_LineageId",
                schema: "evidence",
                table: "evidence_records",
                columns: new[] { "TenantId", "WorkspaceId", "LineageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_records_TenantId_WorkspaceId_Sha256Hash",
                schema: "evidence",
                table: "evidence_records",
                columns: new[] { "TenantId", "WorkspaceId", "Sha256Hash" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_versions_TenantId",
                schema: "evidence",
                table: "evidence_versions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_versions_TenantId_WorkspaceId",
                schema: "evidence",
                table: "evidence_versions",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_versions_TenantId_WorkspaceId_ContentHash",
                schema: "evidence",
                table: "evidence_versions",
                columns: new[] { "TenantId", "WorkspaceId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_versions_TenantId_WorkspaceId_EvidenceId_Sequence",
                schema: "evidence",
                table: "evidence_versions",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_TenantId",
                schema: "retention",
                table: "legal_holds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_TenantId_WorkspaceId",
                schema: "retention",
                table: "legal_holds",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_TenantId_WorkspaceId_EvidenceId_ReleasedAt",
                schema: "retention",
                table: "legal_holds",
                columns: new[] { "TenantId", "WorkspaceId", "EvidenceId", "ReleasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_records_TenantId",
                schema: "lifecycle",
                table: "lifecycle_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_records_TenantId_WorkspaceId",
                schema: "lifecycle",
                table: "lifecycle_records",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_lifecycle_records_TenantId_WorkspaceId_SubjectType_SubjectI~",
                schema: "lifecycle",
                table: "lifecycle_records",
                columns: new[] { "TenantId", "WorkspaceId", "SubjectType", "SubjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lineage_relationships_TenantId",
                schema: "audit_lineage",
                table: "lineage_relationships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_lineage_relationships_TenantId_WorkspaceId",
                schema: "audit_lineage",
                table: "lineage_relationships",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_lineage_relationships_TenantId_WorkspaceId_SourceLineageId_~",
                schema: "audit_lineage",
                table: "lineage_relationships",
                columns: new[] { "TenantId", "WorkspaceId", "SourceLineageId", "TargetLineageId", "RelationshipType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_OwningModule_MessageType",
                schema: "foundation",
                table: "outbox_messages",
                columns: new[] { "OwningModule", "MessageType" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId",
                schema: "foundation",
                table: "outbox_messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_WorkspaceId",
                schema: "foundation",
                table: "outbox_messages",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_TenantId_WorkspaceId_Status_AvailableAt",
                schema: "foundation",
                table: "outbox_messages",
                columns: new[] { "TenantId", "WorkspaceId", "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_TenantId",
                schema: "retention",
                table: "retention_policies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_TenantId_WorkspaceId",
                schema: "retention",
                table: "retention_policies",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_TenantId_WorkspaceId_PolicyKey",
                schema: "retention",
                table: "retention_policies",
                columns: new[] { "TenantId", "WorkspaceId", "PolicyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_ExternalAuthority",
                schema: "identity_access",
                table: "tenants",
                column: "ExternalAuthority",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_TenantId",
                schema: "workflow",
                table: "workflow_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_TenantId_WorkspaceId",
                schema: "workflow",
                table: "workflow_runs",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_TenantId_WorkspaceId_WorkflowKey_Status",
                schema: "workflow",
                table: "workflow_runs",
                columns: new[] { "TenantId", "WorkspaceId", "WorkflowKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_TenantId",
                schema: "workspace_weave",
                table: "workspaces",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_workspaces_TenantId_Name",
                schema: "workspace_weave",
                table: "workspaces",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.Sql(
                """
                create or replace function foundation.current_tenant_id()
                returns uuid
                language sql
                stable
                as $$
                    select nullif(current_setting('app.tenant_id', true), '')::uuid
                $$;

                create or replace function foundation.current_workspace_id()
                returns uuid
                language sql
                stable
                as $$
                    select nullif(current_setting('app.workspace_id', true), '')::uuid
                $$;

                alter table identity_access.tenants enable row level security;
                alter table identity_access.tenants force row level security;
                create policy tenant_context_isolation on identity_access.tenants
                    using ("Id" = foundation.current_tenant_id())
                    with check ("Id" = foundation.current_tenant_id());

                alter table workspace_weave.workspaces enable row level security;
                alter table workspace_weave.workspaces force row level security;
                create policy tenant_workspace_context_isolation on workspace_weave.workspaces
                    using ("TenantId" = foundation.current_tenant_id() and "Id" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "Id" = foundation.current_workspace_id());

                alter table evidence.evidence_records enable row level security;
                alter table evidence.evidence_records force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_records
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_versions enable row level security;
                alter table evidence.evidence_versions force row level security;
                create policy tenant_workspace_context_isolation on evidence.evidence_versions
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table audit_lineage.audit_entries enable row level security;
                alter table audit_lineage.audit_entries force row level security;
                create policy tenant_workspace_context_isolation on audit_lineage.audit_entries
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table audit_lineage.lineage_relationships enable row level security;
                alter table audit_lineage.lineage_relationships force row level security;
                create policy tenant_workspace_context_isolation on audit_lineage.lineage_relationships
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table foundation.outbox_messages enable row level security;
                alter table foundation.outbox_messages force row level security;
                create policy tenant_workspace_context_isolation on foundation.outbox_messages
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table retention.retention_policies enable row level security;
                alter table retention.retention_policies force row level security;
                create policy tenant_workspace_context_isolation on retention.retention_policies
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table retention.legal_holds enable row level security;
                alter table retention.legal_holds force row level security;
                create policy tenant_workspace_context_isolation on retention.legal_holds
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table commercial.capability_entitlements enable row level security;
                alter table commercial.capability_entitlements force row level security;
                create policy tenant_workspace_context_isolation on commercial.capability_entitlements
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table lifecycle.lifecycle_records enable row level security;
                alter table lifecycle.lifecycle_records force row level security;
                create policy tenant_workspace_context_isolation on lifecycle.lifecycle_records
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table workflow.workflow_runs enable row level security;
                alter table workflow.workflow_runs force row level security;
                create policy tenant_workspace_context_isolation on workflow.workflow_runs
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table ai_governance.ai_governance_policies enable row level security;
                alter table ai_governance.ai_governance_policies force row level security;
                create policy tenant_workspace_context_isolation on ai_governance.ai_governance_policies
                    using ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id())
                    with check ("TenantId" = foundation.current_tenant_id() and "WorkspaceId" = foundation.current_workspace_id());

                alter table evidence.evidence_versions
                    add constraint CK_evidence_versions_sequence_positive check ("Sequence" > 0);

                alter table evidence.evidence_versions
                    add constraint CK_evidence_versions_declared_size_non_negative check ("DeclaredSize" >= 0);

                alter table evidence.evidence_records
                    add constraint CK_evidence_records_no_public_blob_url
                    check ("BlobName" !~* '^https?://');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                drop function if exists foundation.current_workspace_id() cascade;
                drop function if exists foundation.current_tenant_id() cascade;
                """);

            migrationBuilder.DropTable(
                name: "ai_governance_policies",
                schema: "ai_governance");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit_lineage");

            migrationBuilder.DropTable(
                name: "capability_entitlements",
                schema: "commercial");

            migrationBuilder.DropTable(
                name: "evidence_records",
                schema: "evidence");

            migrationBuilder.DropTable(
                name: "evidence_versions",
                schema: "evidence");

            migrationBuilder.DropTable(
                name: "legal_holds",
                schema: "retention");

            migrationBuilder.DropTable(
                name: "lifecycle_records",
                schema: "lifecycle");

            migrationBuilder.DropTable(
                name: "lineage_relationships",
                schema: "audit_lineage");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "retention_policies",
                schema: "retention");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "workflow_runs",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "workspaces",
                schema: "workspace_weave");
        }
    }
}