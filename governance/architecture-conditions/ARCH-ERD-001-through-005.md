# ARCH-ERD-001 Through ARCH-ERD-005
# Active Evidence Review/Decision Architecture Conditions

## Status

- Source: Architecture Office conformance handback for the Evidence Review/Decision slice.
- Status: active with conditions.
- Applies to: Evidence Review/Decision and the IdentityAccess/Product Authority integration increment.
- Missing authoritative source bodies: `DLS-INC-002-EVID-001` and `DLS-INC-002-PROD-DEC-001`.

Engineering must not reconstruct or infer the missing source bodies. Any Product-wide authority, role taxonomy, lifecycle vocabulary, retention values, Restricted Pilot scope, Production Evidence, AI execution, or Production Authority decision remains outside Engineering authority until the authoritative sources are recovered and approved.

## Conditions

### ARCH-ERD-001

The implemented Evidence Review/Decision slice is preserved as a bounded implementation. Review remains independent from Decision, candidate decision state remains separate from authoritative decision state, and Evidence keeps immutable history, audit, lineage, and transactional outbox semantics under ADR-014.

### ARCH-ERD-002

Evidence-local `EvidenceReviewer` and `EvidenceApprover` names were provisional local policy abstractions only. Active implementation must not treat those names as canonical enterprise roles, Product-wide role taxonomy, Entra group names, commercial capabilities, support/admin permissions, or Production Authority.

### ARCH-ERD-003

This is the material condition before further role-dependent capability expands. Product-wide role taxonomy, actor mapping, permission assignment authority, separation-of-duty policy, Entra/External ID claims, commercial entitlement mapping, support/admin permissions, and Product decision authority must be supplied by Product, Architecture, and Security authority before expanding beyond the bounded Evidence review/decision use case.

### ARCH-ERD-004

Runtime.Persistence remains infrastructure only. Product modules own domain policy and module state. Runtime.Persistence may map module-owned entities, coordinate RLS-scoped transactions, persist audit/lineage/outbox records, and run generated migrations, but it must not become Product metadata authority or mutate another module schema through shortcuts.

### ARCH-ERD-005

No production deployment, Production Evidence, AI execution, Restricted Pilot authority, Production Authority, or production messaging publication is authorized by this increment. Messaging remains represented by the transactional outbox until a later authorized production publisher increment.

## Engineering Evidence Required

- Runtime boundaries remain enforced by architecture tests.
- Module dependencies remain one-way into BuildingBlocks only.
- BuildingBlocks remain free of EF Core, Npgsql, runtime, API, and module dependencies.
- Migrations remain generated under Runtime.Persistence and isolated by approved module schema.
- AI governance remains a boundary only and contains no model, prompt, agent, tool, provider, or external AI execution.
- React remains separate from Product authority and must not own Product lifecycle or authority rules.
- Evidence reviewer assignment rows store canonical Product permission keys only.
- IdentityAccess owns the canonical Product actor, permission assignment, and separation-of-duty policy boundary for this bounded use case.
