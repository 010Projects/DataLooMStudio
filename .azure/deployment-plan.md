# DataLooM Studio Non-Production Test Preparation Plan

Status: Repository Implementation and Hosted Validation Complete - Security Assessment Pending

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

Canonical baseline: e794bb4e4b32416b401b60c567fe83bdac21fefb

## Authority Boundary

This plan prepares repository assets only. It does not authorize Azure deployment, real Entra application creation, production credentials, customer onboarding, Production Evidence, Restricted Pilot, AI execution, physical Evidence destruction, or Production Authority.

## Mode and Recipe

- Mode: modernize the existing .NET 10 / React 19.2 modular monolith.
- Deployment recipe: Azure Developer CLI with the existing Bicep infrastructure.
- Hosting: Azure Container Apps for API, worker, web, and an explicitly invoked migration job.
- Data: Azure Database for PostgreSQL Flexible Server 18 with Entra authentication and separated database principals.
- Identity: user-assigned managed identities for workloads and parameterized Entra public-client/API contracts.
- Messaging/storage/secrets: Service Bus Premium, private Blob Storage, and Key Vault.
- Telemetry: OpenTelemetry exported to a parameterized collector and Azure Monitor-compatible resources.

## Preparation Work

1. Introduce hardened Test and Production environment semantics while retaining Development convenience.
2. Separate migration, API, and worker database identities and database grants; remove administrator credentials from runtime containers.
3. Add an explicit, non-automatic Container Apps migration job and deterministic deployment ordering contract.
4. Define immutable image references, registry publication inputs, provenance metadata, and signing/admission activation boundaries.
5. Add parameterized non-production Entra API and browser-public-client contracts with canonical claims.
6. Implement frontend authentication and one bounded Evidence workflow against the protected API.
7. Add a fail-closed HTTP malware-scanner adapter plus deterministic contract tests; no always-clean Test bypass.
8. Activate transactional outbox dispatch and reconciliation in the worker while preserving the inert disposal adapter.
9. Add metrics, health signals, dashboard/alert definitions, correlation, and deployment-time OTLP configuration.
10. Add hostile isolation and end-to-end harnesses that distinguish local integration from deployed Test validation.
11. Add Test parameters, recovery drill assets, and the eventual operator deployment runbook.
12. Extend CI without reducing any existing build, audit, Trivy, or supply-chain gate.

## Validation Steps

- Restore and Release-build the full .NET solution.
- Run Architecture, API, persistence, worker, identity, scanner, and configuration tests.
- Run formatting, NuGet vulnerability audit, secret scan, and repository diff hygiene.
- Run frontend install, tests, production build, and high-severity npm audit.
- Generate an idempotent migration artifact and validate migration-job Bicep.
- Compile Bicep and validate Test parameter contracts without deploying.
- Build and scan all four images in hosted CI.
- Validate immutable image/provenance contracts and upload evidence artifacts.
- Validate hostile-isolation and end-to-end harness static/local modes.

## External Activation Prerequisites

- Repository protection/ruleset remediation under DLS-REPO-RISK-001.
- Explicit non-production Test deployment authority.
- Approved Azure subscription, region, resource group, and naming context.
- Real Entra API/public-client applications and consent.
- Approved registry and signing identity/policy.
- Approved malware-scanning service endpoint and workload identity.
- Approved OTLP/Azure Monitor destination.
- Database Entra administrator/bootstrap operator for initial principal grants.
- Test-only synthetic Evidence dataset; no customer or production Evidence.

## Stop Conditions

Stop before any real Azure resource change, real Entra application creation, external paid-service purchase, irreversible operation, Product or Architecture semantic change, weakened security control, physical Evidence destruction, or Production Authority decision.

## Deployment Status

Azure Test deployment is not performed or authorized by this plan.

## Static Validation Evidence

- Release solution build: pass, zero warnings and zero errors.
- Bicep build and lint: pass against `infra/main.bicep`.
- Test parameter contracts: pass in placeholder-review and infrastructure-bootstrap modes.
- Migration artifact generation: pass; idempotent SQL generated locally.
- Azure subscription validation and what-if: not run because this checkpoint does not authorize an Azure control-plane operation or provide an approved target subscription/resource group.
- Deployment: not run and not authorized.
