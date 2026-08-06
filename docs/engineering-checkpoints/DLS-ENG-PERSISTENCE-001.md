# DLS-ENG-PERSISTENCE-001

## Scope

Implemented the PostgreSQL Persistence and Migration Foundation after foundation conformance remediation. The checkpoint establishes EF Core/Npgsql runtime persistence, controlled migration execution, module-owned schemas, RLS isolation evidence, initial tenant/workspace/evidence/audit/lineage/outbox persistence, and ADR-014 atomic evidence registration.

## Repository State

- Repository path: `C:\Users\Bheki\.codex\visualizations\2026\08\05\019fd2f5-5d5c-78f0-a3b6-45a8e0b9e945\DataLooMStudio`
- Branch: `main`
- Commit: `NO_COMMIT` because the local repository has no initial commit.

## Package Versions

- Target framework: `net10.0`
- EF Core: `10.0.10`
- EF Core Design: `10.0.10`
- EF Core Relational: `10.0.10`
- Npgsql EF Core provider: `10.0.3`
- Npgsql test client: `10.0.3`
- Microsoft.Extensions.Hosting: `10.0.10`
- PostgreSQL test image: `postgres:18-alpine`
- React: `19.2.0`
- Vite: `8.2.0`

## Migration Runtime

- Controlled migration runtime: `src/Dls.Migrate/DataLooMStudio.Dls.Migrate`.
- Invocation: `dotnet run --project src/Dls.Migrate/DataLooMStudio.Dls.Migrate -- --apply --connection "<connection-string>"`.
- Migration execution is explicit and returns non-zero exit codes on usage or migration failure.
- API startup and Worker startup do not call `Database.Migrate` or `EnsureCreated`.
- Design-time EF generation uses `src/Dls.Migrate` as startup project and Runtime.Persistence as migration assembly.

## Schemas

Implemented migration catalog entries:

| Module boundary | PostgreSQL schema |
|---|---|
| IdentityAccess | `identity_access` |
| WorkspaceWeave | `workspace_weave` |
| Evidence | `evidence` |
| AuditLineage | `audit_lineage` |
| Retention | `retention` |
| Commercial | `commercial` |
| Lifecycle | `lifecycle` |
| Workflows | `workflow` |
| AiGovernance | `ai_governance` |
| Foundation outbox | `foundation` |

The initial generated migration creates concrete tables for identity/workspace/evidence/audit-lineage/foundation persistence and includes catalog support for the remaining approved schema boundaries.

## Migration

- Migration file: `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260806135504_InitialProductPersistence.cs`
- Designer file: `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260806135504_InitialProductPersistence.Designer.cs`
- Snapshot: `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- Migration history table: `foundation.__EFMigrationsHistory`

## RLS Implementation

PostgreSQL RLS foundation includes:

- `foundation.current_tenant_id()`
- `foundation.current_workspace_id()`
- transaction-local context through `set_config('app.tenant_id', ..., true)` and `set_config('app.workspace_id', ..., true)`
- RLS enabled and forced on tenant/workspace scoped tables;
- policies denying missing context by default;
- cross-tenant and cross-workspace denial;
- storage-reference constraint blocking permanent public HTTP/HTTPS evidence object references;
- sequence and declared-size constraints for evidence versions.

Application context is set by `src/Runtime/DataLooMStudio.Runtime.Persistence/Security/PostgresRlsSessionContext.cs`.

## Persistence Models

Implemented foundation persistence for:

- `Tenant`: opaque ID, lifecycle state, creation timestamp, created-by, concurrency token.
- `Workspace`: opaque ID, owning tenant ID, lifecycle state, data residency region, creation timestamp, created-by, concurrency token.
- `EvidenceRecord`: opaque ID, tenant/workspace scope, evidence type, classification, lifecycle state, registered actor, current version, lineage ID, blob reference, hash, retention boundary, concurrency token.
- `EvidenceVersion`: immutable version ID, evidence ID, sequence, original filename, media type, declared size, content hash, storage object reference, integrity state, creation timestamp, actor attribution, supersession relationship.
- `AuditEntry`: tenant/workspace scope, actor, authority context, action, target, timestamp, correlation, causation, outcome, non-sensitive metadata JSON.
- `LineageRelationship`: tenant/workspace scope, source, target, relationship type, version, actor/process, timestamp, correlation, causation.
- `OutboxMessage`: module-owned transactional outbox message with scope, correlation, availability, attempts, and status.

## Transaction Boundary

ADR-014 initial evidence registration is implemented by `EvidenceRegistrationService`.

Atomic in one transaction:

- Evidence record;
- initial Evidence version;
- required Product Audit record;
- required Lineage relationship;
- transactional Outbox record.

Eventually consistent or out of scope for this checkpoint:

- Blob content persistence;
- asynchronous processing;
- external publication;
- reconciliation workflows;
- production evidence activation.

The implementation uses no distributed transaction.

## Persistence and Isolation Tests

`tests/DataLooMStudio.Persistence.Tests` uses local Docker with PostgreSQL 18 and verifies:

- clean database migration creates required schemas;
- controlled migration execution is repeatable;
- controlled migration execution reports failures;
- tenant and workspace records persist with ownership;
- evidence registration commits evidence/version/audit/lineage/outbox atomically;
- evidence versions are immutable;
- evidence registration rolls back on failure;
- RLS denies missing context, cross-tenant access, cross-workspace access, and pooled connection leakage.

Latest result: `DataLooMStudio.Persistence.Tests` passed 8 of 8 tests.

## Changed Files

Persistence changes include:

- `src/BuildingBlocks/DataLooMStudio.SharedKernel/Integrity/EvidenceVersionId.cs`
- `src/Modules/Tenancy/DataLooMStudio.Modules.Tenancy/Tenant.cs`
- `src/Modules/Workspaces/DataLooMStudio.Modules.Workspaces/Workspace.cs`
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/EvidenceRecord.cs`
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/EvidenceVersion.cs`
- `src/Modules/Audit/DataLooMStudio.Modules.Audit/AuditEntry.cs`
- `src/Modules/Lineage/DataLooMStudio.Modules.Lineage/LineageRelationship.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/DataLooMStudio.Runtime.Persistence.csproj`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/DependencyInjection/DataLooMPersistenceServiceCollectionExtensions.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DesignTimeDataLooMDbContextFactory.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Security/PostgresRlsSessionContext.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceRegistrationRequest.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceRegistrationResult.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceRegistrationService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/ModuleMigrationCatalog.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260806135504_InitialProductPersistence.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260806135504_InitialProductPersistence.Designer.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Dls.Migrate/DataLooMStudio.Dls.Migrate/MigrationRunner.cs`
- `tests/DataLooMStudio.Persistence.Tests/DataLooMStudio.Persistence.Tests.csproj`
- `tests/DataLooMStudio.Persistence.Tests/PostgresFixture.cs`
- `tests/DataLooMStudio.Persistence.Tests/PersistenceFoundationTests.cs`

## Validation Results

- `dotnet restore DataLooMStudio.slnx`: PASS.
- `dotnet build DataLooMStudio.slnx --configuration Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test DataLooMStudio.slnx --configuration Release --no-build`: PASS, 30 total tests.
- `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal`: PASS.
- `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive`: PASS, no vulnerable packages reported.
- `npm install`: PASS, 0 vulnerabilities.
- `npm run build`: PASS.
- `npm audit --audit-level=high`: PASS, 0 vulnerabilities.
- `az bicep build --file infra\main.bicep`: PASS with Bicep version update notice only.

## Skipped Checks

No required local engineering checks were skipped. Infrastructure provisioning, Azure deployment, production evidence activation, customer onboarding, billing provider integration, Search/vector infrastructure, AI implementation, and AI activation were not attempted.

## Unresolved Risks

- Local repository has no initial commit; repository traceability still requires authorized commit/push.
- The EF global tool emitted a non-blocking version notice during migration generation because global `dotnet-ef` was older than the EF runtime packages. Generated migration/build/tests passed.
- Runtime.Persistence uses one runtime-owned EF context for the current foundation. Future vertical slices must avoid using it as a shared Product-service shortcut and should keep behavior behind module-owned application services.
- Administrative RLS bypass/governance roles are not yet modeled beyond migration-owner/application-role separation used by tests.
- Evidence content storage, async outbox dispatch, idempotent Worker handlers, retention/legal-hold workflows, and production authority gates remain future work.

## Recommendation

Persistence: PERSISTENCE FOUNDATION COMPLETE WITH CONDITIONS.

Next Engineering recommendation: DataLooM Studio - Evidence Vertical Slice Workspace should implement the first governed product journey:

`Authenticated Actor -> Tenant -> Workspace -> Evidence Registration -> Immutable Version -> Audit -> Lineage`

This handoff is now allowed because executable migration, transaction, and isolation evidence exists.
