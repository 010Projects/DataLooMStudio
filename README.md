# DataLooM Studio

DataLooM Studio is a greenfield modular monolith foundation for tenant and workspace scoped evidence workflows.

## Baseline

- Backend: .NET 10 LTS, REST endpoints, first-party OpenAPI document at `/openapi/v1.json`.
- Frontend: React 19.2, Vite, TypeScript.
- Persistence: PostgreSQL 18 through EF Core and Npgsql.
- Messaging: Azure Service Bus with application-owned transactional outbox.
- Storage: Azure Blob Storage for evidence payloads.
- Identity and secrets: Microsoft Entra ID or External ID, managed identity, Key Vault.
- Observability: OpenTelemetry.
- Delivery: GitHub Actions, Dockerfiles, Bicep, `azure.yaml`.

## Solution Shape

- `src/BuildingBlocks` contains the shared kernel and infrastructure adapter contracts.
- `src/Modules` contains domain module projects only.
- `src/Runtime` composes modules and owns runtime persistence.
- `src/Api` exposes REST/OpenAPI endpoints and references runtime composition instead of modules directly.
- `src/Dls.Worker` is the background execution runtime composition root.
- `src/Dls.Migrate` is the explicit controlled migration runtime.
- `src/Web` contains the React application and has no product authority implementation.

## Module Boundaries

The backend is composed under `src/Modules` with no `Operations` module. The current modules are:

- Tenancy
- Workspaces
- Evidence
- Lineage
- Audit
- Retention
- Commercial
- Lifecycle
- Workflows
- AiGovernance

Each module has a `module.manifest.json`. The `AiGovernance` module is a boundary only and contains no AI execution implementation.

Architecture conformance tests enforce runtime boundaries, module dependencies, BuildingBlocks restrictions, migration isolation, AI boundary enforcement, and React/Product separation.

## Persistence and Migrations

Runtime persistence lives under `src/Runtime/DataLooMStudio.Runtime.Persistence`. EF Core mappings are isolated to runtime persistence, generated migrations execute only through `src/Dls.Migrate`, and application/API or Worker startup does not run database migrations.

The foundation migration creates module-owned PostgreSQL schemas for `identity_access`, `workspace_weave`, `evidence`, `audit_lineage`, and `foundation`, with additional approved migration-boundary catalog entries for retention, commercial, lifecycle, workflow, and AI governance. PostgreSQL row-level security is enabled for tenant and workspace scoped tables, with transaction-local tenant/workspace context.

## Local Validation

```powershell
dotnet restore DataLooMStudio.slnx
dotnet build DataLooMStudio.slnx --configuration Release --no-restore
dotnet test DataLooMStudio.slnx --configuration Release --no-build
dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal
dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive
```

```powershell
Set-Location src/Web/DataLooMStudio.Web
npm install
npm run build
npm audit --audit-level=high
```

```powershell
az bicep build --file infra/main.bicep
```

Controlled migrations are invoked explicitly:

```powershell
dotnet run --project src/Dls.Migrate/DataLooMStudio.Dls.Migrate -- --apply --connection "<connection-string>"
```

## Local Run

```powershell
dotnet run --project src/Api/DataLooMStudio.Api
```

```powershell
dotnet run --project src/Dls.Worker/DataLooMStudio.Dls.Worker
```

```powershell
Set-Location src/Web/DataLooMStudio.Web
npm run dev
```

The Vite dev server proxies `/api`, `/healthz`, and `/openapi` to the API on `http://localhost:5287`.

## Authority Boundary

Engineering owns compilable implementation assets for this checkpoint. Repository creation, product authority, security authority, restricted pilot authority, production authority, and AI execution authority remain outside Engineering.
