[CmdletBinding()]
param(
    [Parameter(Mandatory)] [uri] $ApiBaseUri,
    [Parameter(Mandatory)] [string] $AccessToken,
    [Parameter(Mandatory)] [guid] $WorkspaceId,
    [Parameter(Mandatory)] [guid] $ForeignWorkspaceId,
    [Parameter(Mandatory)] [guid] $EvidenceId,
    [string] $CrossTenantToken,
    [string] $StaleAuthorityToken,
    [string] $RevokedAuthorityToken,
    [switch] $RequireAuthorityScenarios
)

$ErrorActionPreference = 'Stop'
$base = $ApiBaseUri.ToString().TrimEnd('/')

function Assert-Denied([string] $Name, [string] $Token, [guid] $RouteWorkspace, [string] $WorkspaceHeader, [int[]] $Expected) {
    $uri = "$base/api/v1/workspaces/$($RouteWorkspace.ToString('D'))/evidence/$($EvidenceId.ToString('D'))"
    $headers = @{ Authorization = "Bearer $Token" }
    if ($WorkspaceHeader) { $headers['X-Workspace-Id'] = $WorkspaceHeader }
    $response = Invoke-WebRequest -Method Get -Uri $uri -Headers $headers -SkipHttpErrorCheck
    if ($response.StatusCode -notin $Expected) { throw "$Name returned $($response.StatusCode), expected $($Expected -join ',')." }
    [pscustomobject]@{ scenario = $Name; status = $response.StatusCode; result = 'DENIED' }
}

$results = @()
$results += Assert-Denied 'missing-workspace-context' $AccessToken $WorkspaceId '' @(400)
$results += Assert-Denied 'forged-workspace-context' $AccessToken $ForeignWorkspaceId $ForeignWorkspaceId.ToString('D') @(403)
$results += Assert-Denied 'malformed-token' 'malformed.token.value' $WorkspaceId $WorkspaceId.ToString('D') @(401)
if ($CrossTenantToken) { $results += Assert-Denied 'cross-tenant-evidence-id' $CrossTenantToken $WorkspaceId $WorkspaceId.ToString('D') @(403) }
if ($StaleAuthorityToken) { $results += Assert-Denied 'stale-authority' $StaleAuthorityToken $WorkspaceId $WorkspaceId.ToString('D') @(403) }
if ($RevokedAuthorityToken) { $results += Assert-Denied 'revoked-authority' $RevokedAuthorityToken $WorkspaceId $WorkspaceId.ToString('D') @(403) }

if ($RequireAuthorityScenarios -and (!$CrossTenantToken -or !$StaleAuthorityToken -or !$RevokedAuthorityToken)) {
    throw 'Cross-tenant, stale-authority, and revoked-authority actor tokens are mandatory for governed deployed Test validation.'
}

$results
