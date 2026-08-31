[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResourceGroupName,

    [Parameter(Mandatory)]
    [string] $JobName,

    [Parameter(Mandatory)]
    [string] $ExecutionName,

    [Parameter(Mandatory)]
    [string] $ExpectedImageReference,

    [Parameter(Mandatory)]
    [string] $ExpectedLastAppliedMigration,

    [Parameter(Mandatory)]
    [string] $ParametersPath,

    [Parameter(Mandatory)]
    [string] $OutputParametersPath,

    [Parameter(Mandatory)]
    [string] $EvidencePath,

    [string] $Subscription
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ExpectedImageReference -notmatch '@sha256:[a-fA-F0-9]{64}$') {
    throw 'ExpectedImageReference must be an immutable digest-addressed image.'
}

if ([string]::IsNullOrWhiteSpace($ExpectedLastAppliedMigration)) {
    throw 'ExpectedLastAppliedMigration is required.'
}

$executionArguments = @(
    'containerapp', 'job', 'execution', 'show',
    '--resource-group', $ResourceGroupName,
    '--name', $JobName,
    '--job-execution-name', $ExecutionName,
    '--output', 'json',
    '--only-show-errors'
)
if (-not [string]::IsNullOrWhiteSpace($Subscription)) {
    $executionArguments += @('--subscription', $Subscription)
}

$executionJson = & az @executionArguments
if ($LASTEXITCODE -ne 0) {
    throw "Unable to retrieve Container Apps migration execution '$ExecutionName'."
}

$execution = $executionJson | ConvertFrom-Json -Depth 100
$executionStatus = if ($null -ne $execution.properties.status) { $execution.properties.status } else { $execution.status }
if ($executionStatus -cne 'Succeeded') {
    throw "Migration execution '$ExecutionName' is not Succeeded. Current status: '$executionStatus'."
}

$executionNameMatches = $execution.name -ceq $ExecutionName
$executionIdExists = -not [string]::IsNullOrWhiteSpace($execution.id)
$executionIdMatches = $execution.id -match "/jobs/$([regex]::Escape($JobName))/executions/$([regex]::Escape($ExecutionName))$"
if (-not ($executionNameMatches -and $executionIdExists -and $executionIdMatches)) {
    throw 'Migration execution identity does not match the requested job and execution.'
}

$migrationContainer = @($execution.properties.template.containers) |
    Where-Object { $_.name -ceq 'migrate' } |
    Select-Object -First 1
if ($null -eq $migrationContainer -or $migrationContainer.image -cne $ExpectedImageReference) {
    throw "Migration execution image does not match the approved immutable image '$ExpectedImageReference'."
}

$logArguments = @(
    'containerapp', 'job', 'logs', 'show',
    '--resource-group', $ResourceGroupName,
    '--name', $JobName,
    '--execution', $ExecutionName,
    '--container', 'migrate',
    '--tail', '1000',
    '--format', 'text'
)
if (-not [string]::IsNullOrWhiteSpace($Subscription)) {
    $logArguments += @('--subscription', $Subscription)
}

$logs = & az @logArguments
if ($LASTEXITCODE -ne 0) {
    throw "Unable to retrieve deterministic result evidence for migration execution '$ExecutionName'."
}

$resultPrefix = 'DLS_MIGRATION_RESULT:'
$resultLine = @($logs) |
    Where-Object { $_.Contains($resultPrefix, [StringComparison]::Ordinal) } |
    Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($resultLine)) {
    throw 'Migration execution logs do not contain deterministic DLS_MIGRATION_RESULT evidence.'
}

$resultJson = $resultLine.Substring($resultLine.IndexOf($resultPrefix, [StringComparison]::Ordinal) + $resultPrefix.Length)
$migrationResult = $resultJson | ConvertFrom-Json -Depth 20
$schemaVersionMatches = $migrationResult.schemaVersion -eq 1
$resultStatusMatches = $migrationResult.status -ceq 'Succeeded'
$resultImageMatches = $migrationResult.imageReference -ceq $ExpectedImageReference
$lastMigrationMatches = $migrationResult.lastAppliedMigration -ceq $ExpectedLastAppliedMigration
if (-not ($schemaVersionMatches -and $resultStatusMatches -and $resultImageMatches -and $lastMigrationMatches)) {
    throw 'Migration result evidence does not match the approved image and expected database migration.'
}

$evidence = [ordered]@{
    executionResourceId = [string] $execution.id
    executionName = $ExecutionName
    status = 'Succeeded'
    imageDigest = $ExpectedImageReference
    lastAppliedMigration = $ExpectedLastAppliedMigration
    completedAt = [string] $migrationResult.completedAt
}
$canonicalEvidence = $evidence | ConvertTo-Json -Compress
$evidenceHash = ([Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonicalEvidence)))).ToLowerInvariant()
$evidence['evidenceSha256'] = "sha256:$evidenceHash"

$parameters = Get-Content -LiteralPath $ParametersPath -Raw | ConvertFrom-Json -Depth 100
if ($null -eq $parameters.parameters.migrationVerification) {
    throw 'The parameter document does not declare parameters.migrationVerification.'
}

$parameters.parameters.migrationVerification.value = [pscustomobject] $evidence
$parameters.parameters.deployApplications.value = $true
$parameters | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputParametersPath -Encoding utf8NoBOM
([pscustomobject] $evidence) | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $EvidencePath -Encoding utf8NoBOM

Write-Output "Verified migration execution '$ExecutionName'. Generated '$OutputParametersPath' and '$EvidencePath'."
