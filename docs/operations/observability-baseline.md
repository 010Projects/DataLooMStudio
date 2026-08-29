# DataLooM Studio Observability Baseline

Artifact: DLS-ENG-PRODUCTION-HARDENING-001

## Canonical Telemetry

OpenTelemetry is the canonical telemetry approach. Production startup validation requires an externally governed OTLP endpoint so telemetry does not silently fall back to a local collector.

## Service Indicators

- API availability: `/healthz` and `/readyz`.
- API error rate: HTTP 5xx and policy-denial error classes.
- API latency: route-level duration percentiles.
- PostgreSQL health: readiness check, connection failures, command failures, migration history.
- Outbox backlog: pending count, oldest pending age, retry/failure count.
- Worker health: replica availability, startup failures, processing loop health once dispatchers are activated.
- Message-processing failures: Service Bus dead-letter count and publish failures.
- Audit durability failures: failed audit persistence and rollback events.
- Authorization anomalies: denial spikes, stale authority denials, SoD denials, actor mismatch denials.
- Tenant/workspace isolation failures: rejected context substitution, RLS-denied access, missing context.
- Dependency health: Blob, Key Vault, Service Bus, PostgreSQL, OTLP exporter.

## SLO Candidates

These are operational readiness controls, not contractual SLAs:

- API successful request ratio by route.
- API p95 latency by route class.
- Readiness success ratio.
- Outbox pending age.
- Audit persistence success ratio.
- Product Authority evaluation success/deny distribution.
- Worker processing error ratio once processing is activated.

## Alert Strategy

- Page on sustained readiness failure or database unavailability.
- Page on audit durability failures.
- Page on suspected tenant/workspace isolation breach.
- Ticket on rising authority-denial anomalies unless tied to an active incident.
- Ticket on outbox backlog age beyond the operational threshold.
- Ticket on dependency degradation before user-facing failure.

## Dashboard Baseline

Minimum dashboard sections:

- API health and route latency.
- Error budget indicators.
- PostgreSQL availability and migration state.
- Outbox status.
- Worker status.
- Dependency status.
- Audit and authority denial trends.
- Evidence lifecycle and retention/legal hold operational events.

## Severity Classification

- Sev 1: tenant isolation breach, audit loss, production-wide outage, unauthorised destructive capability.
- Sev 2: sustained write failure, migration failure requiring rollback/restore, worker backlog blocking product workflows.
- Sev 3: degraded dependency, elevated denial rates, isolated route error.
- Sev 4: documentation or telemetry gap without active customer impact.

## Escalation

- Engineering owns diagnosis and remediation.
- Security owns isolation, audit, authority, and suspected compromise decisions.
- Product owns customer-visible authority or workflow decisions.
- Production Authority owns production deployment and activation decisions.
