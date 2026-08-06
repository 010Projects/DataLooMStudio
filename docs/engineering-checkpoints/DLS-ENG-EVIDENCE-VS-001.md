# DLS-ENG-EVIDENCE-VS-001

## Governed Evidence Registration Vertical Slice Checkpoint

## Repository

- Repository URL: `https://github.com/010Projects/DataLooMStudio`
- Implementation branch: `feature/dls-inc-001-evidence-registration`
- Implementation commit SHA: `078d584076f8e69cad9613586623c86307546c5a`
- Pull request reference: `https://github.com/010Projects/DataLooMStudio/pull/1`
- Merge status: not merged.
- CI result for implementation commit: PASS.
- CI run: `https://github.com/010Projects/DataLooMStudio/actions/runs/31112727806`

## Changed-File Summary

- Added Evidence registration API endpoint.
- Added request/response API contract and OpenAPI path entry.
- Added idempotency key and request hash fields to Evidence records.
- Added EF migration for Evidence registration idempotency.
- Added registration validation, workspace-ownership checks, idempotent replay, and conflict handling.
- Wrapped the Evidence registration transaction in the EF execution strategy for retry-compatible transaction execution.
- Added API, architecture, persistence, RLS, rollback, idempotency, and API-through-PostgreSQL integration tests.

## API Contract

Endpoint:

```http
POST /api/v1/workspaces/{workspaceId}/evidence
```

Accepted context:

- authenticated actor;
- tenant identifier from trusted authentication claims;
- workspace identifier from trusted authentication claims or workspace context header;
- optional `Idempotency-Key` header.

Request body:

```json
{
  "evidenceType": "Document",
  "classification": "Internal",
  "originalFileName": "synthetic.txt",
  "mediaType": "text/plain",
  "declaredSize": 14,
  "contentHash": "64-character-sha256-hex",
  "storageObjectReference": "tenant/workspace/object-reference",
  "retentionPolicyKey": "default",
  "idempotencyKey": "optional-client-key"
}
```

Response body:

```json
{
  "evidenceId": "opaque-product-id",
  "versionId": "opaque-version-id",
  "lifecycleState": "Registered",
  "integrityState": "Pending",
  "createdAt": "timestamp",
  "idempotentReplay": false
}
```

Status behavior:

- `201`: registration committed or idempotent replay returned.
- `400`: invalid command or missing tenant/workspace context.
- `401`: invalid actor context.
- `403`: route workspace does not match context or workspace is not active in tenant context.
- `409`: idempotency key reused with a different request payload.

## Product Behaviour

The endpoint registers synthetic Evidence metadata only. It does not upload content, store Evidence content in PostgreSQL, expose Blob URLs, emit storage credentials, or invoke AI.

Validation rejects unsupported Evidence types, unsupported classifications, invalid media types, invalid SHA-256 hashes, invalid sizes, unsafe public storage references, missing retention policy keys, invalid actors, and workspace mismatches.

## Persistence Behaviour

Evidence registration persists:

- `EvidenceRecord`;
- initial immutable `EvidenceVersion`;
- Product `AuditEntry`;
- `LineageRelationship`;
- transactional `OutboxMessage`.

Idempotency is scoped by Tenant, Workspace, and operation key through a unique PostgreSQL index on `evidence.evidence_records`.

## Transaction Boundary

ADR-014 is implemented in one PostgreSQL transaction. The transaction commits Evidence, initial version, Audit, Lineage, and Outbox atomically. If a database failure occurs inside the transaction, rollback tests verify that no partial Evidence, Audit, Lineage, or Outbox rows remain.

No distributed transaction is introduced.

## RLS Evidence

The slice uses the existing PostgreSQL RLS foundation:

- tenant and workspace context are set transaction-locally;
- missing context is denied;
- cross-Tenant access is denied;
- cross-Workspace access is denied;
- pooled connection context leakage remains covered by integration tests.

Tenant isolation failures: 0.

Workspace isolation failures: 0.

## Audit Evidence

Registration audit records include:

- Tenant;
- Workspace;
- actor;
- authority context;
- action;
- Evidence identifier;
- correlation;
- causation;
- outcome;
- non-sensitive metadata.

Audit metadata does not include Evidence content, secrets, tokens, or storage credentials.

## Lineage Evidence

The initial lineage record establishes the registration relationship for the Evidence lineage identifier and includes Tenant, Workspace, relationship type, actor/process, correlation, causation, timestamp, and version.

## Outbox Evidence

Successful registration writes an `EvidenceRegistered` outbox message atomically with the Evidence transaction. The payload includes event version, Evidence identifier, version identifier, lineage identifier, aggregate identifier, Tenant, Workspace, and integrity state. Async dispatch remains future work.

## Idempotency Behaviour

The command accepts an explicit idempotency key from the request body or `Idempotency-Key` header. If no key is provided, the service derives one from registration metadata.

Semantics:

- same Tenant, Workspace, key, and request payload returns the original successful outcome with `idempotentReplay = true`;
- same Tenant, Workspace, and key with a different payload returns `409`;
- cross-Tenant and cross-Workspace collisions are prevented by scoped uniqueness;
- duplicate replay does not create another Evidence record or Outbox message.

## Automated-Test Results

Local results before branch publication:

- `DataLooMStudio.Architecture.Tests`: PASS, 21 tests.
- `DataLooMStudio.Api.Tests`: PASS, 4 tests.
- `DataLooMStudio.Persistence.Tests`: PASS, 18 tests.
- Total: PASS, 43 tests.

Coverage includes:

- successful API registration through PostgreSQL;
- idempotent replay;
- invalid request rejection;
- invalid actor rejection;
- forbidden workspace rejection;
- missing context rejection;
- immutable initial version;
- rollback on transaction failure;
- Audit persistence;
- Lineage persistence;
- Outbox persistence;
- clean migration and repeat migration execution;
- RLS missing/cross-tenant/cross-workspace/pooled-connection tests;
- AI boundary and no-Blob/no-AI architecture checks.

## Vulnerability Results

- NuGet vulnerable package scan: PASS, no vulnerable packages reported.
- npm audit with high threshold: PASS, 0 vulnerabilities.

## Local Validation Results

- `dotnet restore DataLooMStudio.slnx`: PASS.
- `dotnet build DataLooMStudio.slnx --configuration Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test DataLooMStudio.slnx --configuration Release --no-build`: PASS, 43 tests.
- `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal`: PASS.
- `npm install`: PASS, 0 vulnerabilities.
- `npm run build`: PASS.
- `npm audit --audit-level=high`: PASS, 0 vulnerabilities.
- `az bicep build --file infra\main.bicep`: PASS with Bicep version update notice only.
- Secret scan: PASS, no token/key/private-key material found.

## Known Limitations

- Actual Blob upload and content receipt are excluded.
- Malware scanning is excluded.
- Evidence availability state transitions are excluded.
- Async outbox dispatch remains future work.
- Production Evidence authority is not granted.
- AI, agents, Search, and vector stores remain outside scope.

## Risks

- Runtime.Persistence still owns the concrete transactional implementation. The API depends on an interface, but future vertical slices should continue moving Product decisions into explicit module application boundaries.
- Administrative RLS bypass/governance remains future work beyond migration-owner/application-role separation.
- Branch protection activation remains a repository governance follow-up.

## Next Recommended Vertical Slice

DataLooM Studio - Evidence Content and Integrity Workspace:

- controlled upload allocation;
- Blob adapter boundary;
- short-lived upload access;
- content receipt;
- integrity verification;
- malware-scanning boundary;
- transition toward `Available`;
- Evidence-content lifecycle and failure states.

## Recommendation

EVIDENCE REGISTRATION VERTICAL SLICE COMPLETE WITH CONDITIONS
