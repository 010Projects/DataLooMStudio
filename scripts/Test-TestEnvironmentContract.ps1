[CmdletBinding()]
param(
    [string] $ParametersFile = './infra/environments/test/main.parameters.example.json',
    [switch] $AllowPlaceholders,
    [switch] $InfrastructureBootstrap
)

$ErrorActionPreference = 'Stop'
$document = Get-Content -LiteralPath $ParametersFile -Raw | ConvertFrom-Json -Depth 20
$parameters = $document.parameters
foreach ($name in 'deployMigrationJob', 'deployApplications', 'migrationVerification') {
    if ($parameters.PSObject.Properties.Name -notcontains $name) {
        throw "$name must be declared explicitly by the Test parameter contract."
    }
}
$deployMigrationJob = [bool] $parameters.deployMigrationJob.value
$deployApplications = [bool] $parameters.deployApplications.value
$placeholdersAllowed = $AllowPlaceholders -or $InfrastructureBootstrap

if ($parameters.environment.value -ne 'test') {
    throw 'Test parameter contract must set environment to test.'
}

if ($deployApplications -and -not $deployMigrationJob) {
    throw 'Application deployment requires the governed migration job boundary.'
}

if ($deployApplications) {
    $migrationEvidence = $parameters.migrationVerification.value
    $statusIsValid = $migrationEvidence.status -ceq 'Succeeded'
    $imageIsValid = $migrationEvidence.imageDigest -ceq $parameters.migrationContainerImage.value
    $executionNameIsValid = -not [string]::IsNullOrWhiteSpace($migrationEvidence.executionName)
    $executionIdIsValid = $migrationEvidence.executionResourceId -match "/jobs/.+/executions/$([regex]::Escape($migrationEvidence.executionName))$"
    $migrationIsValid = -not [string]::IsNullOrWhiteSpace($migrationEvidence.lastAppliedMigration)
    $evidenceDigestIsValid = $migrationEvidence.evidenceSha256 -match '^sha256:[a-f0-9]{64}$'

    if (-not ($statusIsValid -and $imageIsValid -and $executionNameIsValid -and $executionIdIsValid -and $migrationIsValid -and $evidenceDigestIsValid)) {
        throw 'Application deployment requires live-verified migration execution identity, status, image, migration, and evidence digest.'
    }
}

$migrationVerification = $parameters.migrationVerification.value
$bootstrapHasWorkloads = $deployMigrationJob -or $deployApplications -or -not [string]::IsNullOrWhiteSpace($migrationVerification.executionResourceId)
if ($InfrastructureBootstrap -and $bootstrapHasWorkloads) {
    throw 'Infrastructure bootstrap cannot create the migration job or application workloads.'
}

$images = 'apiContainerImage', 'workerContainerImage', 'migrationContainerImage', 'webContainerImage'
foreach ($image in $images) {
    $value = $parameters.$image.value
    if ($value -notmatch '^[^\s:]+(?:[:][0-9]+)?/[^\s]+@sha256:[a-f0-9]{64}$') {
        throw "$image must be an immutable sha256 image reference."
    }

    if (-not $placeholdersAllowed -and $value -match '(\.invalid|sha256:0{64})') {
        throw "$image still contains a non-deployable placeholder."
    }
}

$required = 'entraAuthority', 'entraTenantId', 'entraClientId', 'entraAudience', 'entraApiScope', 'entraSpaClientId',
    'allowedHosts', 'allowedOriginsCsv', 'otelExporterOtlpEndpoint', 'malwareScannerEndpoint', 'malwareScannerAudience'
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace($parameters.$name.value)) {
        throw "$name is required."
    }
}

$tenantId = $parameters.entraTenantId.value
$tenantIdIsValid = $tenantId -match '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
$authorityIsValid = $parameters.entraAuthority.value -ceq "https://login.microsoftonline.com/$tenantId/v2.0"
if (-not ($tenantIdIsValid -and $authorityIsValid)) {
    throw 'Test Entra authority must be tenant-specific and consistent with entraTenantId.'
}

$scopePrefix = "$($parameters.entraAudience.value.TrimEnd('/'))/"
$scopeIsAudienceBound = $parameters.entraApiScope.value.StartsWith($scopePrefix, [StringComparison]::Ordinal)
$scopeName = $parameters.entraApiScope.value.Substring([Math]::Min($scopePrefix.Length, $parameters.entraApiScope.value.Length))
if (-not $scopeIsAudienceBound -or [string]::IsNullOrWhiteSpace($scopeName) -or $scopeName.Contains('/')) {
    throw 'Test delegated API scope must be a single scope rooted in the configured API audience.'
}

if ($document.parameters.PSObject.Properties.Name -contains 'postgresAdministratorPassword') {
    throw 'Hardened Test parameters must not contain a PostgreSQL administrator password.'
}

if (-not $placeholdersAllowed) {
    $serialized = $document | ConvertTo-Json -Depth 20
    if ($serialized -match '(\.invalid|00000000-0000-0000-0000-000000000000)') {
        throw 'Test parameters still contain non-deployable identity or endpoint placeholders.'
    }
}

Write-Output 'Test environment parameter contract: PASS'
