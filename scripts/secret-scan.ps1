$ErrorActionPreference = 'Stop'

$patterns = @(
    'BEGIN (RSA|OPENSSH|DSA|EC|PGP) PRIVATE KEY',
    'AccountKey=',
    'DefaultEndpointsProtocol=.*AccountKey',
    'client_secret',
    'password\s*=\s*["'']',
    'api[_-]?key\s*=\s*["'']',
    'AWS_SECRET_ACCESS_KEY',
    'AZURE_CLIENT_SECRET',
    'ConnectionStrings__.*=.*(Password|AccountKey)'
)

$searchRoots = @(
    'src',
    'infra',
    'docs',
    'governance',
    '.github',
    'README.md',
    'azure.yaml',
    'DataLooMStudio.slnx',
    'Directory.Build.props',
    'global.json'
)

$findings = New-Object System.Collections.Generic.List[string]

foreach ($pattern in $patterns) {
    $output = & git grep -nE -- $pattern -- $searchRoots 2>$null

    if ($LASTEXITCODE -eq 0) {
        foreach ($line in $output) {
            $findings.Add($line)
        }
    }
    elseif ($LASTEXITCODE -gt 1) {
        throw "Secret scan failed while evaluating pattern: $pattern"
    }
}

if ($findings.Count -gt 0) {
    Write-Error "Potential secret material found:`n$($findings -join [Environment]::NewLine)"
    exit 1
}

Write-Host 'Secret scan completed without findings.'
