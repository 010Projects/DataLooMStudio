# DLS-ENG-EVIDENCE-CONTENT-001
# Evidence Content and Integrity Vertical Slice Checkpoint

## Repository

- Repository: `https://github.com/010Projects/DataLooMStudio`
- Source main commit: `c7e7ec4467951209a7fad369eaaa2130e6fbe335`
- Implementation branch: `feature/dls-inc-001-evidence-content-integrity`
- Implementation commit: `5b5cf09e3efb44d0ce9d001cf3ac697ad57946aa`
- Checkpoint commit: `dbc8db122101bb091f34e7f069bf8b62d74cdaf9`
- Pull request: `#2` - `https://github.com/010Projects/DataLooMStudio/pull/2`
- CI result: PASS - PR run `31122894236` on checkpoint commit `dbc8db122101bb091f34e7f069bf8b62d74cdaf9`
- CI incident evidence: earlier PR run `31121156441` failed in hosted-runner setup before checkout/build/test with `Service Unavailable`; rerun on the checkpoint commit passed.

## Changed Files

- `src/Api/DataLooMStudio.Api/Endpoints/EvidenceEndpoints.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/DependencyInjection/DataLooMInfrastructureServiceCollectionExtensions.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/SecurityScanning/IEvidenceMalwareScanner.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/SecurityScanning/UnavailableEvidenceMalwareScanner.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/AzureEvidenceObjectStore.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/DevelopmentEvidenceObjectStore.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/IEvidenceObjectStore.cs`
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/AzureEvidenceBlobStore.cs` removed
- `src/BuildingBlocks/DataLooMStudio.Infrastructure/Storage/IEvidenceBlobStore.cs` removed
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/EvidenceContentVerification.cs`
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/EvidenceRecord.cs`
- `src/Modules/Evidence/DataLooMStudio.Modules.Evidence/EvidenceUploadAllocation.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/DependencyInjection/DataLooMPersistenceServiceCollectionExtensions.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceContentConflictException.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceContentForbiddenException.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceContentService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/EvidenceContentValidationException.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Evidence/IEvidenceContentService.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260806163952_EvidenceContentIntegrity.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/20260806163952_EvidenceContentIntegrity.Designer.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Migrations/DataLooMDbContextModelSnapshot.cs`
- `src/Runtime/DataLooMStudio.Runtime.Persistence/Persistence/DataLooMDbContext.cs`
- `tests/DataLooMStudio.Architecture.Tests/FoundationArchitectureTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceContentApiTests.cs`
- `tests/DataLooMStudio.Persistence.Tests/EvidenceContentServiceTests.cs`

## API Contracts

- `POST /api/v1/workspaces/{workspaceId}/evidence/{evidenceId}/upload-allocation`
- `POST /api/v1/workspaces/{workspaceId}/evidence/{evidenceId}/versions/{versionId}/content-received`

The upload-allocation response returns an internal storage-object reference, short-lived upload authority, expiry, permitted operation, media type and maximum size. The content-received response returns lifecycle state, integrity outcome, scan outcome, failure reason where present, verified size and verified hash.

## Lifecycle Implementation

Supported success path:

```text
Registered
    -> UploadAllocated
    -> Available
```

Audited intermediate events include content receipt, integrity verification started/succeeded, scan requested/completed and availability. The persisted current lifecycle state is held on `EvidenceRecord`; immutable version rows are not modified.

Supported failure paths:

```text
UploadAllocated
    -> Quarantined
```

Quarantine is applied for size mismatch, hash mismatch, malicious scan, suspicious scan, failed scan, unavailable scan and unsupported scan.

## Storage Adapter

`IEvidenceObjectStore` is provider-neutral and supports upload allocation, metadata confirmation, controlled content read, quarantine and removal of uncommitted objects. `AzureEvidenceObjectStore` keeps Azure SDK types and short-lived SAS generation inside infrastructure. `DevelopmentEvidenceObjectStore` models the same semantics for tests without provisioning Azure resources.

## Upload Authority Semantics

- Authority is object-specific and short-lived.
- Permitted operation is write-only.
- Permanent Blob URLs and account-key application access are not introduced.
- Upload authority is returned to the caller but only a hash is stored in Product persistence.
- Allocation records preserve Tenant, Workspace, Evidence version, expiry, media type and size constraints.

## Integrity Verification

The service verifies actual object existence through the storage adapter, compares actual size to declared size, computes SHA-256 from a controlled stream when provider-native hash evidence is not available, and prevents client assertion alone from proving content receipt.

## Scanning Boundary

`IEvidenceMalwareScanner` is provider-neutral. Outcomes are `Clean`, `Malicious`, `Suspicious`, `Failed`, `Unavailable` and `Unsupported`. Only `Clean` permits `Available`. The default scanner is `UnavailableEvidenceMalwareScanner`, which safely prevents availability when no approved provider is configured.

## Quarantine Behaviour

Quarantine preserves Evidence and content references, records the cause, writes Audit and Lineage, emits outbox notification, and invokes storage quarantine after the database consistency boundary commits. The slice does not implement Security investigation or controlled release from quarantine.

## Availability Decision

Evidence becomes `Available` only when:

- content receipt is confirmed by storage metadata;
- actual size matches the declared version size;
- computed or trusted SHA-256 hash matches the declared version hash;
- malware scan result is `Clean`;
- Audit, Lineage, verification and outbox writes commit atomically.

## Transaction and Outbox Model

Database-owned state changes are committed atomically: allocation or verification row, Evidence lifecycle state, Audit, Lineage and Outbox. External object-store and scanner calls are outside the PostgreSQL transaction and are represented through explicit states, idempotency and recoverable quarantine behavior.

## Isolation Evidence

Executed tests cover:

- cross-Tenant upload allocation denial;
- cross-Workspace content receipt denial;
- missing context denial;
- expired allocation rejection;
- mismatched or invalid receipt rejection through service/API validation;
- background integrity processing preserving Tenant and Workspace context;
- pooled-context safety through existing RLS tests;
- quarantine records remaining workspace-scoped.

Tenant isolation failures: `0`

Workspace isolation failures: `0`

## Audit Evidence

Audit records are written for:

- upload allocation;
- upload allocation expiry;
- content receipt;
- integrity verification started;
- integrity verification succeeded;
- integrity verification failed;
- scan requested;
- scan completed;
- quarantine;
- availability.

Operational logging is not used as Product Audit.

## Lineage Evidence

Lineage relationships connect the Evidence lineage ID to content events, integrity outcomes, scanning outcomes, availability and quarantine events. Evidence version IDs and allocation IDs are included in Audit and Outbox payloads; immutable lineage IDs are preserved.

## Automated Test Results

- `dotnet restore DataLooMStudio.slnx`: PASS
- `dotnet build DataLooMStudio.slnx --configuration Release --no-restore`: PASS - 0 warnings, 0 errors
- `dotnet test DataLooMStudio.slnx --configuration Release --no-build`: PASS - 66 tests
- Architecture tests: PASS - 25 tests
- API tests: PASS - 4 tests
- Persistence tests: PASS - 37 tests
- Content-integrity tests: PASS
- Quarantine tests: PASS
- Storage-adapter tests: PASS
- Malware-boundary tests: PASS
- RLS tests: PASS
- Rollback tests: PASS
- Idempotency tests: PASS
- React/Product separation tests: PASS
- AI boundary tests: PASS

## Vulnerabilities and Security Checks

- `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive`: PASS - no vulnerable packages
- `npm install`: PASS - 0 vulnerabilities
- `npm audit --audit-level=high`: PASS - 0 vulnerabilities
- Local secret-pattern scan: PASS - no matches
- `az bicep build --file infra/main.bicep`: PASS - Bicep version notice only

## Limitations

- No Azure resources were provisioned.
- No production malware provider was selected or accepted by Security.
- Azure storage adapter remains unprovisioned and unvalidated against a live storage account.
- Quarantine release and Security investigation workflow are not implemented in this slice.
- Retrieval and reviewer decision workflows are not implemented in this slice.
- PR merge remains subject to repository merge governance and latest-head checks.

## Risks

- GitHub Actions runner availability delayed PR CI evidence during checkpoint authoring; rerun on checkpoint commit passed.
- Production upload authority depends on Azure user-delegation SAS configuration and managed identity permissions that require later Azure validation.
- The default scanner intentionally returns `Unavailable`; environments without an approved scanner will quarantine received content.

## Next Engineering Recommendation

**EVIDENCE CONTENT AND INTEGRITY SLICE COMPLETE WITH CONDITIONS**

Conditions:

1. Repository merge governance must be completed without bypassing required checks.
2. Production storage and malware provider evidence require separate Security and Azure validation authority.

After successful PR review, CI and merge governance, the next specialist workspace is:

**DataLooM Studio — Evidence Review and Decision Workspace**
