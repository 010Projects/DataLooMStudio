using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetentionGovernanceIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "retention",
                table: "retention_policies",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "retention",
                table: "retention_policies",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "retention",
                table: "retention_policies",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                schema: "retention",
                table: "retention_policies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "retention",
                table: "legal_holds",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "retention",
                table: "legal_holds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReleasedBy",
                schema: "retention",
                table: "legal_holds",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                schema: "retention",
                table: "legal_holds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                update retention.retention_policies
                set
                    "ConcurrencyToken" = "Id",
                    "CreatedBy" = case when "CreatedBy" = '' then 'migration' else "CreatedBy" end,
                    "IdempotencyKey" = 'legacy-retention-policy:' || "Id"::text,
                    "RequestHash" = lpad(md5('legacy-retention-policy|' || "Id"::text || '|' || "PolicyKey"), 64, '0')
                where "IdempotencyKey" = '';

                update retention.legal_holds
                set
                    "ConcurrencyToken" = "Id",
                    "IdempotencyKey" = 'legacy-legal-hold:' || "Id"::text,
                    "RequestHash" = lpad(md5('legacy-legal-hold|' || "Id"::text || '|' || "EvidenceId"::text), 64, '0')
                where "IdempotencyKey" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_retention_policies_TenantId_WorkspaceId_IdempotencyKey",
                schema: "retention",
                table: "retention_policies",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_TenantId_WorkspaceId_IdempotencyKey",
                schema: "retention",
                table: "legal_holds",
                columns: new[] { "TenantId", "WorkspaceId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_retention_policies_TenantId_WorkspaceId_IdempotencyKey",
                schema: "retention",
                table: "retention_policies");

            migrationBuilder.DropIndex(
                name: "IX_legal_holds_TenantId_WorkspaceId_IdempotencyKey",
                schema: "retention",
                table: "legal_holds");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "retention",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "retention",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "retention",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                schema: "retention",
                table: "retention_policies");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "retention",
                table: "legal_holds");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "retention",
                table: "legal_holds");

            migrationBuilder.DropColumn(
                name: "ReleasedBy",
                schema: "retention",
                table: "legal_holds");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                schema: "retention",
                table: "legal_holds");
        }
    }
}