# DLS-ENG-EVIDENCE-DISPOSAL-IMPLEMENTATION-AUTH-HANDOVER-001
# Governed Physical Evidence Disposal Implementation Authority Request Package

## Purpose

Prepare a future Engineering implementation authority request for governed physical Evidence disposal.

This artifact does not grant implementation authority and does not authorize physical deletion, purge execution, production Evidence, production deployment, Restricted Pilot Authority, Production Authority, AI execution, or customer onboarding.

## Current Architecture Position

- Evidence disposal must remain downstream of canonical Product Authority, retention expiry, Legal Hold release, deterministic deletion eligibility, and explicit disposal approval.
- ADR-014 Evidence Consistency Boundary remains authoritative: Evidence integrity, immutable history, auditability, lineage, transactional consistency, and module boundaries must not be weakened by disposal execution.
- Disposal execution must be a governed lifecycle after deletion eligibility, not a shortcut inside eligibility evaluation.
- Disposal must not introduce module-local authority systems, module-local migrations, direct storage shortcuts from bounded contexts, or application-startup migration behavior.

## Implementation Scope

### Disposal Aggregate Implementation

- Add a Retention-owned disposal aggregate representing the governed lifecycle for physical disposal after deletion eligibility.
- Proposed aggregate name: `DisposalRecord`.
- The aggregate must be Tenant-scoped, Workspace-scoped, Evidence-scoped, and linked to the deletion eligibility evaluation that made disposal requestable.
- Required state model:
  - `Requested`
  - `Approved`
  - `Queued`
  - `Executing`
  - `StorageDeleted`
  - `Reconciled`
  - `Completed`
  - `Denied`
  - `Failed`
  - `Cancelled`
- Required fields:
  - `Id`
  - `TenantId`
  - `WorkspaceId`
  - `EvidenceId`
  - `DeletionEligibilityEvaluationId`
  - `RequestedByProductActorId`
  - `RequestedAt`
  - `RequestReason`
  - `ApprovedByProductActorId`
  - `ApprovedAt`
  - `ApprovalReason`
  - `AuthorityVersion`
  - `AuthorityFreshnessExpiresAt`
  - `RetentionPolicyId`
  - `RetentionPolicyVersion`
  - `LegalHoldReleaseRequestId`
  - `EvidenceLineageId`
  - `EvidenceContentObjectKey`
  - `StorageContainer`
  - `ContentHash`
  - `State`
  - `AttemptCount`
  - `LastAttemptAt`
  - `LastFailureReason`
  - `StorageDeletedAt`
  - `ReconciledAt`
  - `CompletedAt`
  - `CorrelationId`
  - `CausationId`
  - `IdempotencyKey`
  - `RowVersion`
- The aggregate must be append-audited and must preserve enough evidence to prove what was deleted, why it was eligible, who approved it, which authority version permitted it, and which storage object was targeted.
- The aggregate must not store or expose original Evidence content after disposal.

### Command Processing

- Introduce explicit commands for the disposal lifecycle:
  - `RequestEvidenceDisposal`
  - `ApproveEvidenceDisposal`
  - `QueueEvidenceDisposal`
  - `ExecuteEvidenceDisposal`
  - `ReconcileEvidenceDisposal`
  - `CancelEvidenceDisposal`
- Commands must be idempotent by Tenant, Workspace, Evidence, command type, and idempotency key.
- Commands must reject stale deletion eligibility, stale authority, missing approval, conflicting Legal Hold state, retention-policy changes that invalidate eligibility, and any Tenant/Workspace mismatch.
- Command processing must occur inside explicit transactions for state changes, Audit records, Lineage records, and outbox messages.
- Failed mandatory Audit persistence must roll back consequential disposal state changes.
- Command handlers must emit domain events to the transactional outbox only after state and mandatory Audit persistence are part of the same transaction.

### Execution Workload

- Implement disposal execution as a background workload outside interactive API request processing.
- The workload must consume only governed disposal work items created from approved `DisposalRecord` state.
- The workload must run under a dedicated workload identity with least-privilege storage permissions.
- The workload must re-check Tenant, Workspace, Evidence, retention, Legal Hold, approval, and authority freshness immediately before storage deletion.
- The workload must use bounded batch size, cancellation support, retry policy, backoff, dead-letter behavior, and poison-message handling.
- The workload must never scan arbitrary storage containers looking for deletable content.
- The workload must not run in production until Production Authority and explicit Engineering Implementation Authority are granted.

### Storage Deletion Boundary

- Create an explicit storage deletion boundary owned by infrastructure/runtime integration, not by Evidence, Retention domain entities, API endpoints, React UI, or tests.
- The boundary must expose a narrow operation such as `DeleteEvidenceContentAsync(tenantId, workspaceId, evidenceId, objectKey, contentHash, disposalRecordId, cancellationToken)`.
- The boundary must verify the requested object key belongs to the Tenant/Workspace/Evidence scope before calling Azure Blob Storage deletion.
- The boundary must require exact object identity and expected content hash or immutable content version metadata before deletion.
- The boundary must not accept arbitrary container names, arbitrary blob paths, wildcard deletion, recursive deletion, or user-supplied storage connection strings.
- Soft-delete, versioning, immutability policy, and legal-hold behavior in Blob Storage must be reviewed by Security and Architecture before enabling production disposal.

### Reconciliation

- Add reconciliation to prove disposal state after storage operation completion.
- Reconciliation must verify:
  - the intended storage object no longer resolves as active content;
  - no active Legal Hold exists;
  - no current retention policy invalidates disposal;
  - Evidence metadata now reflects disposed content state without resurrecting content;
  - Audit and Lineage have the expected disposal events;
  - the outbox contains or published the required disposal event.
- Reconciliation must be repeatable and idempotent.
- Reconciliation failure must not recreate deleted content, clear Audit, or remove Lineage.
- Reconciliation must produce a deterministic failure reason and keep `DisposalRecord` recoverable for operator review.

### DisposalRecord Persistence

- Persist `DisposalRecord` in Runtime.Persistence-owned EF migrations under the approved Retention schema or another Architecture-approved schema.
- Enable and force PostgreSQL row-level security on the disposal table.
- Add Tenant/Workspace RLS policy and supporting indexes for:
  - Tenant/Workspace/Evidence lookup;
  - idempotency replay;
  - queue selection;
  - state reconciliation;
  - audit correlation.
- Add database constraints for valid states, required approval fields, positive attempt counts, required object identity, required authority version, and legal transition invariants where practical.
- Do not introduce module-local migrations.
- Do not introduce cross-module direct persistence shortcuts.
- Do not delete Evidence rows or Evidence versions as part of this aggregate unless a later Architecture decision explicitly authorizes metadata deletion.

### Audit Integration

- Product Audit must record every material disposal event, separate from application logs and telemetry.
- Required Audit events:
  - `Evidence.DisposalRequested`
  - `Evidence.DisposalRequestDenied`
  - `Evidence.DisposalApproved`
  - `Evidence.DisposalApprovalDenied`
  - `Evidence.DisposalQueued`
  - `Evidence.DisposalExecutionStarted`
  - `Evidence.DisposalStorageDeleted`
  - `Evidence.DisposalExecutionFailed`
  - `Evidence.DisposalReconciled`
  - `Evidence.DisposalCompleted`
  - `Evidence.DisposalCancelled`
  - `Evidence.DisposalAuthorityDenied`
  - `Evidence.DisposalStaleAuthorityDenied`
  - `Evidence.DisposalLegalHoldDenied`
  - `Evidence.DisposalRetentionDenied`
- Each Audit record must include actor or workload identity, Tenant, Workspace, Evidence, `DisposalRecordId`, authority version, policy/version context, correlation id, causation id, reason, timestamp, and outcome.
- Lineage must record disposal lifecycle events without erasing prior Evidence lineage.
- Telemetry may observe workload health, latency, and failures, but must not replace Audit.

## Security Implementation Controls

### Workload Identity

- Use a dedicated managed workload identity for disposal execution.
- The identity must not share credentials with API, web, migration, CI, support, admin, or AI execution workloads.
- The identity must be disabled by default outside authorized environments.
- Production use requires explicit Security acceptance and Production Authority.

### Least Privilege

- Grant only the exact storage permissions required to delete approved Evidence content objects.
- The identity must not have account-owner, container-owner, broad list/delete, key-management, or tenant-wide data access unless Security explicitly approves an equivalent least-privilege design.
- Command handlers must not carry storage delete permissions.
- Human Product roles must grant Product authority only; they must not grant cloud storage privileges.

### Tenant Isolation

- Disposal commands, queue work items, storage object keys, Audit, Lineage, outbox, and reconciliation must be Tenant-scoped.
- A hostile Tenant must not infer Evidence existence, disposal state, storage object identity, or deletion outcome for another Tenant.
- RLS and application-level checks must both reject cross-Tenant attempts.

### Workspace Isolation

- Disposal must require Workspace context and deny cross-Workspace command processing, queue execution, reconciliation, and Audit access.
- A valid Tenant actor in one Workspace must not manipulate or infer disposal state for another Workspace without explicit scoped authority.

### Legal Hold Checks

- Disposal request, approval, queueing, execution, and reconciliation must all re-check Legal Hold state.
- Any active Legal Hold must deny disposal.
- Legal Hold release must remain distinct from disposal approval.
- A Legal Hold created after approval but before execution must stop execution.

### Retention Checks

- Disposal must require a current deletion eligibility evaluation and must re-evaluate current retention policy state before queueing and before execution.
- Retention expiry alone must not authorize disposal.
- Policy changes that invalidate eligibility must deny or suspend disposal until re-approved under current policy.

### Approval Validation

- Disposal approval must require explicit Product permission, assignment, scope, SoD, and fresh authority.
- Disposal approval cannot be inherited from Tenant Owner, Workspace Owner, Support, Security, Repository, Platform, Commercial, Billing, or generic administrative authority.
- Self-approval must be denied where SoD requires independent approval.
- Approval replay with stale authority, revoked authority, altered scope, or expired freshness must be denied.

### Command Security

- Every command must validate Tenant, Workspace, actor or workload identity, scope, idempotency key, correlation id, reason, and expected current state.
- Commands must fail closed on contradictory authority, missing policy, missing Audit persistence, missing Evidence, stale eligibility, active hold, or ambiguous storage identity.
- API endpoints must call bounded application/runtime services and must not directly delete storage objects.
- React UI must not contain Product authority rules or disposal policy decisions.

### Retry Safety

- Retry behavior must be idempotent and state-aware.
- Retried storage deletion must treat already-deleted target content as a reconciliation case, not as authority bypass.
- Partial failures must preserve `DisposalRecord`, Audit trail, Lineage, and outbox state for deterministic recovery.
- Dead-lettered disposal work must require governed operator review and must not auto-delete after retry exhaustion.

## Validation Plan

### Hostile Tenant Tests

- Prove a Tenant A actor cannot request, approve, queue, execute, reconcile, or inspect disposal for Tenant B Evidence.
- Prove Tenant A cannot infer Tenant B Evidence existence from disposal errors, timings, Audit records, or queue state.
- Prove RLS denies cross-Tenant `DisposalRecord` access.

### Hostile Workspace Tests

- Prove a Workspace A actor cannot request, approve, queue, execute, reconcile, or inspect disposal for Workspace B Evidence.
- Prove cross-Workspace Evidence object keys are rejected by the storage deletion boundary.
- Prove Workspace isolation holds for Audit, Lineage, outbox, and reconciliation state.

### Approval Replay Tests

- Prove replayed approval commands with the same idempotency key return deterministic results.
- Prove replayed approval commands with changed actor, authority version, scope, reason, or disposal target are rejected.
- Prove an approval replay cannot approve a new or different `DisposalRecord`.

### Stale Authority Tests

- Prove expired authority freshness denies request, approval, queueing, and execution.
- Prove revoked assignments deny approval and execution.
- Prove changed SoD policy denies previously acceptable approval when freshness is no longer valid.
- Prove stale-authority denials are audited without mutating consequential disposal state.

### Legal Hold Race Tests

- Prove disposal is denied when an active hold exists before request.
- Prove disposal is denied when a hold is placed after eligibility but before approval.
- Prove disposal is denied when a hold is placed after approval but before execution.
- Prove disposal is denied or suspended when a hold is placed during execution before storage deletion.
- Prove no storage deletion occurs in every Legal Hold race denial path.

### Partial Failure Tests

- Prove failed Audit persistence rolls back request, approval, queue, and execution state changes where Audit is mandatory.
- Prove storage timeout after state transition does not mark disposal completed.
- Prove outbox persistence failure rolls back consequential state.
- Prove retry after transient storage failure does not create duplicate Audit, Lineage, or outbox events.

### Recovery Resurrection Tests

- Prove recovery workflows never recreate disposed Evidence content.
- Prove reconciliation does not restore blob content, clear disposal state, erase Audit, or rewrite Lineage.
- Prove retry of already-deleted content completes through reconciliation only when authority, policy, and object identity still match.
- Prove metadata remains disposed and cannot be used to retrieve deleted content.

### Audit Integrity Tests

- Prove every consequential disposal state transition writes mandatory Product Audit records.
- Prove Audit records include Tenant, Workspace, Evidence, disposal record, actor or workload identity, authority version, policy context, correlation, causation, reason, timestamp, and outcome.
- Prove Audit failures fail closed.
- Prove application logs and telemetry cannot substitute for Product Audit.

## Architecture and Security Conformance Tests

- Add architecture tests proving only the approved storage deletion boundary can call blob deletion APIs.
- Add tests proving Evidence, Retention domain entities, API endpoints, React UI, and module policies do not directly delete storage.
- Add tests proving no local disposal role taxonomy is introduced outside IdentityAccess.
- Add tests proving BuildingBlocks does not depend on Runtime.Persistence, Azure Blob Storage SDKs, Product Authority implementation, or module runtime services.
- Add tests proving AI components cannot trigger disposal commands or storage deletion.
- Add migration isolation tests proving all disposal schema changes remain Runtime.Persistence-owned.

## Authority Boundary

Engineering must not implement physical Evidence deletion until all of the following are true:

1. Product Authority is confirmed for governed physical disposal, including required permissions, scopes, SoD, role-assignment boundaries, approval requirements, and effective entitlement boundaries.
2. Security implementation conditions are accepted, including workload identity, least privilege, command security, storage boundary design, retry safety, tenant/workspace isolation, and audit integrity.
3. The Architecture ADR set remains valid, including ADR-014 and any future disposal-specific ADR.
4. Explicit Engineering Implementation Authority is issued for physical disposal execution.

Until those conditions are met:

- Do not add storage delete calls.
- Do not add purge jobs.
- Do not add disposal execution queues.
- Do not add production workload identity configuration.
- Do not add production storage permissions.
- Do not expose user-facing physical deletion controls.
- Do not alter Evidence metadata semantics to imply physical deletion has occurred.

## Requested Future Authority Decision

Architecture, Product, and Security are requested to confirm whether Engineering may proceed in a future increment with:

- `DisposalRecord` aggregate and persistence;
- disposal request and approval command processing;
- dedicated disposal execution workload;
- restricted storage deletion boundary;
- reconciliation workflow;
- mandatory Product Audit and Lineage integration;
- conformance and hostile isolation test suite;
- no production activation until separate Production Authority is granted.

## Result

IMPLEMENTATION AUTHORITY REQUEST PACKAGE PREPARED; PHYSICAL EVIDENCE DISPOSAL NOT IMPLEMENTED.
