# Non-Production Test Validation Harness

## Local and CI integration validation

The solution, API, worker, architecture, scanner-contract, and PostgreSQL 18 persistence tests are the authoritative local/CI integration layer. They use synthetic data and test doubles at external service boundaries. They do not claim that Entra, Azure Blob Storage, Service Bus, the managed malware scanner, or Azure telemetry is active.

Run the repository validation floor and `scripts/Test-DeploymentHarnessContracts.ps1`. GitHub-hosted CI supplies the Linux Docker engine required for PostgreSQL and container validation when Docker Desktop is unavailable locally.

## Deployed Test validation

After explicit Test deployment authority and environment activation, run `Invoke-EvidenceJourney.ps1` with a real non-production bearer token, synthetic file, and authorized workspace. It proves registration, scoped upload, fail-closed malware scanning, persistence, retrieval, Audit/Lineage identifiers, and review initiation.

Run `Invoke-HostileIsolationTests.ps1 -RequireAuthorityScenarios` with separately provisioned cross-Tenant, stale-authority, and revoked-authority actors. Missing scenario identities are a failed governed Test validation, not a reason to skip a case.

Neither harness may use customer or production Evidence. A pass is Test-environment evidence only and grants no Restricted Pilot or Production Authority.
