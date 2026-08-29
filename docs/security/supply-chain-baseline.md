# DataLooM Studio Supply-Chain Baseline

Artifact: DLS-ENG-PRODUCTION-HARDENING-001

## Implemented Repository Controls

- CI builds API, worker, migration, and web container images without publishing them.
- CI scans built images for high and critical vulnerabilities.
- CI generates dependency evidence for .NET packages, npm packages, and container image metadata.
- CI validates dependency vulnerability status for .NET and npm.
- CI runs a repository secret scan before artifact generation.
- CI validates idempotent EF migration script generation.

## Container Provenance

Image publication is not authorised by this checkpoint. Future published images must record:

- source commit SHA;
- Dockerfile path;
- build workflow run id;
- image digest;
- vulnerability scan result;
- SBOM or dependency evidence location.

## Immutable Deployment References

Production deployment should reference immutable image digests, not mutable tags. Bicep accepts externally governed image references so deployment authority can provide digest-pinned images.

## Dependency Provenance

- .NET dependencies are locked by project references and NuGet package versions in source.
- npm dependencies are locked by `package-lock.json`.
- CI records transitive dependency lists as validation artifacts.

## Signing Boundary

Image signing and verification require registry, key, and deployment-policy authority outside this checkpoint. Repository-side preparation is complete when the pipeline can produce digest-pinned image metadata and validation artifacts. Activation requires a separate production supply-chain authority decision.
