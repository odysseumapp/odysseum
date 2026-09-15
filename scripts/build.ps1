param([string]$WebRoot = '')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$WebRoot) { $WebRoot = $env:ODYSSEUM_WEB_PATH }
if (!$WebRoot) { $WebRoot = Join-Path $repoRoot 'odysseum-web' }
if (![IO.Path]::IsPathRooted($WebRoot)) { $WebRoot = Join-Path $repoRoot $WebRoot }
$WebRoot = [IO.Path]::GetFullPath($WebRoot)
if (!(Test-Path -LiteralPath (Join-Path $WebRoot 'package.json'))) {
    throw "Frontend checkout not found at '$WebRoot'. Pass -WebRoot with the location of odysseum-web."
}

function Test-NuxtNode([string]$NodePath) {
    if (!(Test-Path -LiteralPath $NodePath)) { return $false }
    $nodeVersionText = & $NodePath --version
    if ($LASTEXITCODE -ne 0) { return $false }
    $nodeVersion = [version]($nodeVersionText -replace '^v', '')
    return ($nodeVersion.Major -eq 22 -and $nodeVersion -ge [version]'22.19.0') -or
        ($nodeVersion.Major -eq 24 -and $nodeVersion -ge [version]'24.11.0') -or $nodeVersion.Major -ge 26
}

# Use a compatible installed Node, or keep Node 24 beside the project without changing the machine's installation.
$nodeCommand = Get-Command node -ErrorAction SilentlyContinue
$nodeDirectory = if ($nodeCommand -and (Test-NuxtNode $nodeCommand.Source)) { Split-Path -Parent $nodeCommand.Source } else { '' }
if (!$nodeDirectory) {
    $nodeVersion = '24.21.0'
    $nodeArchitecture = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
    $nodePackage = "node-v$nodeVersion-win-$nodeArchitecture"
    $cacheDirectory = Join-Path $repoRoot '.cache'
    $nodeDirectory = Join-Path $cacheDirectory $nodePackage
    if (!(Test-NuxtNode (Join-Path $nodeDirectory 'node.exe'))) {
        Write-Host "Downloading project-local Node $nodeVersion..."
        New-Item -ItemType Directory -Force -Path $cacheDirectory | Out-Null
        $archive = Join-Path $cacheDirectory "$nodePackage.zip"
        Invoke-WebRequest -UseBasicParsing -Uri "https://nodejs.org/dist/v$nodeVersion/$nodePackage.zip" -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $cacheDirectory -Force
        if (!(Test-NuxtNode (Join-Path $nodeDirectory 'node.exe'))) { throw 'Could not prepare Node for Nuxt.' }
    }
}

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
