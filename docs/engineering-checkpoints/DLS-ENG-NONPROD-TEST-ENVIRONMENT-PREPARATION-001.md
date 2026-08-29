# DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

## Secure Non-Production Test Preparation Implementation Report

**Canonical baseline:** `e794bb4e4b32416b401b60c567fe83bdac21fefb`

**Branch:** `feature/dls-nonprod-test-preparation-001`

**Repository:** `010Projects/DataLooMStudio`

**Decision:** `IMPLEMENTED - HOSTED VALIDATION PENDING`

**Azure Test deployment:** `NOT PERFORMED / NOT AUTHORIZED`

## 1. Engineering Decision

The bounded repository preparation is implemented. The resulting baseline models a hardened `Test` environment without mislabelling it as `Production`, separates database and workload identities, gates application rollout on an explicitly successful migration execution, uses immutable deployment image references, integrates non-production Entra authentication, provides an executable bounded Evidence journey, keeps malware scanning fail closed, activates only approved outbox work in the worker, and supplies deployable observability and operator validation contracts.

This decision is authority to submit the repository baseline to hosted CI and Security assessment. It is not Test deployment authority. Real identity resources, scanner resources, image signatures, Azure infrastructure, restore operations, and deployed end-to-end validation remain activation work requiring explicit authority.

## 2. Environment Validation Model

- `Development` retains deliberate developer-safe defaults.
- `Test` retains the `Test` host identity and is subject to hardened configuration validation.
- `Production` remains hardened and separately unauthorized.
- A hardened environment rejects local or password-bearing PostgreSQL configuration, wildcard/local hosts and CORS origins, missing Entra audience/scope, missing scanner configuration, and missing HTTPS OTLP export.
- Host environment and `DataLooM:EnvironmentKind` must agree; setting a Test kind on a Development host cannot bypass validation.

## 3. Database Least Privilege

- API, worker, and migration workloads use separate user-assigned managed identities and PostgreSQL Entra principals.
- Runtime connection strings contain usernames and TLS requirements but no passwords.
- Npgsql obtains Azure PostgreSQL access tokens through `DefaultAzureCredential` and a periodic password provider; tokens are not persisted in repository configuration.
- The migration identity remains the elevated schema/bootstrap boundary.
- The API role receives runtime DML over owned module schemas; existing RLS, immutability, audit, retention, and disposal controls remain authoritative.
- The worker has no direct table access. It can execute four fixed outbox functions only.
- A no-login `dls_outbox_executor` owns the fixed `SECURITY DEFINER` functions and holds the narrow table rights needed to claim, complete, fail/dead-letter, and count outbox work across RLS scopes.
- Runtime principal creation uses PostgreSQL Entra object IDs and is performed only by the explicit migration command.

Reference implementation conditions follow Azure PostgreSQL Entra and role/RLS guidance:

- https://learn.microsoft.com/azure/postgresql/flexible-server/concepts-azure-ad-authentication
- https://learn.microsoft.com/azure/postgresql/security/security-connect-with-managed-identity
- https://learn.microsoft.com/azure/postgresql/security/security-manage-entra-users
- https://learn.microsoft.com/azure/postgresql/security/security-access-control
- https://www.npgsql.org/doc/security.html

## 4. Governed Migration Job

The Container Apps migration job is manual, single-replica, non-ingress, non-retrying, separately identified, and digest-referenced. It runs `--apply --bootstrap-runtime-roles` using the migration identity. Infrastructure bootstrap defaults to no job and no applications. Application resources can be created only when the job is enabled and `migrationSuccessEvidence` contains the exact succeeded execution resource ID. Deployment operators must stop on any migration failure; no migration occurs in API startup.

## 5. Images, Provenance, and Signing

- A private Premium ACR is modelled with its administrator account disabled.
- API, worker, migration, and web inputs require immutable `@sha256:` references.
- Each workload receives only ACR pull rights through its own identity.
- The publication contract requires a validated source commit, private-network runner, GitHub OIDC, digest lock, SBOM/provenance capture, Trivy validation, approved signing identity, signature, and verification result.
- No publication workflow, signature, or provenance claim is fabricated. Actual publication/signing/admission requires the approved Test registry, identity, runner, and separate authority.

## 6. Identity and Product Authority

- The API contract uses an Entra API application, delegated `Dls.Access` scope, issuer/audience/signature validation, canonical `tid`/`oid` actor mapping, and an exact `X-Workspace-Id` context.
- The browser uses MSAL as a public client with authorization code and PKCE; no browser secret exists.
- Authentication establishes the actor. IdentityAccess membership and Product permissions remain the runtime authority contract.
- Evidence registration, content allocation/receipt, read, and review paths retain server-side Product Authority, RLS, assignment/scope/freshness, and audit enforcement.
- Missing/malformed tokens produce authentication failure; missing scope, Product permission, or workspace authority produces a safe authorization failure.
- No local canonical role system was introduced in Evidence, Review, or Decision.

## 7. Executable Evidence Journey

The React surface now performs sign-in, authorized workspace selection, Evidence registration, SHA-256 calculation, content allocation, SAS upload, malware-gated receipt, Evidence retrieval, and review request. Safe authorization and scan failures are shown without weakening the API. The summary exposes bounded audit and lineage identifiers returned by the backend. The browser UI was visually checked at desktop and 390-pixel mobile widths; compact navigation was corrected to avoid clipping.

## 8. Malware Scanning

`ManagedIdentityEvidenceMalwareScanner` sends a managed-identity authenticated request containing request, Tenant, Workspace, Evidence, version, object-reference, media, size, and digest context. It validates response request ID and content digest. Suspicious, malicious, unsupported, unknown, malformed, unavailable, token-failure, and timeout outcomes fail closed. Readiness includes scanner health. The automated test double is deterministic and test-bound; it is not a Test runtime always-safe bypass.

External activation requires an approved private scanner endpoint, its workload identity, Blob read scope for the referenced Test object only, network/DNS access, and an audience accepted by the scanner.

## 9. Worker Activation

The worker now claims transactional outbox messages with leases and `SKIP LOCKED`, publishes with the outbox ID as the Service Bus message ID, preserves Tenant/Workspace/correlation metadata, retries with bounded exponential delay, dead-letters after the governed attempt limit, rejects stale lease completion, reclaims expired work, and emits traces and metrics. Processing has a governed suspension switch. The worker does not call Evidence disposal processing.

**Physical Evidence destruction:** `UNAVAILABLE`

**Automatic purge:** `ABSENT`

**Destructive adapter activation:** `ABSENT`

## 10. Observability

- API and worker emit OpenTelemetry traces and metrics to a required HTTPS OTLP endpoint.
- Instrumentation includes ASP.NET Core, HTTP dependencies, Npgsql, correlation IDs, API duration/errors, authorization denials, scanner failures, outbox publish/failure/backlog, database dependency health, and audit persistence failures.
- Container Apps logs remain available through Log Analytics; no unsupported OTLP-log claim is made.
- Dashboard and alert definitions cover availability, errors, latency, worker/outbox, PostgreSQL, Service Bus, scanner, authorization-denial spikes, audit persistence, and startup/configuration failures.
- Alert thresholds are Test operational candidates, not customer SLAs.

## 11. Recovery, Isolation, and E2E Readiness

- The recovery drill covers backup evidence, isolated restore, database/blob validation, audit/lineage integrity, Tenant/Workspace isolation, and reconciliation.
- Hostile deployed tests cover cross-Tenant, cross-Workspace, missing/forged workspace, malformed token, stale/revoked authority, unauthorized Evidence IDs, direct API calls, and worker scope preservation.
- The Evidence E2E harness covers identity, authorization, ingestion, scanning, PostgreSQL, Blob, audit, lineage, worker/outbox, and observability evidence.
- Local/integration and deployed-Test modes are explicitly separated. No Azure restore or deployed E2E execution occurred.

## 12. Deployment Runbook

The operator runbook provides the required 18-step sequence: prerequisites, identity/resource preparation, preview, infrastructure bootstrap, database bootstrap, image publication/signing, migration job creation and execution, API/worker/web rollout, health, observability, identity, hostile isolation, Evidence E2E, recovery drill, evidence capture, and rollback. It defines stop conditions for authority, configuration, migration, signature, health, isolation, scanning, audit, retention/legal hold, and recovery failures.

## 13. Validation Evidence

| Gate | Local result |
|---|---|
| Release build | PASS - 0 warnings, 0 errors |
| Architecture tests | PASS - 55/55 |
| API/config/scanner tests | PASS - 14/14 |
| Worker tests | PASS - 2/2 |
| Frontend tests | PASS - 3/3 |
| Frontend production build | PASS |
| npm audit high | PASS - 0 vulnerabilities |
| .NET formatting | PASS |
| NuGet transitive vulnerability audit | PASS - no vulnerable packages |
| Secret scan | PASS - no findings |
| Diff hygiene | PASS |
| Bicep build/lint | PASS |
| Test parameter contracts | PASS - review and bootstrap modes |
| Deployment harness contracts | PASS |
| Idempotent migration artifact | PASS - 128,718 bytes |
| Local PostgreSQL integration | NOT RUN - Docker Desktop Linux engine unavailable |
| Local container build/Trivy | NOT RUN - Docker Desktop Linux engine unavailable |
| Hosted full solution/PostgreSQL/container/Trivy | PENDING latest-head PR CI |

The hosted workflow retains the full existing build, persistence, architecture, API, formatting, dependency, secret, Bicep, four-container, four-Trivy, supply-chain, and artifact gates. It adds frontend auth/workflow tests and Test environment/harness contract validation without weakening a security gate.

## 14. Complete Changed-File Register

### Governance, CI, infrastructure, operations, and scripts

- `.azure/deployment-plan.md`
- `.github/workflows/ci.yml`
- `DataLooMStudio.slnx`
- `infra/main.bicep`
- `infra/main.parameters.json`
- `infra/environments/test/main.parameters.example.json`
- `infra/modules/postgres-entra-administrator.bicep`
- `infra/modules/private-dns-link.bicep`
- `infra/modules/private-endpoint.bicep`
- `operations/observability/test-alerts.yaml`
- `operations/observability/test-dashboard.yaml`
- `scripts/Test-DeploymentHarnessContracts.ps1`
- `scripts/Test-TestEnvironmentContract.ps1`

### Documentation

- `docs/engineering-checkpoints/DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001.md`
- `docs/operations/test-deployment-runbook.md`
- `docs/operations/test-image-provenance.md`
- `docs/operations/test-observability.md`
- `docs/operations/test-recovery-drill.md`
- `docs/security/nonproduction-identity-contract.md`
- `docs/security/test-database-least-privilege.md`
- `docs/security/test-malware-scanning-contract.md`

### API, infrastructure, migration, worker, and persistence

- `src/Api/DataLooMStudio.Api/Endpoints/EvidenceEndpoints.cs`
- `src/Api/DataLooMStudio.Api/Health/MalwareScannerHealthCheck.cs`
- `src/Api/DataLooMStudio.Api/Middleware/TenantWorkspaceContextMiddleware.cs`
- `src/Api/DataLooMStudio.Api/Program.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Configuration/DataLooMInfrastructureOptions.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Configuration/ProductionConfigurationValidator.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Database/AzurePostgreSqlAccessTokenProvider.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Database/IDatabaseAccessTokenProvider.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/DataLooMStudio.Infrastructure.csproj`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/DependencyInjection/DataLooMInfrastructureServiceCollectionExtensions.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Outbox/OutboxMessage.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Outbox/OutboxMessageStatus.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Outbox/ServiceBusOutboxPublisher.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/SecurityScanning/IEvidenceMalwareScanner.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/SecurityScanning/ManagedIdentityEvidenceMalwareScanner.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/SecurityScanning/UnavailableEvidenceMalwareScanner.cs`
- `src/Dls.Migrate/DataLooMStudio.Dls.Migrate/DataLooMStudio.Dls.Migrate.csproj`
- `src/Dls.Migrate/DataLooMStudio.Dls.Migrate/MigrationCommand.cs`
- `src/Dls.Migrate/DataLooMStudio.Dls.Migrate/Program.cs`
- `src/Dls.Migrate/DataLooMStudio.Dls.Migrate/RuntimeDatabaseRoleBootstrapper.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/OutboxDispatcher.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Program.cs`
- `src/Dls.Worker/DataLooMStudio.Dls.Worker/Worker.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/DependencyInjection/DataLooMPersistenceServiceCollectionExtensions.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceContentService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceQueryService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceRegistrationService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/IEvidenceQueryService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260829201326_TestEnvironmentOutboxDispatch.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260829201326_TestEnvironmentOutboxDispatch.Designer.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Observability/AuditPersistenceTelemetryInterceptor.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Outbox/IOutboxDispatchStore.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Outbox/PostgresOutboxDispatchStore.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`

### Web

- `src/Web/DataLooMStudio.Web/docker-entrypoint.d/40-dls-runtime-config.sh`
- `src/Web/DataLooMStudio.Web/Dockerfile`
- `src/Web/DataLooMStudio.Web/index.html`
- `src/Web/DataLooMStudio.Web/nginx/default.conf.template`
- `src/Web/DataLooMStudio.Web/package-lock.json`
- `src/Web/DataLooMStudio.Web/package.json`
- `src/Web/DataLooMStudio.Web/public/config.js`
- `src/Web/DataLooMStudio.Web/runtime-config/config.js.template`
- `src/Web/DataLooMStudio.Web/src/api/evidence.ts`
- `src/Web/DataLooMStudio.Web/src/App.tsx`
- `src/Web/DataLooMStudio.Web/src/evidence/EvidenceWorkspace.tsx`
- `src/Web/DataLooMStudio.Web/src/main.tsx`
- `src/Web/DataLooMStudio.Web/src/runtimeConfig.ts`
- `src/Web/DataLooMStudio.Web/src/style.css`
- `src/Web/DataLooMStudio.Web/test/contracts.test.mjs`

### Automated validation

- `tests/DataLooMStudio.Api.Tests/ManagedIdentityEvidenceMalwareScannerTests.cs`
- `tests/DataLooMStudio.Api.Tests/ProductionConfigurationValidatorTests.cs`
- `tests/DataLooMStudio.Architecture.Tests/FoundationArchitectureTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceContentApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceContentServiceTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceRegistrationApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceReviewDecisionApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceReviewDecisionServiceTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/PersistenceFoundationTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/TestProductAuthorityService.cs`
- `tests/DataLooMStudio.Worker.Tests/DataLooMStudio.Worker.Tests.csproj`
- `tests/DataLooMStudio.Worker.Tests/OutboxDispatcherTests.cs`
- `tests/e2e/Invoke-EvidenceJourney.ps1`
- `tests/e2e/Invoke-HostileIsolationTests.ps1`
- `tests/e2e/README.md`

## 15. External Activation Prerequisites and Remaining Risks

1. `DLS-REPO-RISK-001` remains open: `main` is unprotected and no repository ruleset exists. Repository Office must enforce PR-only integration, required CI/reviews, and force-push/deletion protection before Test deployment authority.
2. Security Office must assess this baseline and explicitly grant non-production Test deployment authority before any Azure or Entra control-plane action.
3. Real Entra API/SPA registrations, consent, redirect URIs, Product actors/memberships/assignments, and scanner audience must be provisioned and independently reviewed.
4. The private ACR requires an approved private-network runner, OIDC federation, signing identity, digest publication, signatures, and verification/admission evidence.
5. The external fail-closed scanner endpoint, scanner workload identity, Blob read scope, private connectivity, health, and operational ownership must be activated.
6. Azure PostgreSQL Entra administration and runtime principal bootstrap must be tested in the authorized Test environment; runtime role behavior is covered statically and by the hosted PostgreSQL integration suite.
7. OTLP collector/Azure Monitor resources, alert action groups, notification routes, and dashboard import remain deployment-time activation.
8. Backup/restore, hostile isolation, Evidence E2E, and operational drills are prepared but not executed against Azure.
9. Test must use synthetic data only. No customer or Production Evidence is authorized.

## 16. Exact Authority Boundary and Route

Not granted or inferred: Azure Test deployment, Restricted Pilot, Production deployment, Production identity activation, customer onboarding, Production Evidence, AI execution, physical Evidence destruction, destructive adapters, Production Authority, or General Availability.

After latest-head hosted CI succeeds, return this artifact to **DataLooM Studio - Security Office** for an explicit security assessment for **non-production Test deployment authority only**.

Do not request Production Authority or Restricted Pilot authority from this checkpoint.
