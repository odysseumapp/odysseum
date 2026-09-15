param(
    [ValidateRange(1, 65535)][int]$Port = 5080,
    [string]$Workspace = '',
    [string]$WebRoot = '',
    [ValidateRange(0, 65535)][int]$ApiPort = 0
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'runtime.ps1')
$WebRoot = Get-OdysseumWebRoot $repoRoot $WebRoot
$nodeDirectory = Get-OdysseumNodeDirectory $repoRoot
$serverRoot = Join-Path $repoRoot 'server/Odysseum.Server'
$serverDll = Join-Path $serverRoot 'bin/Debug/net10.0/Odysseum.Server.dll'
$webEntry = Join-Path $WebRoot '.output/server/index.mjs'
if (!(Test-Path -LiteralPath $serverDll) -or !(Test-Path -LiteralPath $webEntry)) {
    throw 'Build output is missing. Run ./scripts/build.ps1 first.'
}
if (!$ApiPort) { $ApiPort = if ($Port -lt 65535) { $Port + 1 } else { $Port - 1 } }
if ($Port -eq $ApiPort) { throw 'The UI and API must use different ports.' }
foreach ($listenPort in @($Port, $ApiPort)) {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $listenPort)
    try { $probe.Start() }
    catch { throw "Port $listenPort is unavailable. Stop the existing app or choose another -Port / -ApiPort." }
    finally { $probe.Stop() }
}

if (!$Workspace) { $Workspace = Join-Path $repoRoot 'workspace' }
$runLogs = Join-Path $repoRoot ".cache/run-$PID"
New-Item -ItemType Directory -Force -Path $runLogs | Out-Null
$apiUrl = "http://127.0.0.1:$ApiPort"
$webUrl = "http://127.0.0.1:$Port"
$processes = @()
$previousEnvironment = @{}
$environment = @{
    ODYSSEUM_WORKSPACE = [IO.Path]::GetFullPath($Workspace)
    ODYSSEUM_DEMO = if ($env:ODYSSEUM_DEMO) { $env:ODYSSEUM_DEMO } else { 'true' }
    ASPNETCORE_ENVIRONMENT = 'Development'
    NUXT_API_ORIGIN = $apiUrl
    HOST = '127.0.0.1'
    PORT = "$Port"
    NITRO_HOST = '127.0.0.1'
    NITRO_PORT = "$Port"
}
foreach ($name in $environment.Keys) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Assert-Running {
    foreach ($child in $processes) {
        $child.Refresh()
        if ($child.HasExited) { throw "A service stopped with exit code $($child.ExitCode). Check the logs in $runLogs." }
    }
}

try {
    foreach ($name in $environment.Keys) { [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process') }
    $processes += Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"' + $serverDll + '"'), '--urls', $apiUrl) -WorkingDirectory $serverRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runLogs 'api.log') -RedirectStandardError (Join-Path $runLogs 'api-errors.log')
    $processes += Start-Process -FilePath (Join-Path $nodeDirectory 'node.exe') -ArgumentList ('"' + $webEntry + '"') -WorkingDirectory $WebRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runLogs 'web.log') -RedirectStandardError (Join-Path $runLogs 'web-errors.log')

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    foreach ($url in @($apiUrl, $webUrl)) {
        $ready = $false
        while (!$ready) {
            Assert-Running
            if ([DateTime]::UtcNow -ge $deadline) { throw "Startup timed out. Check the logs in $runLogs." }
            try { $ready = (Invoke-WebRequest -UseBasicParsing -Uri "$url/health" -TimeoutSec 1).StatusCode -eq 200 }
            catch { if ([DateTime]::UtcNow -ge $deadline) { throw "Startup timed out. Check the logs in $runLogs." } }
            if (!$ready) { Start-Sleep -Milliseconds 200 }
        }
    }
    Write-Host "Odysseum: http://localhost:$Port"
    Write-Host "API docs: http://localhost:$ApiPort/scalar"
    Write-Host "Logs: $runLogs"
    Write-Host 'Press Ctrl+C to stop both services.'
    while ($true) { Assert-Running; Start-Sleep -Milliseconds 500 }
} finally {
    foreach ($child in $processes) {
        if (!$child.HasExited) { Stop-Process -Id $child.Id -ErrorAction SilentlyContinue }
        $child.Dispose()
    }
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
}
