using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceRegistrationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegistrationIdempotencyKey",
                schema: "evidence",
                table: "evidence_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationRequestHash",
                schema: "evidence",
                table: "evidence_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_records_TenantId_WorkspaceId_RegistrationIdempoten~",
                schema: "evidence",
                table: "evidence_records",
                columns: new[] { "TenantId", "WorkspaceId", "RegistrationIdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_evidence_records_TenantId_WorkspaceId_RegistrationIdempoten~",
                schema: "evidence",
                table: "evidence_records");

            migrationBuilder.DropColumn(
                name: "RegistrationIdempotencyKey",
                schema: "evidence",
                table: "evidence_records");

            migrationBuilder.DropColumn(
                name: "RegistrationRequestHash",
                schema: "evidence",
                table: "evidence_records");
        }
    }
}