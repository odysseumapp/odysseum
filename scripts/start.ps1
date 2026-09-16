param(
    [ValidateRange(1, 65535)][int]$Port = 5080,
    [string]$Workspace = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$serverRoot = Join-Path $repoRoot 'server/Odysseum.Server'
$serverDll = Join-Path $serverRoot 'bin/Debug/net10.0/Odysseum.Server.dll'
if (!(Test-Path -LiteralPath $serverDll)) { throw 'Build output is missing. Run ./scripts/build.ps1 first.' }
if (!(Test-Path -LiteralPath (Join-Path $serverRoot 'webui/current.json'))) {
    throw 'The web UI is not installed. Run ./scripts/build.ps1 first.'
}
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
try { $probe.Start() }
catch { throw "Port $Port is unavailable. Stop the existing app or choose another -Port." }
finally { $probe.Stop() }

if (!$Workspace) { $Workspace = Join-Path $repoRoot 'workspace' }
$url = "http://127.0.0.1:$Port"
$environment = @{
    ODYSSEUM_WORKSPACE = [IO.Path]::GetFullPath($Workspace)
    ODYSSEUM_DEMO = if ($env:ODYSSEUM_DEMO) { $env:ODYSSEUM_DEMO } else { 'true' }
    ASPNETCORE_ENVIRONMENT = 'Development'
}
$previousEnvironment = @{}
foreach ($name in $environment.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
}

# One .NET process serves the API and the installed static UI. Ctrl+C stops it.
Push-Location $serverRoot
try {
    Write-Host "Odysseum: http://localhost:$Port"
    Write-Host "API docs: http://localhost:$Port/scalar"
    dotnet $serverDll --urls $url
} finally {
    Pop-Location
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
}
