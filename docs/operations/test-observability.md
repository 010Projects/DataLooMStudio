# Test Observability Activation

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

OpenTelemetry traces and metrics export to `OTEL_EXPORTER_OTLP_ENDPOINT`. Structured console logs, including correlation IDs, are collected by the Container Apps environment into Log Analytics. This split is explicit; this checkpoint does not claim OTLP log export.

## Required Signals

- API: readiness, availability, `dls.api.request.duration`, 4xx/5xx rate, `dls.authorization.denials`, startup/configuration failures.
- Worker: `dls.outbox.published`, `dls.outbox.failed`, `dls.outbox.backlog`, replica failures, Service Bus publish failures, dead-letter transitions.
- Dependencies: `dls.dependencies.failures` with bounded `dependency`/`operation` dimensions for PostgreSQL persistence, Blob allocation/seal/read/quarantine, Service Bus publish, and scanner endpoint/identity failures. `dls.malware.scan.completed` and `dls.malware.scan.failures` record scanner outcomes, including timeout and malformed-response failure.
- Assurance: Product Authority denials, tenant/workspace context rejection, `dls.audit.persistence.failures`, malware quarantine, lineage and outbox events.

`operations/observability/test-dashboard.yaml` and `test-alerts.yaml` are deployment input catalogues bound to instruments emitted by API, Persistence, Infrastructure, and Worker meters. Thresholds are initial operational candidates, not customer SLAs. Before rollout, the operator must bind every panel and rule to the approved Log Analytics/Application Insights resource and an approved non-production action group, test one synthetic firing per severity, and capture dashboard screenshots and alert delivery evidence.

Stop rollout if readiness, database, audit durability, isolation, or malware scanning signals are absent. Physical disposal has no dashboard because execution remains unavailable; any physical-destruction signal is a security incident.
