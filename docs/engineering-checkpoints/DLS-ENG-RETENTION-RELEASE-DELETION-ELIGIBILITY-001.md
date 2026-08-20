# DLS-ENG-RETENTION-RELEASE-DELETION-ELIGIBILITY-001
# Retention Release and Deletion Eligibility Vertical Slice

## Repository

- Repository: `https://github.com/010Projects/DataLooMStudio`
- Baseline main SHA: `6614fd9032b7705de5c49b359349ff897f1d4171`
- Feature branch: `feature/dls-retention-release-deletion-eligibility-001`
- Implementation commit/head SHA: pending commit
- Pull request: pending publication
- Source authority: verified post-merge `main` after PR #5
- Not authorized: physical Evidence deletion, production purge jobs, production Evidence, customer onboarding, production deployment, Restricted Pilot Authority, Production Authority, AI execution, or production privileged-access infrastructure

## Implemented Scope

- Added Legal Hold release request lifecycle.
- Added authorised independent Legal Hold release approval.
- Added deterministic deletion-eligibility evaluation.
- Preserved active Legal Hold precedence over ordinary retention expiry.
- Preserved `LEGAL HOLD RELEASE != EVIDENCE DELETION`.
- Preserved `DELETION ELIGIBILITY != PHYSICAL DELETION`.
- Added Retention-owned policy records and deterministic policy decision code.
- Added Runtime.Persistence-owned EF migration for release and eligibility persistence.
- Added REST endpoints for release request, release approval, and deletion eligibility evaluation.
- Added architecture/security/isolation regression tests.

## Changed Files

- `docs/engineering-checkpoints/DLS-ENG-PRODUCT-AUTHORITY-INTEGRATION-002-MERGE-001.md`
- `docs/engineering-checkpoints/DLS-ENG-RETENTION-RELEASE-DELETION-ELIGIBILITY-001.md`
- `src/Api/DataLooMStudio.Api/Endpoints/RetentionEndpoints.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityActions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityPermissions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityRoleTaxonomy.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DeletionEligibilityEvaluation.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DeletionEligibilityPolicy.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DeletionEligibilityPolicyDecision.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DeletionEligibilityPolicyInput.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/DeletionEligibilityReasonCodes.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/LegalHold.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/LegalHoldReleaseRequest.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/LegalHoldReleaseStates.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260815144403_RetentionReleaseDeletionEligibility.Designer.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260815144403_RetentionReleaseDeletionEligibility.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/IRetentionGovernanceService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceCommands.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceResults.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceService.cs`
- `tests/DataLooMStudio.Architecture.Tests/FoundationArchitectureTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/RetentionGovernanceApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/RetentionGovernanceServiceTests.cs`

## Project Reference Changes

- `.csproj` project references added: none.
- `.csproj` project references removed or moved: none.
- Runtime.Persistence remains the only EF migration owner.
- No module-local migrations were introduced.
- No BuildingBlocks persistence/runtime coupling was introduced.
- No React Product authority logic was introduced.
- No AI dependency or execution path was introduced.

## Migration Changes

- Added migration: `20260815144403_RetentionReleaseDeletionEligibility`.
- Added table: `retention.legal_hold_release_requests`.
- Added table: `retention.deletion_eligibility_evaluations`.
- Added column: `retention.legal_holds.ReleaseReason`.
- Enabled and forced RLS on both new tables.
- Added tenant/workspace RLS policies on both new tables.
- Added retention release state and authority-version check constraints.
- Added deletion eligibility reason-code and authority-version check constraints.
- Updated IdentityAccess permission check constraints for:
  - `Governance.LegalHold.Release.Request`
  - `Governance.LegalHold.Release.Approve`
  - `Governance.Retention.DeletionEligibility.Evaluate`

## Domain Invariants Implemented

- Active Legal Hold prevents deletion eligibility.
- Retention expiry cannot override an active Legal Hold.
- Legal Hold release requires explicit authorised Product action.
- Legal Hold release does not delete Evidence.
- Deletion eligibility is persisted as a separately derived state record.
- Retention expiry plus no active hold plus valid authority/policy conditions may become deletion eligible.
- Eligibility evaluation preserves deterministic reason semantics.

## Authority Controls

- Canonical IdentityAccess Product Authority is used for all sensitive operations.
- Permissions remain the runtime authority contract.
- New permission keys:
  - `Governance.LegalHold.Release.Request`
  - `Governance.LegalHold.Release.Approve`
  - `Governance.Retention.DeletionEligibility.Evaluate`
- Tenant Owner and Workspace Owner receive no implicit retention, hold-release, or deletion authority.
- Support, Security, Repository, Platform, Commercial, and Billing authority do not confer release or deletion eligibility authority.
- Technical/administrative privilege alone is denied by executable tests.

## SoD Controls

- Release request and release approval are separate operations.
- Approval calls canonical Product Authority SoD evaluation.
- Self-approval is denied.
- SoD denial leaves Legal Hold, Evidence, and release request state unchanged.

## Tenant/Workspace Isolation Evidence

- RLS is enabled and forced on new retention tables.
- Cross-tenant Evidence release/evaluation attempts are denied.
- Cross-workspace Evidence release/evaluation attempts are denied.
- Denied cross-boundary attempts do not create release request or eligibility records.

## Legal Hold Release Lifecycle Evidence

- Active hold can receive a release request.
- Duplicate release request with the same idempotency key replays deterministically.
- Release approval requires explicit approval permission.
- Stale approval authority is denied.
- Revoked approval authority is denied.
- Successful approval records approved actor, reason, timestamp, authority version, and policy version.
- Successful approval marks the hold released and clears `EvidenceRecord.IsUnderLegalHold` only when no active hold remains.
- Successful approval writes Audit, Lineage, and outbox records in the same transaction.

## Deletion Eligibility Evidence

- Active Legal Hold returns `ActiveLegalHold` and `IsEligible = false`.
- Retention not expired returns `RetentionNotExpired` and `IsEligible = false`.
- Missing policy returns `RetentionPolicyMissing` and `IsEligible = false`.
- Lifecycle restriction returns `LifecycleRestricted` and `IsEligible = false`.
- Expired retention with no active hold returns `Eligible` and `IsEligible = true`.
- Duplicate evaluation with the same idempotency key replays deterministically.
- Evaluation writes Audit, Lineage, outbox, and a retention-owned evaluation record.

## Audit and Lineage Evidence

Audited material events include:

- `Retention.LegalHoldReleaseRequested`
- `Retention.LegalHoldReleaseApproved`
- `Retention.LegalHoldReleaseDenied`
- `Retention.DeletionEligibilityDetermined`
- `Retention.DeletionEligibilityDenied`
- Product Authority permit/deny events from IdentityAccess
- Product Authority SoD permit/deny events from IdentityAccess

Lineage events include:

- `LegalHoldReleased`
- `DeletionEligibilityDetermined`
- `DeletionEligibilityDenied`

## Test Inventory

- `Active_legal_hold_prevents_deletion_eligibility_even_after_retention_expiry`
- `Legal_hold_release_requires_independent_approval_and_does_not_delete_evidence`
- `Unauthorised_stale_and_revoked_authority_cannot_release_hold`
- `Cross_tenant_and_cross_workspace_context_cannot_release_or_evaluate`
- `Retention_expiry_without_active_hold_can_become_deletion_eligible_without_deleting_evidence`
- `Retention_not_expired_is_not_deletion_eligible`
- `Technical_or_administrative_privilege_alone_cannot_release_hold_or_evaluate_deletion`
- `Mandatory_audit_persistence_failure_rolls_back_legal_hold_release`
- `Api_releases_legal_hold_and_evaluates_deletion_eligibility_without_physical_deletion`
- `Retention_release_and_deletion_eligibility_must_not_enable_physical_deletion`

## Validation Results

| Check | Result |
| --- | --- |
| `dotnet build DataLooMStudio.slnx --configuration Release` | PASS - 0 warnings, 0 errors |
| `dotnet test tests/DataLooMStudio.Persistence.Tests/DataLooMStudio.Persistence.Tests.csproj --configuration Release --no-build --filter RetentionGovernanceServiceTests` | PASS - 12 tests |
| `dotnet test tests/DataLooMStudio.Persistence.Tests/DataLooMStudio.Persistence.Tests.csproj --configuration Release --no-build --filter RetentionGovernance` | PASS - 14 tests |
| `dotnet test tests/DataLooMStudio.Architecture.Tests/DataLooMStudio.Architecture.Tests.csproj --configuration Release` | PASS - 41 tests |
| `dotnet restore DataLooMStudio.slnx` | PASS |
| `dotnet build DataLooMStudio.slnx --configuration Release --no-restore` | PASS - 0 warnings, 0 errors |
| `dotnet test DataLooMStudio.slnx --configuration Release --no-build` | PASS - 148 tests |
| `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal` | PASS |
| `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive` | PASS - no vulnerable NuGet packages reported |
| `npm install` in `src/Web/DataLooMStudio.Web` | PASS - 0 vulnerabilities |
| `npm run build` in `src/Web/DataLooMStudio.Web` | PASS |
| `npm audit --audit-level=high` in `src/Web/DataLooMStudio.Web` | PASS - 0 vulnerabilities |
| `az bicep build --file infra/main.bicep` | PASS |
| Local secret-pattern scan | PASS - no configured secret patterns found |
| `git diff --check` | PASS - line-ending warnings only |
| Latest-head CI | pending PR publication |

## Physical Deletion Statement

Physical Evidence deletion was not implemented.

No delete endpoint, purge job, background deletion executor, blob delete operation, Evidence row deletion, Evidence version deletion, or production purge authority was added.

## Unresolved Risks

- Physical deletion execution remains unauthorised and requires a future Architecture/Product/Security decision before Engineering implementation.
- Production identity activation, Entra/External ID production mapping, PIM, break-glass operations, production privileged access, Production Evidence, Restricted Pilot Authority, Production Authority, production deployment, customer onboarding, and AI execution remain outside this slice.
- Retention work queues, reviewer UI, and operational reporting remain future Product slices.

## Recommendation

Proceed with normal PR governance for this bounded release/eligibility slice after full validation and latest-head CI pass. Do not merge without normal repository review and CI requirements.

## Exact Next Specialist Workspace

`DataLooM Studio - Architecture Office / Codex - DLS-ARCH-RETENTION-DELETION-EXECUTION-001`

## Result

# LEGAL HOLD RELEASE AND DELETION ELIGIBILITY IMPLEMENTED; PHYSICAL DELETION NOT IMPLEMENTED
