# DataLooM Studio Backup and Restore Readiness

Artifact: DLS-ENG-PRODUCTION-HARDENING-001

## Authority Boundary

This document defines production-intent recovery controls. It does not claim recovery readiness until restore evidence is captured from an approved environment.

## Backup Availability

- PostgreSQL Flexible Server backup retention is modelled in Bicep.
- Blob storage versioning and delete retention are modelled in Bicep.
- Key Vault soft delete and purge protection are modelled in Bicep.
- Service Bus dead-letter behaviour is modelled for outbox subscriptions.

## Restore Procedure

1. Confirm the recovery objective and incident authority.
2. Freeze writes or route traffic according to the incident commander decision.
3. Restore PostgreSQL into an isolated target.
4. Validate schema and migration history.
5. Validate tenant/workspace row-level isolation.
6. Validate Evidence records, Evidence versions, Audit entries, Lineage relationships, retention policies, legal holds, and DisposalRecords.
7. Validate Blob references and version availability without performing destructive cleanup.
8. Run reconciliation checks for outbox and disposal state.
9. Promote the restored target only through approved production authority.

## Restoration Validation

Required checks:

- Database is reachable and migration history is complete.
- Tenant and workspace scoped records remain isolated.
- Audit and Product Authority audit records are present.
- Lineage IDs and relationship versions are unchanged.
- Retention and legal hold state remains authoritative.
- Disposal records remain immutable and do not claim physical destruction.
- Outbox records are either safely pending, dispatched, or reconciled.

## Recovery Evidence

Capture:

- Restore source and target.
- Restore timestamp.
- Operator and approval chain.
- Validation command output.
- Reconciliation report.
- Any accepted residual risk.

## Post-Restore Reconciliation

- Re-run outbox dispatch reconciliation before re-enabling background processing.
- Check for disposal resurrection signals.
- Do not delete or purge Evidence as part of restore.
- Escalate any Audit or Lineage discontinuity to Security and Architecture.
