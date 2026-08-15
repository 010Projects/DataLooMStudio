# DLS-ENG-PRODUCT-AUTHORITY-INTEGRATION-002
# Canonical Role Taxonomy Integration and Product Feature Continuation

## Repository

- Repository: `https://github.com/010Projects/DataLooMStudio`
- Branch: `feature/dls-product-authority-integration-002`
- Source authority: merged `main` after PR #4, merge commit `57d92ed7a98bd52c8079a24b60ba3df049fc1c7c`
- Product decision registered: `DLS-PROD-AUTH-001`
- Implementation authority: bounded Engineering implementation only
- Not authorized: production identity activation, Production Evidence, production deployment, customer onboarding, AI execution, Restricted Pilot Authority, or Production Authority

## Scope Applied

- Registered `DLS-PROD-AUTH-001` as the canonical Product Authority taxonomy decision.
- Reconciled IdentityAccess authority metadata with the approved sixteen-role Product taxonomy.
- Preserved permissions as the stable runtime authority contract.
- Separated Product business roles from privileged technical and operational authority classes.
- Enforced that Tenant Owner and Workspace Owner have no implicit Evidence, Review, or Decision authority.
- Enforced that Commercial, Billing, Support, Security, Repository, and Platform authority classes have no implicit Product content, Review, or Decision authority.
- Preserved Effective Entitlements as capability-availability authority only.
- Preserved assignment, scope, separation-of-duties, and authority-freshness evaluation inside Product Authority.
- Added the next executable Product capability slice: retention policy definition and legal hold placement through canonical Product Authority.

## Authority-Model Reconciliation

- Canonical Product role names now live in `IdentityAccess` under `ProductAuthorityRoleNames`.
- Canonical Product role definitions now live in `ProductAuthorityRoleTaxonomy`.
- `ProductAuthorityPolicy` rejects non-canonical `ProductRole` metadata labels.
- Runtime permit/deny remains based on explicit `ProductAuthorityPermissions`, not role names.
- Role names are permission-bundle metadata and do not authorize sensitive product mutation by themselves.
- Evidence, Review, Decision, Retention, and Legal Hold authorization continues to require explicit permission, resource scope, assignment context where applicable, separation-of-duties validation, and fresh authority.
- No local canonical role system was introduced in Evidence, Review, Decision, Retention, API, React, or runtime infrastructure boundaries.

## Role/Class Separation

| Class | Roles | Authority Constraint |
| --- | --- | --- |
| Product business | Tenant Owner, Workspace Owner, Evidence Contributor, Evidence Reader, Reviewer, Decision Approver, Governance Administrator, Retention Administrator, Legal Hold Administrator, Auditor | Product-facing taxonomy labels and permission bundles only. Sensitive actions still require explicit permission and policy evaluation. |
| Privileged operational | Commercial Administrator, Billing Administrator, Support Operator, Security Operator | Operational/admin capability only; no implicit Evidence content, Review, or Decision authority. |
| Privileged technical | Repository Administrator, Platform Administrator | Technical/platform capability only; no implicit Product content, Review, or Decision authority. |

## Permission Catalogue

The runtime authority contract remains the Product permission catalogue:

- `Evidence.Content.Create`
- `Evidence.Content.Read`
- `Evidence.Content.Update`
- `Evidence.Content.Delete`
- `Evidence.Review.Request`
- `Evidence.Review.Assign`
- `Evidence.Review.Perform`
- `Evidence.Decision.CreateCandidate`
- `Evidence.Decision.Apply`
- `Governance.Audit.Read`
- `Governance.Retention.Manage`
- `Governance.LegalHold.Manage`
- `Commercial.Entitlements.Manage`
- `Commercial.Billing.Manage`
- `Support.Elevation.Use`
- `Repository.Admin`
- `Platform.Admin`

## Product Slice Continued

Implemented Retention Governance:

- `POST /api/v1/workspaces/{workspaceId}/retention-policies`
- `POST /api/v1/workspaces/{workspaceId}/evidence/{evidenceId}/legal-holds`

The slice:

- Uses `IRetentionGovernanceService` from runtime persistence.
- Requires explicit `ManageRetentionPolicy` or `ManageLegalHold` Product Authority permission.
- Uses canonical Product role metadata only as context labels.
- Preserves tenant/workspace row-level-security scope.
- Adds idempotency keys, request hashes, and concurrency tokens.
- Writes audit records, lineage records, and transactional outbox events inside the same product transaction.
- Marks Evidence under legal hold without enabling deletion, production Evidence, production deployment, or AI.

## Changed Files

- `docs/engineering-checkpoints/DLS-ENG-PRODUCT-AUTHORITY-INTEGRATION-002.md`
- `governance/architecture-conditions/ARCH-ERD-001-through-005.md`
- `governance/product-authority/DLS-PROD-AUTH-001.md`
- `src/Api/DataLooMStudio.Api/Endpoints/RetentionEndpoints.cs`
- `src/Api/DataLooMStudio.Api/Program.cs`
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/EvidenceRecord.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityActions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityPermissions.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityPolicy.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityRoleClasses.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityRoleDefinition.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityRoleNames.cs`
- `src/Modules/IdentityAccess/DataLooMStudio.Modules.IdentityAccess/ProductAuthorityRoleTaxonomy.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/LegalHold.cs`
- `src/Modules/Retention/DataLooMStudio.Modules.Retention/RetentionPolicy.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/DependencyInjection/DataLooMPersistenceServiceCollectionExtensions.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260815132107_RetentionGovernanceIdempotency.Designer.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260815132107_RetentionGovernanceIdempotency.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/IRetentionGovernanceService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceCommands.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceResults.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceValidationException.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceConflictException.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceForbiddenException.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Retention/RetentionGovernanceService.cs`
- `tests/DataLooMStudio.Architecture.Tests/FoundationArchitectureTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/IdentityAccessSecurityTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/RetentionGovernanceApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/RetentionGovernanceServiceTests.cs`

## Migration and Project Reference Evidence

- Migration added: `20260815132107_RetentionGovernanceIdempotency`
- Migration owner: existing `Runtime.Persistence` migration boundary.
- Schemas changed: existing `governance` and `evidence` persistence mappings only.
- Module-local migrations added: none.
- `.csproj` project references added: none.
- `.csproj` project references removed or moved: none.
- Runtime-to-module reference pattern: unchanged.
- API-to-module reference pattern: unchanged; API maps requests to runtime service contracts.
- Persistence coupling: unchanged beyond existing approved runtime persistence ownership.
- BuildingBlocks restrictions: unchanged; no EF Core, Npgsql, runtime, API, or module dependency was introduced into BuildingBlocks.
- React/Product separation: unchanged; no Product Authority rules were introduced into React.
- AI boundary: unchanged; no model, prompt, agent, provider, or AI execution dependency was introduced.

## Architecture and Security Tests Added

- `Product_authority_taxonomy_must_match_canonical_product_decision`
- `Product_authority_role_classes_must_not_imply_content_review_or_decision_authority`
- `Product_role_taxonomy_must_not_be_reintroduced_inside_evidence_or_api_boundaries`
- `Canonical_owner_admin_and_technical_roles_do_not_grant_review_or_decision_authority`
- `Legacy_local_role_labels_are_not_canonical_product_authority_metadata`
- `Retention_policy_requires_explicit_permission_not_role_label`
- `Retention_policy_definition_is_authorized_idempotent_audited_and_outboxed`
- `Legal_hold_requires_explicit_permission_and_records_evidence_lineage`
- `Legal_hold_cannot_cross_tenant_or_workspace_boundary`
- `Api_defines_retention_policy_through_product_authority_boundary`

## Isolation Regression Results

| Invariant | Result |
| --- | --- |
| Runtime boundaries | PASS |
| Module dependencies | PASS |
| BuildingBlocks restrictions | PASS |
| Migration isolation | PASS |
| AI boundary enforcement | PASS |
| React/Product separation | PASS |
| Product role taxonomy does not grant runtime authority | PASS |
| Tenant Owner and Workspace Owner do not imply Evidence/Review/Decision authority | PASS |
| Commercial, Billing, Support, Security, Repository, and Platform roles do not imply content/approval authority | PASS |
| Retention/legal-hold tenant and workspace isolation | PASS |
| Legal hold lineage and audit integrity | PASS |

## Validation Results

| Check | Result |
| --- | --- |
| `dotnet restore DataLooMStudio.slnx` | PASS |
| `dotnet build DataLooMStudio.slnx --configuration Release --no-restore` | PASS |
| `dotnet test tests/DataLooMStudio.Architecture.Tests/DataLooMStudio.Architecture.Tests.csproj --configuration Release --no-build` | PASS - 40 tests |
| `dotnet test tests/DataLooMStudio.Persistence.Tests/DataLooMStudio.Persistence.Tests.csproj --configuration Release --no-build` | PASS - 94 tests |
| `dotnet test DataLooMStudio.slnx --configuration Release --no-build` | PASS - 138 tests |
| `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal` | PASS |
| `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive` | PASS - no vulnerable NuGet packages reported |
| `npm install` in `src/Web/DataLooMStudio.Web` | PASS - 0 vulnerabilities |
| `npm run build` in `src/Web/DataLooMStudio.Web` | PASS |
| `npm audit --audit-level=high` in `src/Web/DataLooMStudio.Web` | PASS - 0 vulnerabilities |
| `az bicep build --file infra/main.bicep` | PASS |
| Local secret-pattern scan | PASS - no configured secret patterns found |

## CI Evidence

- CI evidence must be taken from the latest-head PR run after this branch is published.
- No production deployment workflow is authorized by this checkpoint.

## Unresolved Risks

- Production identity activation, Entra/External ID claims mapping, privileged access workflow, break-glass operations, PIM, and production authority administration remain outside Engineering authority.
- Production Evidence, production deployment, customer onboarding, Restricted Pilot Authority, Production Authority, and AI execution remain unauthorized.
- Commercial entitlement runtime mapping remains capability-availability authority only; it still requires future Product-authorized administration surfaces before operational use.
- Legal hold release, retention expiry calculation, and deletion eligibility enforcement remain future Product slices; this increment only defines policies and places legal holds.

## Architecture Office Follow-Up Evidence

- `ARCH-ERD-001` through `ARCH-ERD-005` remain active architecture conditions.
- `ARCH-ERD-003` is satisfied for canonical role names in this bounded slice through `DLS-PROD-AUTH-001`.
- `ARCH-ERD-003` remains active for actor mapping, permission assignment authority, Entra/External ID claims mapping, commercial entitlement mapping, support/admin workflows, Product decision authority, and future role-dependent capability expansion.
- No Architecture redesign conflict was found.
- Security conditions closed under `DLS-SEC-IDENTITY-VERIFY-002` remain preserved by executable tests.

## Exact Next Specialist Workspace

`DataLooM Studio - MVP Delivery and Engineering Office / Codex - DLS-ENG-RETENTION-RELEASE-DELETION-ELIGIBILITY-001`

## Final Result

# CANONICAL PRODUCT AUTHORITY TAXONOMY INTEGRATED; RETENTION GOVERNANCE SLICE IMPLEMENTED FOR PR REVIEW
