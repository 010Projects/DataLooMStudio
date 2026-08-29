# Isolated Test Recovery Drill

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

This package is executable only after Test deployment and recovery-drill authority. Never restore over the active Test target.

1. Record source PostgreSQL backup/restore point, Blob versioning state, canonical commit, image digests, migration history, Test-only dataset IDs, and approval chain.
2. Suspend API rollout and set `DataLooM__WorkerProcessingEnabled=false`; confirm no outbox claims are active.
3. Restore PostgreSQL to a new isolated server and restore/copy the approved Blob versions to an isolated storage target. Do not purge the source.
4. Connect only the migration/readiness identity. Confirm `foundation.__ef_migrations_history`, schema hashes, row counts by tenant/workspace, and forced RLS policies.
5. Validate Evidence IDs/versions/hashes, immutable Lineage IDs and relationship versions, append-only Audit records, retention/Legal Hold state, and immutable DisposalRecords.
6. Run hostile cross-Tenant/cross-Workspace tests against the isolated target. Any readable foreign row fails the drill.
7. Reconcile blob references and hashes. Missing, changed, or resurrected content is quarantined; it is never declared irrecoverably destroyed.
8. Reconcile outbox leases: expired `Processing` records return through the claim lease; published message IDs remain duplicate-detection keys.
9. Confirm physical disposal adapter remains absent, automatic purge absent, and AI execution absent.
10. Capture commands, logs, timestamps, source/target IDs, test results, and discrepancies. Destroy the isolated recovery target only under separate cleanup authority.

Stop on Audit/Lineage discontinuity, RLS failure, Legal Hold mismatch, unapproved customer data, missing backup evidence, destructive command, or ambiguous disposal resurrection. Security and Architecture decide disposition before any further rollout.
