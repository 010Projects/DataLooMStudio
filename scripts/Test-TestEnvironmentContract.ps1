[CmdletBinding()]
param(
    [string] $ParametersFile = './infra/environments/test/main.parameters.example.json',
    [switch] $AllowPlaceholders,
    [switch] $InfrastructureBootstrap
)

$ErrorActionPreference = 'Stop'
$document = Get-Content -LiteralPath $ParametersFile -Raw | ConvertFrom-Json -Depth 20
$parameters = $document.parameters
foreach ($name in 'deployMigrationJob', 'deployApplications', 'migrationSuccessEvidence') {
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
    $migrationEvidence = $parameters.migrationSuccessEvidence.value
    $invalidMigrationEvidence = [string]::IsNullOrWhiteSpace($migrationEvidence) -or $migrationEvidence -match '(placeholder|changeme|\.invalid)'
    if ($invalidMigrationEvidence) {
        throw 'Application deployment requires exact successful migration execution evidence.'
    }
}

$bootstrapHasWorkloads = $deployMigrationJob -or $deployApplications -or -not [string]::IsNullOrWhiteSpace($parameters.migrationSuccessEvidence.value)
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

$required = 'entraAuthority', 'entraClientId', 'entraAudience', 'entraApiScope', 'entraSpaClientId',
    'allowedHosts', 'allowedOriginsCsv', 'otelExporterOtlpEndpoint', 'malwareScannerEndpoint', 'malwareScannerAudience'
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace($parameters.$name.value)) {
        throw "$name is required."
    }
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
