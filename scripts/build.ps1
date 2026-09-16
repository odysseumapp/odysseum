param([string]$WebRoot = '')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'runtime.ps1')
$WebRoot = Get-OdysseumWebRoot $repoRoot $WebRoot
$nodeDirectory = Get-OdysseumNodeDirectory $repoRoot -Install
$serverRoot = Join-Path $repoRoot 'server/Odysseum.Server'
$archive = Join-Path $repoRoot '.cache/odysseum-webui.zip'

$previousPath = $env:PATH
try {
    $env:PATH = $nodeDirectory + [IO.Path]::PathSeparator + $previousPath
    Push-Location $WebRoot
    try {
        Write-Host 'Installing UI dependencies...'
        & (Join-Path $nodeDirectory 'npm.cmd') ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        Write-Host 'Building static UI...'
        & (Join-Path $nodeDirectory 'npm.cmd') run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        & (Join-Path $nodeDirectory 'npm.cmd') run package -- $archive
        if ($LASTEXITCODE -ne 0) { throw 'Frontend packaging failed.' }
    } finally { Pop-Location }

    Write-Host 'Building API...'
    dotnet build (Join-Path $serverRoot 'Odysseum.Server.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }

    # The API keeps installed UI releases beside its settings file, so install from the server directory.
    Write-Host 'Installing UI release into the API...'
    Push-Location $serverRoot
    try {
        dotnet (Join-Path $serverRoot 'bin/Debug/net10.0/Odysseum.Server.dll') --install-webui $archive
        if ($LASTEXITCODE -ne 0) { throw 'UI installation failed.' }
    } finally { Pop-Location }
    Write-Host "Built API and UI. Start with ./scripts/start.ps1"
} finally { $env:PATH = $previousPath }
