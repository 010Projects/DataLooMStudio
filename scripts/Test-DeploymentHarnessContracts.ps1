[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$evidencePath = './tests/e2e/Invoke-EvidenceJourney.ps1'
$hostilePath = './tests/e2e/Invoke-HostileIsolationTests.ps1'
$readmePath = './tests/e2e/README.md'
$migrationGatePath = './scripts/Confirm-TestMigrationExecution.ps1'

foreach ($path in $evidencePath, $hostilePath) {
    $source = Get-Content -LiteralPath $path -Raw
    [scriptblock]::Create($source) | Out-Null
}

$migrationGate = Get-Content -LiteralPath $migrationGatePath -Raw
[scriptblock]::Create($migrationGate) | Out-Null
foreach ($required in "'execution', 'show'", "'--job-execution-name'", "'job', 'logs', 'show'",
    'DLS_MIGRATION_RESULT:', 'ExpectedImageReference', 'ExpectedLastAppliedMigration', 'migrationVerification') {
    if (-not $migrationGate.Contains($required, [StringComparison]::Ordinal)) {
        throw "Migration verification gate is missing required contract: $required"
    }
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $temporaryBase "dls-migration-gate-$([Guid]::NewGuid().ToString('N'))"))
if (-not $temporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Migration gate test path escaped the operating-system temporary directory.'
}
$approvedImage = "test.azurecr.io/dls-migrate@sha256:$('a' * 64)"
$expectedMigration = '20260830122707_SecurityRemediationEvidenceImmutability'
$jobName = 'dls-test-migrate'
$executionName = 'dls-test-migrate-000001'
$executionId = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/dls-test/providers/Microsoft.App/jobs/$jobName/executions/$executionName"
$inputParameters = Join-Path $temporaryRoot 'input.parameters.json'
$outputParameters = Join-Path $temporaryRoot 'verified.parameters.json'
$migrationEvidence = Join-Path $temporaryRoot 'migration-evidence.json'

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $parameterDocument = Get-Content -LiteralPath './infra/environments/test/main.parameters.example.json' -Raw | ConvertFrom-Json -Depth 100
    $parameterDocument.parameters.migrationContainerImage.value = $approvedImage
    $parameterDocument | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $inputParameters -Encoding utf8NoBOM

    $global:DlsMigrationGateMock = [pscustomobject]@{
        Image = $approvedImage
        Migration = $expectedMigration
        JobName = $jobName
        ExecutionName = $executionName
        ExecutionId = $executionId
    }
    function global:az {
        $global:LASTEXITCODE = 0
        if ($args[2] -ceq 'execution') {
            return [pscustomobject]@{
                name = $global:DlsMigrationGateMock.ExecutionName
                id = $global:DlsMigrationGateMock.ExecutionId
                properties = [pscustomobject]@{
                    status = 'Succeeded'
                    template = [pscustomobject]@{
                        containers = @([pscustomobject]@{
                            name = 'migrate'
                            image = $global:DlsMigrationGateMock.Image
                        })
                    }
                }
            } | ConvertTo-Json -Depth 20
        }

        if ($args[2] -ceq 'logs') {
            $result = [ordered]@{
                schemaVersion = 1
                status = 'Succeeded'
                appliedMigrationCount = 1
                lastAppliedMigration = $global:DlsMigrationGateMock.Migration
                imageReference = $global:DlsMigrationGateMock.Image
                completedAt = '2026-08-30T12:00:00+00:00'
            } | ConvertTo-Json -Compress
            return "DLS_MIGRATION_RESULT:$result"
        }

        throw "Unexpected mocked Azure CLI invocation: $args"
    }

    & $migrationGatePath `
        -ResourceGroupName 'dls-test' `
        -JobName $jobName `
        -ExecutionName $executionName `
        -ExpectedImageReference $approvedImage `
        -ExpectedLastAppliedMigration $expectedMigration `
        -ParametersPath $inputParameters `
        -OutputParametersPath $outputParameters `
        -EvidencePath $migrationEvidence | Out-Null

    $verified = Get-Content -LiteralPath $outputParameters -Raw | ConvertFrom-Json -Depth 100
    $deploymentWasEnabled = [bool] $verified.parameters.deployApplications.value
    $executionEvidenceMatches = $verified.parameters.migrationVerification.value.executionResourceId -ceq $executionId
    $imageEvidenceMatches = $verified.parameters.migrationVerification.value.imageDigest -ceq $approvedImage
    $migrationEvidenceMatches = $verified.parameters.migrationVerification.value.lastAppliedMigration -ceq $expectedMigration
    $evidenceHashIsValid = $verified.parameters.migrationVerification.value.evidenceSha256 -match '^sha256:[a-f0-9]{64}$'
    if (-not ($deploymentWasEnabled -and $executionEvidenceMatches -and $imageEvidenceMatches -and $migrationEvidenceMatches -and $evidenceHashIsValid)) {
        throw 'Migration verification gate did not emit the required immutable deployment evidence.'
    }
}
finally {
    Remove-Item -LiteralPath Function:\global:az -ErrorAction SilentlyContinue
    Remove-Variable -Name DlsMigrationGateMock -Scope Global -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$evidence = Get-Content -LiteralPath $evidencePath -Raw
foreach ($required in 'Authorization = "Bearer $AccessToken"', "'X-Workspace-Id'", 'upload-allocation',
    'content-received', "scanOutcome -ne 'Clean'", 'lineageId', 'reviewState') {
    if (-not $evidence.Contains($required, [StringComparison]::Ordinal)) {
        throw "Evidence journey is missing required contract: $required"
    }
}

$hostile = Get-Content -LiteralPath $hostilePath -Raw
foreach ($required in 'missing-workspace-context', 'forged-workspace-context', 'malformed-token',
    'cross-tenant-evidence-id', 'stale-authority', 'revoked-authority', 'RequireAuthorityScenarios') {
    if (-not $hostile.Contains($required, [StringComparison]::Ordinal)) {
        throw "Hostile isolation harness is missing required scenario: $required"
    }
}

$readme = Get-Content -LiteralPath $readmePath -Raw
$hasLocalContract = $readme.Contains('Local and CI integration validation', [StringComparison]::Ordinal)
$hasDeployedContract = $readme.Contains('Deployed Test validation', [StringComparison]::Ordinal)
if (-not $hasLocalContract -or -not $hasDeployedContract) {
    throw 'The E2E contract must distinguish local/integration evidence from deployed Test evidence.'
}

Write-Output 'Deployment harness contracts: PASS'
