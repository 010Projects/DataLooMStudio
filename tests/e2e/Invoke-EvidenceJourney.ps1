[CmdletBinding()]
param(
    [Parameter(Mandatory)] [uri] $ApiBaseUri,
    [Parameter(Mandatory)] [string] $AccessToken,
    [Parameter(Mandatory)] [guid] $WorkspaceId,
    [Parameter(Mandatory)] [string] $FilePath,
    [string] $OutputPath = './artifacts/e2e/evidence-journey.json'
)

$ErrorActionPreference = 'Stop'
$headers = @{ Authorization = "Bearer $AccessToken"; 'X-Workspace-Id' = $WorkspaceId.ToString('D'); Accept = 'application/json' }
$file = Get-Item -LiteralPath $FilePath
$hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$root = "$($ApiBaseUri.ToString().TrimEnd('/'))/api/v1/workspaces/$($WorkspaceId.ToString('D'))/evidence"

function Invoke-DlsJson([string] $Method, [string] $Uri, [object] $Body) {
    $requestHeaders = $headers.Clone()
    $requestHeaders['Idempotency-Key'] = [guid]::NewGuid().ToString('D')
    $arguments = @{ Method = $Method; Uri = $Uri; Headers = $requestHeaders; ContentType = 'application/json' }
    if ($null -ne $Body) { $arguments.Body = $Body | ConvertTo-Json -Depth 10 }
    Invoke-RestMethod @arguments
}

$registration = Invoke-DlsJson POST $root @{
    evidenceType = 'Document'; classification = 'Internal'; originalFileName = $file.Name
    mediaType = 'application/octet-stream'; declaredSize = $file.Length; contentHash = $hash
    storageObjectReference = "pending/$([guid]::NewGuid().ToString('D'))"; retentionPolicyKey = 'default'
}
$allocation = Invoke-DlsJson POST "$root/$($registration.evidenceId)/upload-allocation" @{}
Invoke-WebRequest -Method Put -Uri $allocation.uploadAuthority -InFile $file.FullName -Headers @{ 'x-ms-blob-type' = 'BlockBlob'; 'Content-Type' = 'application/octet-stream' } | Out-Null
$receipt = Invoke-DlsJson POST "$root/$($registration.evidenceId)/versions/$($registration.versionId)/content-received" @{
    storageObjectReference = $allocation.storageObjectReference
}
if ($receipt.lifecycleState -ne 'Available' -or $receipt.scanOutcome -ne 'Clean') { throw 'Evidence did not pass integrity and malware validation.' }
$summary = Invoke-RestMethod -Method Get -Uri "$root/$($registration.evidenceId)" -Headers $headers
$review = Invoke-DlsJson POST "$root/$($registration.evidenceId)/versions/$($registration.versionId)/reviews" @{ reviewKind = 'Standard' }

$evidence = [ordered]@{
    executedAt = [DateTimeOffset]::UtcNow; api = $ApiBaseUri; workspaceId = $WorkspaceId
    evidenceId = $summary.evidenceId; versionId = $summary.versionId; lineageId = $summary.lineageId
    lifecycleState = $summary.lifecycleState; verificationStatus = $summary.verificationStatus
    scanOutcome = $receipt.scanOutcome; reviewState = $review.state; sha256 = $summary.sha256Hash
}
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$evidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath
$evidence
