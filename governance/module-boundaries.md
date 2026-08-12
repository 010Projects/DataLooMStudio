# Module Boundaries

DataLooM Studio uses a modular monolith. Modules communicate through runtime composition, shared kernel contracts, persistence mappings, and the transactional outbox. Cross-module shortcuts should be treated as architectural defects.

## Required Boundaries

- `src/Modules/Operations` is prohibited.
- `AiGovernance` is a governance boundary only and must not execute models, prompts, agents, tools, or external AI calls.
- Tenant and workspace identifiers are immutable request scope inputs for workspace-scoped endpoints.
- IdentityAccess owns the bounded Product actor, canonical permission assignment, and separation-of-duty authority policy used by Evidence Review/Decision.
- Evidence records carry immutable lineage IDs and content hashes.
- Evidence owns Evidence review and decision state, but Evidence-local reviewer assignment rows must store canonical IdentityAccess permission keys rather than Product-wide role taxonomy.
- Lineage relationships are versioned rather than overwritten.
- Lifecycle owns state transitions.
- Workflows own orchestration and run tracking, not lifecycle state.
- Retention owns retention policy and legal hold decisions.
- Commercial owns capability entitlements and plan boundaries.
- Runtime composition owns module registration.
- Runtime persistence owns EF Core mappings, Npgsql, and generated migrations.
- The interactive API runtime, Worker runtime, and controlled Migration runtime are distinct deployable responsibilities.
- Application and Worker startup must not execute EF migrations; controlled migration execution belongs to `src/Dls.Migrate`.
- BuildingBlocks must not reference modules, API projects, runtime projects, EF Core, or Npgsql.
- The application owns the transactional outbox contract; runtime persistence owns the EF-backed outbox writer.

## Persistence

The EF Core model lives under `src/Runtime/DataLooMStudio.Runtime.Persistence`. It uses separate PostgreSQL schemas for tenancy, workspace, identity access, evidence, lineage, audit, retention, commercial, lifecycle, workflow, AI governance, and foundation outbox data. Tenant and workspace scoped entities have query filters driven by the request context accessor, but query filters are not treated as sufficient isolation.

PostgreSQL row-level security is authoritative for tenant and workspace isolation. Tenant and workspace values are set at transaction scope through `app.tenant_id` and `app.workspace_id`; missing or invalid context must deny access and pooled connection context must not leak across transactions.

Module migration boundaries are cataloged in runtime persistence. Generated migrations must remain isolated by approved module schema and must not be placed inside module projects.

The current foundation uses a runtime-owned EF context for migration generation and controlled persistence tests. Product modules and BuildingBlocks must not expose or depend on a shared Product DbContext. Future vertical slices must keep module behavior behind module-owned services and must not use the runtime context as a cross-module shortcut.
