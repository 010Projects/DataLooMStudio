# DLS-ENG-BASELINE-REG-001

## Greenfield Engineering Baseline Registration Record

## Repository

- Canonical repository URL: `https://github.com/010Projects/DataLooMStudio`
- Repository owner: `010Projects`
- Repository name: `DataLooMStudio`
- Source branch: `main`
- Pull request reference: not applicable; repository was empty and the initial baseline was pushed directly to `main` without force.
- Initial root commit SHA: `f172887f677ffd15497a6cfd5b7062b5466aab1c`
- Registered implementation commit SHA: `850a7344c478880c5fee8fa103264f75c006d032`
- Merge commit SHA: not applicable; no pull request merge was used for the empty-repository baseline.

## Remote Verification

- Remote was confirmed empty before baseline publication.
- Remote default branch after push: `main`
- Remote `main` head after CI repair: `850a7344c478880c5fee8fa103264f75c006d032`
- No force push was used.
- Branch protection was not present because the repository had no branch before the initial push.

## CI Result

- Workflow: `ci`
- Run ID: `31111192887`
- Run URL: `https://github.com/010Projects/DataLooMStudio/actions/runs/31111192887`
- Result: PASS
- Jobs completed: restore, build, tests, backend package audit, frontend install/build/audit, Bicep build.
- Note: the first CI run for root commit `f172887f677ffd15497a6cfd5b7062b5466aab1c` exposed a Linux path-normalization issue in the architecture test helper. The non-force repair commit `850a7344c478880c5fee8fa103264f75c006d032` corrected it and passed CI.

## Local Validation Before Registration

- `dotnet restore DataLooMStudio.slnx`: PASS.
- `dotnet build DataLooMStudio.slnx --configuration Release --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test DataLooMStudio.slnx --configuration Release --no-build`: PASS, 30 total tests.
- `dotnet format DataLooMStudio.slnx --verify-no-changes --verbosity minimal`: PASS.
- `dotnet list DataLooMStudio.slnx package --vulnerable --include-transitive`: PASS, no vulnerable packages reported.
- `npm install`: PASS, 0 vulnerabilities.
- `npm run build`: PASS.
- `npm audit --audit-level=high`: PASS, 0 vulnerabilities.
- `az bicep build --file infra\main.bicep`: PASS with Bicep version update notice only.

## Secret Scan Result

- Secret scan: PASS.
- Method: local restricted-content inspection plus regex scan for key material, GitHub tokens, storage keys, bearer tokens, API keys, client secrets, and Azure secret/token environment names.
- Result: no token/key/private-key material found in tracked content.
- Runtime defaults were corrected before commit to remove hardcoded local PostgreSQL passwords. Synthetic test-container credentials remain isolated to integration tests.

## Repository Tree Summary

- `.github/workflows`: non-deploying CI workflow.
- `.azure`, `azure.yaml`, `infra`: deployment planning and Bicep templates only; no provisioning performed.
- `docs/engineering-checkpoints`: foundation, conformance, persistence, and baseline registration records.
- `governance`: engineering authority, AI boundary, module boundaries, and ADR-014.
- `src/Api`: interactive REST/OpenAPI backend runtime.
- `src/Dls.Worker`: background Worker runtime scaffold.
- `src/Dls.Migrate`: explicit controlled migration runtime.
- `src/BuildingBlocks`: shared kernel and infrastructure adapter contracts.
- `src/Runtime`: module composition and runtime persistence.
- `src/Modules`: Product module foundations; no `Operations` module.
- `src/Web`: React frontend, separate from Product authority.
- `tests`: API, architecture, and PostgreSQL persistence/RLS integration tests.

## Checkpoint Document References

- `docs/engineering-checkpoints/DLS-ENG-FOUNDATION-001.md`
- `docs/engineering-checkpoints/DLS-ENG-FOUNDATION-CONFORMANCE-HANDBACK-001.md`
- `docs/engineering-checkpoints/DLS-ENG-PERSISTENCE-001.md`
- `docs/engineering-checkpoints/DLS-ENG-FOUNDATION-CONFORMANCE-CLOSURE-001.md`

## Remaining Non-Blocking Conditions

- API physical path remains `src/Api` rather than `src/Dls.Api`; responsibility is mapped and tested.
- Some module folder names are mapped to approved taxonomy rather than renamed.
- Runtime.Persistence owns one runtime EF context for the foundation and must not become a cross-module Product shortcut.
- Branch protection should be activated through repository governance after the initial baseline exists.
- Production deployment, infrastructure provisioning, production evidence use, Product authority, Security authority, and AI implementation remain outside this Engineering authority.

## Baseline Status

Canonical Engineering Baseline: REGISTERED
