[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$evidencePath = './tests/e2e/Invoke-EvidenceJourney.ps1'
$hostilePath = './tests/e2e/Invoke-HostileIsolationTests.ps1'
$readmePath = './tests/e2e/README.md'

foreach ($path in $evidencePath, $hostilePath) {
    $source = Get-Content -LiteralPath $path -Raw
    [scriptblock]::Create($source) | Out-Null
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
