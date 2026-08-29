# Secure Test Deployment Runbook

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

Status: prepared, not executed. This runbook requires explicit Test deployment authority.

1. Verify authority, protected `main`, successful latest-head CI, approved subscription/resource group/region, synthetic data, Entra registrations, scanner, OTLP destination, action group, backup policy, and a governed Test operator network/VPN that resolves and reaches private endpoints. Set `deployMigrationJob=false` and `deployApplications=false`; run `./scripts/Test-TestEnvironmentContract.ps1 -ParametersFile <approved-parameters> -InfrastructureBootstrap`.
2. Verify Azure identity has only resource-group deployment, role-assignment, and approved Entra bootstrap rights. No production subscription or credential may be present.
3. Run `az deployment group what-if --resource-group <test-rg> --template-file infra/main.bicep --parameters @<approved-parameters> deployApplications=false`. Security reviews public-network, RBAC, identity, deletion, and replacement changes.
4. Deploy infrastructure with `deployMigrationJob=false` and `deployApplications=false`. This creates private data services, registry, and managed identities; no runnable job, API, worker, or web exists.
5. Confirm API, worker, and migration identity object IDs. Confirm PostgreSQL Entra-only authentication and run the job's `--bootstrap-runtime-roles` path. API receives DML only; worker receives only outbox function execution; migration owns DDL.
6. Publish each reviewed image once to the approved Test registry. Record registry/repository/digest, source commit, SBOM, provenance, signing identity, signature, and verification result. Replace all placeholders, set `deployMigrationJob=true`, and run the parameter contract without placeholder switches. Never use `latest`.
7. Preview and deploy the migration job only. Start it with `az containerapp job start --name <migration-job> --resource-group <test-rg>`. Wait for the exact execution and require `Succeeded`; archive logs and migration count. Do not continue on timeout, retry, or failure.
8. Capture the exact succeeded migration execution resource ID in `migrationSuccessEvidence`, set `deployApplications=true`, retain the exact digests and migration-job setting, run a third `what-if`, then deploy API/worker/web. The resource tag records the migration evidence. No migration runs in API startup.
9. Verify `/healthz`, `/readyz`, revision/image digests, non-root containers, private DNS, PostgreSQL, Blob, Service Bus, Key Vault, scanner health, and OTLP export.
10. Verify SPA login, issuer/audience/signature/scope rejection, Product Actor correlation, workspace membership, permission assignment, and safe `401`/`400`/`403` behavior.
11. Run `tests/e2e/Invoke-HostileIsolationTests.ps1` with approved synthetic actors, including stale/revoked authority inputs.
12. Run `tests/e2e/Invoke-EvidenceJourney.ps1`; capture Evidence, scan, Audit, Lineage, outbox, Blob, and telemetry evidence.
13. Bind and test every rule in `operations/observability/test-alerts.yaml` and capture dashboard evidence.
14. Execute `docs/operations/test-recovery-drill.md` only under its separate drill authority.
15. Return commit, digests, identity IDs, deployment operations, migration execution, E2E/isolation results, telemetry, alerts, restore evidence, exceptions, and rollback disposition to Security.
16. Rollback by setting `deployApplications=false` or reverting to previously verified digests. Schema rollback requires an approved forward fix or isolated restore; never hand-edit or run destructive down migrations.

Stop immediately for placeholder values, unsigned/unverified image, mutable tag, public data-plane access, role overgrant, migration failure, unhealthy readiness, scanner bypass/unavailability, missing telemetry, isolation failure, Audit/Lineage failure, production data/credential, physical deletion path, AI execution, or any unapproved replacement/destruction in `what-if`.
