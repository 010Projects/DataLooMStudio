# Test Image Publication and Provenance

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

Canonical repositories are `dataloomstudio-api`, `dataloomstudio-worker`, `dataloomstudio-migrate`, and `dataloomstudio-web` in the environment ACR. Build tags may identify the source commit, but Bicep accepts deployment references only as `<registry>/<repository>@sha256:<digest>` under the Test contract.

Publication is a separately authorized workflow boundary. It must authenticate to ACR through GitHub OIDC federation from an approved runner with private network and DNS reachability to the Test registry, build from the validated commit, preserve current Trivy gates, push once, resolve registry digests, generate SBOM/provenance, sign each digest through the approved keyless or Key Vault-backed identity, and store an image-lock artifact. No PAT, registry admin credential, floating `latest`, or fabricated signature evidence is allowed.

Before application rollout, verify signatures and provenance against the approved repository, workflow identity, source commit, and builder. Azure Policy/admission enforcement remains an external activation prerequisite; absence or failure of verification stops Test rollout. CI build artifacts are evidence of build validation, not evidence that registry publication or signing occurred.
