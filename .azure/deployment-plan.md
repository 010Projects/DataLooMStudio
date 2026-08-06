# DataLooM Studio Deployment Preparation Plan

Status: Ready for Validation

Artifact: DLS-ENG-FOUNDATION-001 - Greenfield Solution Foundation

## Scope

Prepare deployment-ready engineering foundation assets for the DataLooMStudio modular monolith. This plan does not grant repository creation, product, security, restricted pilot, production, or AI execution authority.

## Architecture Baseline

- Backend: .NET 10 LTS modular monolith exposed through REST/OpenAPI.
- Frontend: React 19.2.
- Data: PostgreSQL 18 through EF Core and Npgsql.
- Messaging: Azure Service Bus with application-owned transactional outbox.
- Storage: Azure Blob Storage for evidence payloads.
- Identity and secrets: Microsoft Entra ID or External ID with Key Vault.
- Observability: OpenTelemetry.
- Delivery: GitHub Actions and Bicep.

## Mandatory Boundaries

- No `src/Modules/Operations` module.
- `AiGovernance` exists as an AI boundary only; no AI execution is implemented.
- Tenant and workspace isolation are enforced in contracts, middleware, and persistence.
- Evidence integrity, auditability, lineage, retention, legal hold, and commercial capability boundaries are first-class module concerns.
- Lifecycle and workflow responsibilities remain separate.
- ADR-014 defines the evidence consistency boundary.

## Planned Outputs

- Compilable .NET 10 solution with API, shared kernel, infrastructure, and module projects.
- Buildable React 19.2 application under `src/Web/DataLooMStudio.Web`.
- Module manifests and governance documents under `src/Modules/*/module.manifest.json` and `governance/`.
- Bicep infrastructure for Container Apps, PostgreSQL 18, Service Bus, Blob Storage, Key Vault, managed identity, private networking, and Log Analytics.
- CI workflow for restore, build, test, frontend build, audit, and Bicep build.

## Validation Plan

- Restore, build, and test .NET projects.
- Restore and build frontend dependencies.
- Run package vulnerability checks where tooling supports it.
- Run Bicep build if Azure CLI/Bicep tooling is available locally.

## Deployment Status

Deployment is explicitly out of scope for this engineering checkpoint.

## Decisions

- Hosting preparation target: Azure Container Apps for API and web containers.
- Database target: Azure Database for PostgreSQL Flexible Server with PostgreSQL version `18`.
- Network target: private PostgreSQL subnet and Container Apps virtual network integration.
- Secret target: deployment-supplied secure parameters and Key Vault with RBAC enabled.
- Messaging target: Azure Service Bus Premium namespace with an outbox topic.
- Evidence target: private Azure Blob container with blob versioning and delete retention.
- OpenAPI target: first-party `/openapi/v1.json` endpoint to avoid carrying a vulnerable OpenAPI generator package.
