using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLooMStudio.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityRemediationEvidenceImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                do $migration$
                begin
                    if exists (select 1 from evidence.evidence_content_verifications) then
                        raise exception 'Existing Evidence content verification rows require separately governed immutable Blob-version reconciliation before this migration can proceed.';
                    end if;
                end
                $migration$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "StorageEntityTag",
                schema: "evidence",
                table: "evidence_content_verifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "StorageVersionId",
                schema: "evidence",
                table: "evidence_content_verifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false);

            migrationBuilder.Sql(
                """
                alter table evidence.evidence_content_verifications
                    add constraint ck_evidence_content_verifications_storage_version
                    check (length("StorageVersionId") > 0 and length("StorageEntityTag") > 0);

                alter table evidence.evidence_upload_allocations
                    drop constraint ck_evidence_upload_allocations_write_only;
                alter table evidence.evidence_upload_allocations
                    add constraint ck_evidence_upload_allocations_create_only
                    check ("PermittedOperation" = 'Create');

                create or replace function foundation.reject_immutable_evidence_mutation()
                returns trigger
                language plpgsql
                set search_path = pg_catalog
                as $function$
                begin
                    raise exception 'Immutable assurance evidence in %.% cannot be updated or deleted.', TG_TABLE_SCHEMA, TG_TABLE_NAME
                        using errcode = '55000';
                end
                $function$;

                revoke all on function foundation.reject_immutable_evidence_mutation() from public;

                create or replace function foundation.protect_governance_evidence_fields()
                returns trigger
                language plpgsql
                set search_path = pg_catalog
                as $function$
                declare
                    immutable_column text;
                begin
                    if TG_OP = 'DELETE' then
                        raise exception 'Governance evidence in %.% cannot be deleted.', TG_TABLE_SCHEMA, TG_TABLE_NAME
                            using errcode = '55000';
                    end if;

                    foreach immutable_column in array TG_ARGV loop
                        if (to_jsonb(OLD) -> immutable_column) is distinct from (to_jsonb(NEW) -> immutable_column) then
                            raise exception 'Governance evidence column %.%.% cannot be changed.', TG_TABLE_SCHEMA, TG_TABLE_NAME, immutable_column
                                using errcode = '55000';
                        end if;
                    end loop;

                    return NEW;
                end
                $function$;

                revoke all on function foundation.protect_governance_evidence_fields() from public;

                create trigger reject_evidence_version_mutation
                    before update or delete on evidence.evidence_versions
                    for each row execute function foundation.reject_immutable_evidence_mutation();
                create trigger reject_content_verification_mutation
                    before update or delete on evidence.evidence_content_verifications
                    for each row execute function foundation.reject_immutable_evidence_mutation();
                create trigger reject_audit_entry_mutation
                    before update or delete on audit_lineage.audit_entries
                    for each row execute function foundation.reject_immutable_evidence_mutation();
                create trigger reject_lineage_relationship_mutation
                    before update or delete on audit_lineage.lineage_relationships
                    for each row execute function foundation.reject_immutable_evidence_mutation();
                create trigger reject_deletion_eligibility_evidence_mutation
                    before update or delete on retention.deletion_eligibility_evaluations
                    for each row execute function foundation.reject_immutable_evidence_mutation();

                create trigger protect_product_actor_evidence
                    before update or delete on identity_access.product_actors
                    for each row execute function foundation.protect_governance_evidence_fields(
                        'Id', 'TenantId', 'WorkspaceId', 'Subject', 'ActorType', 'CreatedBy', 'CreatedAt');
                create trigger protect_tenant_membership_evidence
                    before update or delete on identity_access.product_tenant_memberships
                    for each row execute function foundation.protect_governance_evidence_fields(
                        'Id', 'TenantId', 'ActorId', 'ActorSubject', 'GrantedBy', 'GrantedAt', 'IdempotencyKey', 'RequestHash');
                create trigger protect_workspace_membership_evidence
                    before update or delete on identity_access.product_workspace_memberships
                    for each row execute function foundation.protect_governance_evidence_fields(
                        'Id', 'TenantId', 'WorkspaceId', 'ActorId', 'ActorSubject', 'GrantedBy', 'GrantedAt', 'IdempotencyKey', 'RequestHash');
                create trigger protect_permission_assignment_evidence
                    before update or delete on identity_access.product_permission_assignments
                    for each row execute function foundation.protect_governance_evidence_fields(
                        'Id', 'TenantId', 'WorkspaceId', 'ActorId', 'ActorSubject', 'PermissionKey', 'ResourceType', 'ResourceId',
                        'AssignedBy', 'AssignedAt', 'EffectiveFrom', 'IdempotencyKey', 'RequestHash');
                create trigger protect_authority_elevation_evidence
                    before update or delete on identity_access.product_authority_elevations
                    for each row execute function foundation.protect_governance_evidence_fields(
                        'Id', 'TenantId', 'WorkspaceId', 'ActorId', 'ActorSubject', 'ElevationType', 'RequestedCapability',
                        'PermissionKey', 'ResourceType', 'ResourceId', 'Reason', 'RequestedBy', 'RequestedAt', 'EffectiveFrom',
                        'ExpiresAt', 'RequiresExternalStrongAuthentication', 'SecurityNotificationRequired',
                        'PostEventReviewRequired', 'CorrelationId');
                create trigger protect_disposal_request_evidence
                    before update or delete on retention.disposal_records
                    for each row execute function foundation.protect_governance_evidence_fields(
                        'Id', 'TenantId', 'WorkspaceId', 'EvidenceId', 'DeletionEligibilityEvaluationId', 'RetentionPolicyKey',
                        'RetentionExpiresAt', 'LifecycleState', 'StorageObjectReference', 'ExpectedSha256Hash', 'RequestedBy',
                        'RequestReason', 'RequestedAt', 'RequestAuthorityVersion', 'RequestPolicyIdentifier',
                        'RequestPolicyVersion', 'IdempotencyKey', 'RequestHash');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                alter table evidence.evidence_upload_allocations
                    drop constraint if exists ck_evidence_upload_allocations_create_only;
                alter table evidence.evidence_upload_allocations
                    add constraint ck_evidence_upload_allocations_write_only
                    check ("PermittedOperation" = 'Write');

                drop trigger if exists protect_disposal_request_evidence on retention.disposal_records;
                drop trigger if exists protect_authority_elevation_evidence on identity_access.product_authority_elevations;
                drop trigger if exists protect_permission_assignment_evidence on identity_access.product_permission_assignments;
                drop trigger if exists protect_workspace_membership_evidence on identity_access.product_workspace_memberships;
                drop trigger if exists protect_tenant_membership_evidence on identity_access.product_tenant_memberships;
                drop trigger if exists protect_product_actor_evidence on identity_access.product_actors;
                drop trigger if exists reject_deletion_eligibility_evidence_mutation on retention.deletion_eligibility_evaluations;
                drop trigger if exists reject_lineage_relationship_mutation on audit_lineage.lineage_relationships;
                drop trigger if exists reject_audit_entry_mutation on audit_lineage.audit_entries;
                drop trigger if exists reject_content_verification_mutation on evidence.evidence_content_verifications;
                drop trigger if exists reject_evidence_version_mutation on evidence.evidence_versions;
                drop function if exists foundation.protect_governance_evidence_fields();
                drop function if exists foundation.reject_immutable_evidence_mutation();
                """);

            migrationBuilder.DropColumn(
                name: "StorageEntityTag",
                schema: "evidence",
                table: "evidence_content_verifications");

            migrationBuilder.DropColumn(
                name: "StorageVersionId",
                schema: "evidence",
                table: "evidence_content_verifications");
        }
    }
}