param([string]$WebRoot = '', [string]$Runtime = 'win-x64', [switch]$Unpacked, [switch]$SkipUi)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'runtime.ps1')
$nodeDirectory = Get-OdysseumNodeDirectory $repoRoot -Install
$npm = Join-Path $nodeDirectory 'npm.cmd'
$serverProject = Join-Path $repoRoot 'server/Odysseum.Server/Odysseum.Server.csproj'
$desktopRoot = Join-Path $repoRoot 'desktop'
$archive = Join-Path $repoRoot 'webui.zip'
$publishDirectory = Join-Path $repoRoot '.cache/desktop-publish'
$architecture = if ($Runtime -like '*arm64') { '--arm64' } else { '--x64' }

$previousPath = $env:PATH
try {
    $env:PATH = $nodeDirectory + [IO.Path]::PathSeparator + $previousPath

    if (!$SkipUi) {
        $WebRoot = Get-OdysseumWebRoot $repoRoot $WebRoot
        Push-Location $WebRoot
        try {
            Write-Host 'Installing UI dependencies...'
            & $npm ci --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
            Write-Host 'Building static UI...'
            & $npm run build
            if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
            & $npm run package -- $archive
            if ($LASTEXITCODE -ne 0) { throw 'Frontend packaging failed.' }
        } finally { Pop-Location }
    }
    if (!(Test-Path -LiteralPath $archive)) { throw "No UI archive at '$archive'. Run again without -SkipUi." }

    Write-Host "Publishing the server for $Runtime..."
    dotnet publish $serverProject -c Release -r $Runtime --self-contained -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }

    $sidecar = Join-Path $desktopRoot 'server'
    if (Test-Path -LiteralPath $sidecar) { Remove-Item -LiteralPath $sidecar -Recurse -Force }
    New-Item -ItemType Directory -Path $sidecar | Out-Null
    Copy-Item (Join-Path $publishDirectory 'Odysseum.Server.exe') $sidecar

    Push-Location $desktopRoot
    try {
        Write-Host 'Installing desktop dependencies...'
        & $npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Desktop dependency installation failed.' }
        Write-Host 'Building the desktop app...'
        if ($Unpacked) { & $npm run dist -- $architecture --dir } else { & $npm run dist -- $architecture }
        if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    } finally { Pop-Location }

    Write-Host "Built the desktop app in $(Join-Path $desktopRoot 'dist'). Check it with: cd desktop; npm run smoke"
} finally { $env:PATH = $previousPath }
