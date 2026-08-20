# DLS-ENG-EVIDENCE-DISPOSAL-IMPLEMENTATION-001
# Governed Evidence Disposal Bounded Implementation Package

## Authority Decision

Engineering Execution Authority is granted only for bounded implementation and test of the governed Evidence disposal control plane and disabled non-production execution architecture.

Physical Evidence deletion execution remains not authorized. Production Evidence deletion remains not authorized. Production Authority, Restricted Pilot Authority, customer onboarding, AI disposal, production deployment, and General Availability remain not granted.

The implementation deliberately preserves a disabled storage adapter as the default disposal boundary. No production/customer Evidence can be physically deleted by this package.

## Changed File Register

New implementation files:

- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/IEvidenceDisposalObjectStore.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/DisabledEvidenceDisposalObjectStore.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Disposal/EvidenceDisposalWorkItem.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Disposal/EvidenceDisposalWorkItemProcessor.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalPolicy.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalPolicyDecision.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalPolicyInput.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalReasonCodes.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalRecord.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalRecordStates.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260817172615_EvidenceDisposalControlPlane.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260817172615_EvidenceDisposalControlPlane.Designer.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceService.Disposal.cs`
- `docs/engineering-checkpoints/DLS-ENG-EVIDENCE-DISPOSAL-IMPLEMENTATION-001.md`

Modified implementation files:

- `src/Api/DataLooMStudio.Api/Endpoints/RetentionEndpoints.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/DependencyInjection/DataLooMInfrastructureServiceCollectionExtensions.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Program.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityActions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityCapabilities.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityPermissions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityResourceTypes.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductWorkloadIdentityMatrix.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/RetentionModule.cs`
- `src/Modules/Retention/module.manifest.json`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/IRetentionGovernanceService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceCommands.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceResults.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceService.cs`
- `tests/DataLooMStudio.Architecture.Tests/FoundationArchitectureTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/RetentionGovernanceApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/RetentionGovernanceServiceTests.cs`

Pre-existing worktree changes observed and preserved without reversal:

- Deleted: `docs/engineering-checkpoints/DLS-ENG-PRODUCT-AUTHORITY-INTEGRATION-002-MERGE-001.md`
- Deleted: `docs/engineering-checkpoints/DLS-ENG-RETENTION-RELEASE-DELETION-ELIGIBILITY-001.md`
- Untracked: `docs/engineering-checkpoints/DLS-ENG-EVIDENCE-DISPOSAL-AUTH-DECISION-HANDOVER-001.md`
- Untracked: `docs/engineering-checkpoints/DLS-ENG-EVIDENCE-DISPOSAL-IMPLEMENTATION-AUTH-HANDOVER-001.md`

## Domain Implementation

Retention now owns `DisposalRecord` and disposal policy objects. The state model supports requested, approved, queued, executing, storage-disposed, reconciled, completed, denied, failed, suspended, and cancelled states.

The disposal policy is downstream of deletion eligibility and re-checks current Evidence state, retention expiry, lifecycle state, Legal Hold state, and disposal authority before advancing consequential state.

`DataLooMDbContext` persists disposal records under the Retention schema and rejects deletion or mutation of immutable disposal identity fields through SaveChanges guards.

## Migration Details

Migration `20260817172615_EvidenceDisposalControlPlane` adds `retention.disposal_records` with Tenant and Workspace scope, state/idempotency indexes, authority metadata, storage object identity, audit/reconciliation fields, and concurrency token support.

The migration enables and forces PostgreSQL RLS on `retention.disposal_records` with the same transaction-scoped Tenant/Workspace context policy used by the rest of Runtime.Persistence.

Database constraints enforce valid disposal states, non-negative attempts, positive authority versions, required approval/queue fields by state, and `EvidencePhysicallyDeleted = false`.

IdentityAccess permission and resource-scope constraints were extended for:

- `Evidence.Disposal.Request`
- `Evidence.Disposal.Approve`
- `Evidence.Disposal.Queue`
- `Workload.EvidenceDisposal.Execute`
- `Workload.EvidenceDisposal.Reconcile`
- `EvidenceDisposal` resource type

## Authority Integration

IdentityAccess remains the canonical authority boundary. Disposal uses permissions as the runtime authority contract and does not add a local role system in Retention, Evidence, Review, Decision, API, Worker, or React.

No Product role bundle grants disposal authority. Disposal authority is explicit assignment-first.

The `evidence-disposal` workload profile may execute and reconcile disposal only. It is explicitly prohibited from request, approval, queueing, Evidence read, review/decision authority, review assignment management, and break-glass activation.

Tenant Owner, Workspace Owner, Commercial, Billing, Support, Security, Repository, and Platform authority continue to receive no implicit Evidence, Review, Decision, or Disposal authority.

## Workload Design

`EvidenceDisposalWorkItemProcessor` is a worker-side processor that accepts an explicit scoped work item containing Tenant, Workspace, Evidence, DisposalRecord, workload subject, correlation id, and idempotency key.

The worker sets request context before calling Retention governance execution. The existing hosted worker remains inert; no automatic queue polling or production purge loop was introduced.

No API endpoint executes disposal. The API exposes only request, approval, and queueing control-plane routes.

## Storage Abstraction

`IEvidenceDisposalObjectStore` is the provider-neutral disposal storage boundary.

`DisabledEvidenceDisposalObjectStore` is the registered default implementation. It always returns suspended/non-confirmed results and always reports `EvidencePhysicallyDeleted = false`. It contains no Azure Blob SDK usage and no delete calls.

No production storage delete credentials, production delete adapter, destructive break-glass path, or destructive administrator bypass was added.

## Command Integrity

Request, approval, queue, execution, and reconciliation commands use idempotency keys and request hashes. Changed command content with a reused idempotency key is rejected.

Approval and queueing capture authority metadata. Execution and reconciliation require workload authority and re-check scoped command identity. Command expiry is enforced for approval-to-queue and queue-to-execution windows.

Command replay, queue scope substitution, partial storage failure, and idempotent retry paths are covered by tests.

## Legal Hold and Retention Enforcement

Disposal is denied or suspended when Legal Hold is active before execution, after approval, or while queued.

Retention changes after approval deny further progress. Deletion eligibility remains separate from physical disposal and does not trigger automatic destruction.

## Isolation Evidence

Tenant and Workspace scope is enforced by:

- Runtime request context validation;
- Product Authority tenant/workspace membership checks;
- disposal record Tenant/Workspace fields;
- PostgreSQL RLS on `retention.disposal_records`;
- command-level Tenant/Workspace/Evidence matching;
- worker work-item scoped context.

Hostile Tenant and hostile Workspace execution paths are covered in persistence tests.

## Audit and Lineage Implementation

The implementation writes Product Audit for disposal request, approval, queue, execution outcomes, suspension/failure, and reconciliation.

Lineage is recorded for request, approval, and reconciliation lifecycle points without erasing prior Evidence lineage.

Outbox messages are emitted transactionally for request, approval, queue, and reconciliation events. Audit durability failure rolls back consequential disposal request state.

## Reconciliation

Reconciliation calls the disabled/synthetic storage boundary, records confirmation or resurrection detection, preserves `EvidencePhysicallyDeleted = false`, and does not restore or recreate Evidence content.

Resurrection detection is represented as a reconciliation outcome and retained in disposal state for governed follow-up.

## Validation Results

Local validation completed:

- `dotnet build DataLooMStudio.slnx --configuration Release --no-restore` - passed, 0 warnings, 0 errors.
- `dotnet test DataLooMStudio.slnx --configuration Release --no-build --logger "console;verbosity=minimal"` - passed.
  - `DataLooMStudio.Api.Tests`: 4 passed.
  - `DataLooMStudio.Architecture.Tests`: 44 passed.
  - `DataLooMStudio.Persistence.Tests`: 113 passed.
- `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal` - passed after automated formatting.
- `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive` - no vulnerable NuGet packages reported.
- Secret-pattern scan with `rg` for storage keys, SAS, private keys, client secrets, and password literals - no matches.
- `npm ci` in `src/Web/DataLooMStudio.Web` - passed, 0 vulnerabilities reported by install audit.
- `npm run build` in `src/Web/DataLooMStudio.Web` - passed.
- `npm audit --audit-level=high` in `src/Web/DataLooMStudio.Web` - 0 vulnerabilities.
- `az bicep build --file infra/main.bicep` - passed; no deployment performed.
- `git diff --check` - passed with line-ending normalization warnings only.

CI evidence:

- The local validation mirrors the configured GitHub Actions workflow steps in `.github/workflows/ci.yml`.
- GitHub-hosted CI has not run for these uncommitted local changes. CI must run after branch push/PR update.

## Security Validation Coverage

Implemented automated coverage includes:

- Request / Approve / Execute SoD.
- Self-approval denial.
- Stale approval denial.
- Revoked authority denial.
- Cross-Tenant destructive denial.
- Cross-Workspace destructive denial.
- Legal Hold placed before execution.
- Legal Hold placed after approval.
- Legal Hold placed while queued.
- Retention change after approval.
- Command replay.
- Command expiry.
- Queue scope substitution.
- Partial storage failure.
- Idempotent retry.
- Audit durability rollback.
- DisposalRecord immutability.
- Kill/suspension enforcement through disabled store.
- Restore resurrection reconciliation.
- API request/approve/queue path and no public execute endpoint.
- Runtime boundaries.
- Module dependencies.
- BuildingBlocks restrictions.
- Migration isolation.
- AI boundary enforcement.
- React/Product separation.

## Residual Engineering Risks

- Physical deletion adapter is intentionally absent. Future implementation requires separate Product, Architecture, Security, and Engineering authority.
- Worker polling and Service Bus dequeue integration remain intentionally inert. Future queue activation requires explicit non-production execution authority and must remain disabled for production/customer Evidence.
- Blob soft-delete, versioning, immutability, storage Legal Hold behavior, and least-privilege Azure RBAC assignment require Security and Architecture verification before any real adapter exists.
- GitHub-hosted CI evidence is pending until changes are committed, pushed, and reviewed through repository governance.
- Existing pre-task worktree deletions under `docs/engineering-checkpoints` remain unresolved and were not altered by this implementation.

## Operational Dependencies

- PostgreSQL RLS transaction context must be set for disposal commands and worker execution.
- Product Authority assignments for disposal permissions must be explicit and scoped.
- The `evidence-disposal` workload identity must remain non-production, least-privilege, and disabled from production storage until future authority.
- Service Bus queue binding, dead-letter handling, operational suspension controls, and reconciliation scheduling require a future bounded execution increment.
- Production deployment, production credentials, production purge scheduling, and customer onboarding remain out of scope.

## Return Package

Return this package to DataLooM Studio - Security Office for `DLS-SEC-EVIDENCE-DISPOSAL-VERIFY-001`.

Copy to:

- DataLooM Studio - Architecture Office;
- DataLooM Studio - Product Office;
- DataLooM Studio - Programme Management Office.

## Result

BOUNDED GOVERNED EVIDENCE DISPOSAL CONTROL PLANE IMPLEMENTED AND VALIDATED.

PHYSICAL EVIDENCE DELETION EXECUTION NOT AUTHORISED AND NOT ENABLED.
