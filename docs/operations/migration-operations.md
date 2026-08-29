# DataLooM Studio Migration Operations

Artifact: DLS-ENG-PRODUCTION-HARDENING-001

## Authority Boundary

Production migration execution is not authorised by this document. This runbook defines the operating model that must be satisfied before a controlled migration is requested.

## Preparation

- Confirm the target commit, migration list, and generated idempotent SQL artifact.
- Confirm the deployment environment, tenant impact, expected downtime posture, and rollback/forward-fix strategy.
- Confirm no migration enables production Evidence deletion, AI execution, or customer onboarding authority.
- Confirm the migration is generated from `src/Runtime/DataLooMStudio.Runtime.Persistence` and executed only through `src/Dls.Migrate`.

## Required Approval Boundary

- Engineering prepares and validates the migration artifact.
- Security reviews data-boundary and authority-impacting migrations.
- Architecture reviews only if a migration changes an approved boundary or schema ownership model.
- Product approval is required only if the migration changes product semantics or customer-facing behaviour.
- Production Authority remains a separate decision.

## Pre-Deployment Checks

- Latest main CI must pass.
- `dotnet ef migrations script --idempotent` must generate a migration artifact successfully.
- Bicep must build successfully.
- Database backup availability must be confirmed before execution.
- The migration operator must confirm the target database and environment identity.

## Dry Run

Where a disposable database is available, restore a current backup into an isolated validation environment and execute:

```powershell
dotnet run --project src/Dls.Migrate/DataLooMStudio.Dls.Migrate -- --apply --connection "<isolated-restore-connection>"
```

Capture applied migration count, elapsed time, warnings, and post-migration validation results.

## Execution

- Execute from the approved artifact and commit only.
- Use deployment-supplied secrets or managed identity; do not embed credentials in source.
- Capture command output, operator identity, timestamp, target environment, and commit SHA.
- Run post-migration smoke checks before increasing traffic.

## Failure Handling

- Stop retry loops after repeated deterministic failure.
- Preserve logs and database state for investigation.
- Do not hand-edit production schema outside the approved fix path.
- Choose rollback, restore, or forward-fix through the incident commander and approval chain.

## Evidence Capture

- Commit SHA.
- Generated SQL artifact hash.
- Backup confirmation.
- Migration command output.
- Post-migration validation output.
- Incident or exception record if any.

## Post-Migration Validation

- API `/readyz`.
- Module manifest endpoint.
- Tenant/workspace RLS smoke tests.
- Audit write smoke test.
- Outbox table health.
- Retention/legal hold state query.
- Disposal control-plane invariant: no physical deletion adapter is active.
