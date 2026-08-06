# DLS-ENG-FOUNDATION-CONFORMANCE-CLOSURE-001

## Scope

Applied DLS-ENG-FOUNDATION-CONFORMANCE-HANDBACK-002 structural corrections without redesigning the approved modular monolith architecture.

## Conformance Closure

- BuildingBlocks moved to `src/BuildingBlocks` and no longer references modules, API projects, runtime projects, EF Core, or Npgsql.
- Runtime composition introduced under `src/Runtime/DataLooMStudio.Runtime` for module registration.
- Runtime persistence introduced under `src/Runtime/DataLooMStudio.Runtime.Persistence` for EF Core, Npgsql, DbContext mappings, EF outbox writer, and migration boundary metadata.
- Worker runtime introduced under `src/Dls.Worker/DataLooMStudio.Dls.Worker`.
- Controlled migration runtime introduced under `src/Dls.Migrate/DataLooMStudio.Dls.Migrate`.
- API project references Runtime and Runtime.Persistence instead of direct module project references.
- Module projects remain dependent only on BuildingBlocks.
- AiGovernance remains a boundary-only module with no provider client, prompt/model/tool execution, or AI package dependencies.
- React application remains under `src/Web` and does not introduce Product, restricted pilot, production, or product authority surfaces.

## Project Reference Migration

- `src/Shared/*` project paths migrated to `src/BuildingBlocks/*` in the solution and test projects.
- Direct API-to-module references removed from `src/Api/DataLooMStudio.Api/DataLooMStudio.Api.csproj`.
- Worker project references Runtime, Runtime.Persistence, and BuildingBlocks; it does not reference API or Migration runtime.
- Migration runtime references Runtime.Persistence only.
- Module composition references moved to `src/Runtime/DataLooMStudio.Runtime/DataLooMStudio.Runtime.csproj`.
- Persistence composition references moved to `src/Runtime/DataLooMStudio.Runtime.Persistence/DataLooMStudio.Runtime.Persistence.csproj`.
- EF Core and Npgsql package references moved out of BuildingBlocks.Infrastructure and remain only in Runtime.Persistence.

## Validation Results

- `dotnet restore DataLooMStudio.slnx`: passed.
- `dotnet build DataLooMStudio.slnx --configuration Release --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test DataLooMStudio.slnx --configuration Release --no-build`: passed, 30 total tests.
- `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal`: passed after mechanical formatting.
- `npm run build` in `src/Web/DataLooMStudio.Web`: passed.
- `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive`: passed, no vulnerable packages reported.
- `npm audit --audit-level=high`: passed, 0 vulnerabilities.
- `az bicep build --file infra/main.bicep`: passed with an Azure CLI Bicep version update notice only.

## Architecture Office Follow-Up Evidence

- New conformance tests enforce runtime boundaries, module dependencies, BuildingBlocks restrictions, migration isolation, AI boundary enforcement, and React/Product separation.
- `governance/module-boundaries.md` now records Runtime and Runtime.Persistence ownership explicitly.
- Migration boundary catalog identifies the approved module schema set and excludes Operations.

## Unresolved Risks

- API physical path remains `src/Api` rather than `src/Dls.Api`; responsibility is mapped and covered by tests.
- Existing module folder names are mapped to approved schema responsibilities rather than renamed to every approved Product taxonomy term.
- Runtime.Persistence owns a single runtime EF context for the current foundation and must not become a cross-module Product shortcut in vertical slices.
- Security, restricted pilot, production, repository publication, Product authority, and AI execution authority remain outside Engineering scope for this artifact.

## Next Specialist Workspace

DataLooM Studio - Evidence Vertical Slice Workspace.
