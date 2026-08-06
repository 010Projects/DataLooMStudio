# ADR-014 Evidence Consistency Boundary

Status: Accepted

## Context

Evidence must preserve integrity, auditability, lineage, retention, and legal hold behavior across tenant and workspace boundaries. Evidence payloads live in Blob Storage, while authoritative evidence metadata lives in PostgreSQL. Outbound notifications use Azure Service Bus through the transactional outbox.

## Decision

The Evidence module owns the evidence consistency boundary.

Evidence ingestion and mutation must persist metadata, immutable lineage IDs, content hash metadata, legal hold indicators, and outbox messages in the same application transaction before publishing any external message. Blob payload writes are referenced by immutable blob names and verified by SHA-256 metadata.

Lineage relationship changes are versioned. Retention and legal hold decisions are separate module concerns, but Evidence records must expose the identifiers and flags needed to enforce them without bypassing Retention.

## Consequences

- Evidence metadata is the source of truth for integrity and lineage references.
- Service Bus publication is derived from the outbox and never from direct in-request publishing.
- Legal hold release and retention expiry require module-coordinated workflows.
- AI governance metadata may reference evidence but cannot execute models inside the Evidence boundary.
