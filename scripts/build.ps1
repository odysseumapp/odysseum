param([string]$WebRoot = '')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'runtime.ps1')
$WebRoot = Get-OdysseumWebRoot $repoRoot $WebRoot
$nodeDirectory = Get-OdysseumNodeDirectory $repoRoot -Install

$previousPath = $env:PATH
try {
    $env:PATH = $nodeDirectory + [IO.Path]::PathSeparator + $previousPath
    Push-Location $WebRoot
    try {
        Write-Host 'Installing UI dependencies...'
        & (Join-Path $nodeDirectory 'npm.cmd') ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        Write-Host 'Building Nuxt UI...'
        & (Join-Path $nodeDirectory 'npm.cmd') run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }

    Write-Host 'Building API...'
    dotnet build (Join-Path $repoRoot 'server/Odysseum.Server/Odysseum.Server.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    Write-Host "Built API and UI. Frontend output: $(Join-Path $WebRoot '.output')"
} finally { $env:PATH = $previousPath }
