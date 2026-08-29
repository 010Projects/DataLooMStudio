# DataLooM Studio Production-Intent Runbooks

Artifact: DLS-ENG-PRODUCTION-HARDENING-001

## Application Startup Failure

- Automated recovery: platform restart according to Container Apps policy.
- Engineering action: inspect startup logs, configuration validation errors, image digest, and dependency availability.
- Security escalation: required if startup failure is caused by identity, Key Vault, or suspected secret exposure.
- Production Authority: required before changing production deployment posture.

## Configuration Failure

- Automated recovery: none beyond failed startup.
- Engineering action: compare runtime settings with approved environment configuration.
- Security escalation: required for identity, secret, host/origin, or authority boundary misconfiguration.
- Product decision: required if external origins or product-facing authority assumptions change.

## Database Migration Failure

- Automated recovery: no automatic schema rollback.
- Engineering action: stop rollout, preserve logs, identify failed migration and data state.
- Security escalation: required for tenant/workspace isolation, audit, authority, or evidence-integrity impact.
- Production Authority: required for rollback, restore, or forward-fix execution.

## Database Recovery

- Automated recovery: provider-managed backups only.
- Engineering action: execute the approved restore procedure in an isolated target first.
- Security escalation: required for audit, lineage, legal hold, or disposal inconsistency.
- Product decision: required for customer-visible data recovery communication.

## Worker Backlog

- Automated recovery: scale worker replicas within approved limits.
- Engineering action: inspect outbox age, worker logs, Service Bus health, and dependency errors.
- Security escalation: required if backlog affects audit durability or disposal governance state.
- Production Authority: required before changing worker activation model.

## Service Bus Processing Failure

- Automated recovery: retry and dead-letter behaviour governed by Service Bus configuration.
- Engineering action: inspect dead-letter counts, message age, duplicate detection, and publisher failures.
- Security escalation: required for authority or audit messages.

## Audit Durability Incident

- Automated recovery: transaction rollback prevents consequential mutation where enforced.
- Engineering action: preserve failed transaction evidence and identify durability dependency.
- Security escalation: mandatory.
- Product decision: required if user-visible workflow state is affected.

## Authority or Authorization Anomaly

- Automated recovery: stale or contradictory authority denies sensitive action.
- Engineering action: inspect Product Authority audit records and actor correlation.
- Security escalation: mandatory for bypass, actor mismatch, stale authority failure, or SoD failure.
- Product decision: required for role or permission model changes.

## Tenant Isolation Incident

- Automated recovery: deny on missing or contradictory context.
- Engineering action: stop affected workflow, preserve logs, and run RLS validation.
- Security escalation: mandatory Sev 1.
- Production Authority: required for any production traffic decision.

## Evidence Lifecycle Incident

- Automated recovery: none that mutates lifecycle state.
- Engineering action: inspect Evidence, Audit, Lineage, retention, and workflow state.
- Security escalation: required for integrity or audit gaps.
- Product decision: required for customer-visible Evidence workflow handling.

## Disposal Execution Suspension

- Automated recovery: disabled disposal object store returns suspended outcomes.
- Engineering action: confirm no physical adapter is present and no automatic purge loop exists.
- Security escalation: mandatory for any destructive capability signal.
- Production Authority: no physical deletion can be enabled without a separate authority sequence.

## Key Vault or Dependency Outage

- Automated recovery: retry according to SDK and platform policy.
- Engineering action: validate managed identity, RBAC assignment, network reachability, and dependency health.
- Security escalation: required for secret exposure or identity drift.

## Deployment Rollback

- Automated recovery: Container Apps revision rollback where authorised.
- Engineering action: identify target revision or image digest and confirm migration compatibility.
- Security escalation: required if rollback affects authority, audit, Evidence, retention, legal hold, or disposal semantics.
- Production Authority: required before production rollback execution.
