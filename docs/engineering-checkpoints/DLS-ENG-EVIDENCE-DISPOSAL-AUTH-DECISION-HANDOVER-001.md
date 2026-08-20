# DLS-ENG-EVIDENCE-DISPOSAL-AUTH-DECISION-HANDOVER-001
# Formal Engineering Implementation Authority Request Preparation

## Receiving Office

DataLooM Studio - MVP Delivery and Engineering Office

## Source Review

- Source Security review: `DLS-SEC-EVIDENCE-DISPOSAL-IMPLEMENTATION-AUTH-REVIEW-001`
- Prior Engineering preparation artifact: `DLS-ENG-EVIDENCE-DISPOSAL-IMPLEMENTATION-AUTH-HANDOVER-001`
- Product Authority baseline: `DLS-PROD-AUTH-001`
- Architecture boundary: `ADR-014 Evidence Consistency Boundary`
- Active Architecture conditions: `ARCH-ERD-001` through `ARCH-ERD-005`

## Purpose

Proceed with formal Engineering Implementation Authority request preparation after Security acceptance.

This package is a request-preparation artifact only. It does not implement physical Evidence deletion and does not grant destructive execution authority.

## 1. Implementation Authority Request

### Requested Future Implementation Scope

Engineering requests future authority to implement governed physical Evidence disposal as a bounded post-eligibility lifecycle with the following exact scope:

1. Add a Retention-owned `DisposalRecord` aggregate for requested, approved, queued, executing, storage-deleted, reconciled, completed, denied, failed, and cancelled disposal states.
2. Add command processing for disposal request, approval, queueing, execution handoff, reconciliation, failure recording, and cancellation.
3. Add a dedicated worker execution path that processes only approved and queued disposal records.
4. Add a narrow storage deletion boundary that can delete only the exact Evidence content object referenced by an approved disposal record.
5. Add reconciliation that proves storage deletion state without resurrecting content or erasing Audit/Lineage.
6. Add Runtime.Persistence-owned migrations for disposal persistence, RLS, constraints, idempotency indexes, queue selection indexes, and audit correlation indexes.
7. Add mandatory Product Audit, Lineage, and outbox events around every consequential disposal state transition.
8. Add architecture, security, isolation, race-condition, replay, stale-authority, and partial-failure tests.

The requested scope excludes production activation and excludes any uncontrolled or automated destruction outside the governed command lifecycle.

### Files and Modules Expected To Be Affected

Proposed new files:

- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalRecord.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalRecordStates.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalReasonCodes.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalPolicy.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalPolicyDecision.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DisposalPolicyInput.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/IEvidenceDisposalGovernanceService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/EvidenceDisposalCommands.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/EvidenceDisposalResults.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/EvidenceDisposalGovernanceService.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Disposal/EvidenceDisposalWorker.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Disposal/EvidenceDisposalWorkItemProcessor.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/IEvidenceDisposalObjectStore.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/AzureEvidenceDisposalObjectStore.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/DevelopmentEvidenceDisposalObjectStore.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceDisposalGovernanceServiceTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceDisposalWorkerTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceDisposalApiTests.cs`

Proposed modified files:

- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityPermissions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityActions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityCapabilities.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityRoleTaxonomy.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductWorkloadIdentityMatrix.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/RetentionModule.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/DependencyInjection/DataLooMPersistenceServiceCollectionExtensions.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Program.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/DependencyInjection/DataLooMInfrastructureServiceCollectionExtensions.cs`
- `src/Api/DataLooMStudio.Api/Endpoints/RetentionEndpoints.cs`
- `tests/DataLooMStudio.Architecture.Tests/FoundationArchitectureTests.cs`
- `.github/workflows/ci.yml`

Files explicitly not expected to own disposal policy or destructive execution:

- `src/Web/DataLooMStudio.Web/src/App.tsx`
- `src/Web/DataLooMStudio.Web/src/api/*`
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/*`
- `src/Modules/AiGovernance/DataLooMStudio.Modules.AiGovernance/*`
- `src/BuildingBlocks/DataLooMStudio.SharedKernel/*`

### Storage Boundaries

- Only `IEvidenceDisposalObjectStore` and its approved implementations may call Azure Blob delete APIs.
- API endpoints must never call storage deletion directly.
- Retention domain entities must never call storage deletion directly.
- Evidence module entities and services must never call storage deletion directly.
- React UI must never contain disposal policy or storage deletion behavior.
- The disposal storage boundary must require Tenant id, Workspace id, Evidence id, disposal record id, exact object key, and expected content hash or immutable blob version metadata.
- The boundary must reject arbitrary container names, arbitrary blob paths, wildcard deletion, recursive deletion, user-supplied connection strings, and cross-Tenant or cross-Workspace object keys.
- The boundary must be registered for the Worker execution path only unless Architecture and Security explicitly approve another runtime.

### Workload Identity Design

- Use a dedicated managed workload identity named for disposal execution only.
- The disposal workload identity must be separate from API, migration, CI, support, admin, repository, platform, and AI identities.
- The identity must be disabled or unassigned by default in non-authorized environments.
- The identity must receive only the minimum data-plane permission required to delete approved Evidence content objects in the approved storage scope.
- Product actors approve disposal through IdentityAccess Product Authority; human Product roles must not receive cloud storage permissions.
- Workload execution must verify that the queued work item is already approved, current, scoped, and fresh before using the workload identity to call storage.
- Production assignment of this identity is out of scope until Production Authority is granted.

### Migration Requirements

- All disposal persistence must be implemented through Runtime.Persistence-owned EF Core migrations.
- No module-local migrations may be introduced.
- Required table: `retention.disposal_records`, unless Architecture approves a different disposal schema.
- Required RLS: enable and force row-level security on all disposal tables.
- Required policy: Tenant/Workspace context isolation using transaction-scoped PostgreSQL context.
- Required indexes:
  - Tenant/Workspace/Evidence lookup;
  - Tenant/Workspace/idempotency lookup;
  - queue selection by state and next-attempt timestamp;
  - reconciliation by state;
  - audit correlation by correlation and causation ids.
- Required constraints:
  - valid disposal states;
  - required approval fields before queueing;
  - required storage object identity before execution;
  - non-negative attempt counts;
  - required authority version;
  - immutable Tenant, Workspace, Evidence, and eligibility linkage.
- Existing Evidence rows and Evidence versions must not be physically deleted by the migration or by metadata cleanup.

### Rollback Strategy

- Rollback before execution authority: remove disposal feature branch changes, remove generated migration, and leave existing retention/legal-hold/deletion-eligibility behavior unchanged.
- Rollback after migration but before any storage deletion: disable disposal worker registration, disable queue processing, migrate schema back only if no disposal records exist, or retain inert records for audit continuity.
- Rollback after queued but not executed records: set processing off, preserve `DisposalRecord`, Audit, Lineage, and outbox state, and require governed operator review before requeue or cancellation.
- Rollback after partial storage failure: do not mark completed; keep failed state and reconciliation evidence; retry only through idempotent worker processing after authority and policy re-check.
- Rollback after storage deletion: content resurrection is not a rollback strategy. Preserve Audit, Lineage, `DisposalRecord`, and metadata disposed state; remediate only through formal incident and recovery governance.
- Any rollback must fail closed if Audit integrity cannot be preserved.

## 2. Security Control Mapping

| Product requirement | Architecture decision | Security condition | Implementation control | Automated validation | Evidence |
| --- | --- | --- | --- | --- | --- |
| Disposal requires explicit Product authority | `DLS-PROD-AUTH-001`; `ARCH-ERD-003` | Product roles do not equal endpoint authority | New disposal permissions evaluated through IdentityAccess permission, assignment, scope, SoD, and freshness | Permission denial, stale authority, revoked authority, and SoD tests | Product Authority audit records and denied-action tests |
| Tenant Owner and Workspace Owner have no implicit disposal authority | `DLS-PROD-AUTH-001` | Administrative ownership must not imply destructive Product authority | Role taxonomy excludes disposal permissions from owner/admin bundles | Architecture role-bundle tests | Conformance test output |
| Legal Hold blocks disposal | ADR-014; Retention owns Legal Hold decisions | Active hold must deny destructive execution | Re-check Legal Hold at request, approval, queueing, execution, and reconciliation | Legal Hold race tests | Audit events `Evidence.DisposalLegalHoldDenied` |
| Retention expiry alone does not authorize disposal | Retention owns policy; eligibility is separate from deletion | Current retention policy must remain valid | Require current deletion eligibility and re-evaluate retention before queue and execution | Retention policy change and expiry-only denial tests | Disposal policy decision records |
| Disposal approval must be independent where SoD applies | `DLS-PROD-AUTH-001`; `ARCH-ERD-003` | Self-approval and contradictory authority deny | SoD evaluation before approval and before execution handoff | Self-approval and SoD conflict tests | Product Authority SoD audit |
| Storage deletion must be narrowly bounded | ADR-014 storage/content-hash boundary | No broad storage deletion or arbitrary paths | `IEvidenceDisposalObjectStore` accepts exact scoped object identity only | Architecture tests scan for Blob delete calls outside approved boundary | Architecture test output and code review |
| Execution uses workload identity | Module boundaries distinguish API, Worker, Migration runtime | Dedicated least-privilege identity required | Worker-only registration and managed identity configuration | Configuration tests and secret scan | IaC diff and CI scan results |
| Tenant isolation is mandatory | Module boundaries; PostgreSQL RLS authoritative | Hostile Tenant must fail closed | RLS on disposal records plus scoped command checks | Cross-Tenant request, approval, execution, reconciliation, and query tests | Persistence test output |
| Workspace isolation is mandatory | Module boundaries; workspace scope is immutable input | Hostile Workspace must fail closed | Workspace-scoped disposal records and storage object key validation | Cross-Workspace command and storage-boundary tests | Persistence test output |
| Audit is mandatory and separate from logs | ADR-014; Product Audit boundary | Audit failure must prevent consequential mutation | Transactional Audit plus state change plus outbox persistence | Audit persistence failure rollback tests | Audit rows and transaction assertions |
| Retry must be safe | Transactional outbox and worker separation | Retries must not bypass authority or duplicate events | Idempotency keys, state machine checks, attempt tracking, backoff, dead-letter | Replay, duplicate command, partial failure, poison-item tests | DisposalRecord attempt history |
| AI must not dispose Evidence | AI boundary; `ARCH-ERD-005` | AI execution remains unauthorized | No AiGovernance disposal command or storage dependency | Architecture tests proving no AI-to-disposal path | Architecture conformance test output |

## 3. Explicit Non-Scope Confirmation

The following are explicitly excluded from this authority preparation package:

- Production deletion.
- Customer Evidence deletion.
- AI disposal.
- Automated destruction outside governed request, approval, execution, and reconciliation.
- Irreversibility claims in UI, API, Audit, or documentation before Security and Architecture approve exact storage behavior.
- Production Evidence processing.
- General Availability.
- Production Authority.
- Restricted Pilot Authority.
- Customer onboarding.
- Background purge jobs that run without explicit approved `DisposalRecord` state.
- Any user-facing delete button or destructive UI control.
- Any physical deletion from Evidence eligibility evaluation.

## 4. Required Approval Chain

Engineering must obtain all of the following before destructive code is written:

1. Product Office confirmation that governed physical disposal is a Product capability, including permission names, approval scope, role-assignment boundaries, SoD baseline, and entitlement boundary.
2. Architecture Office confirmation that the disposal lifecycle, storage deletion boundary, migration ownership, Audit/Lineage behavior, ADR-014 interpretation, and module dependencies remain valid.
3. Security implementation authority approval for workload identity, least privilege, storage boundary design, tenant/workspace isolation, command security, retry safety, Audit integrity, and non-production activation controls.
4. Engineering execution authority naming the branch, implementation scope, validation floor, CI requirements, and PR governance route.

Until all four approvals are explicitly present, Engineering must not write destructive code or introduce storage deletion execution paths.

## Authority Boundary

This Security decision does not grant:

- physical Evidence deletion;
- destructive execution;
- Production Evidence processing;
- Production Authority;
- General Availability.

It also does not grant AI execution, production purge jobs, customer onboarding, or production deployment.

## Requested Decision

Return one:

- `APPROVED FOR ENGINEERING IMPLEMENTATION AUTHORITY`
- `APPROVED WITH CONDITIONS`
- `RETURNED FOR REVISION`

Required decision evidence:

- approved Product permissions and scopes;
- approved Architecture disposal lifecycle and storage boundary;
- accepted Security implementation controls;
- exact Engineering execution authority;
- explicit confirmation that production activation remains out of scope.

## Result

FORMAL ENGINEERING IMPLEMENTATION AUTHORITY REQUEST PREPARED; DESTRUCTIVE CODE NOT WRITTEN.
