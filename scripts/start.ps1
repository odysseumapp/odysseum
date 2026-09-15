param([int]$Port = 5080, [string]$Workspace = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$Workspace) { $Workspace = Join-Path $repoRoot 'workspace' }
$env:ODYSSEUM_WORKSPACE = [System.IO.Path]::GetFullPath($Workspace)
$env:ODYSSEUM_DEMO = 'true'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project (Join-Path $repoRoot 'server/Odysseum.Server/Odysseum.Server.csproj') --no-build --no-launch-profile -- --urls "http://127.0.0.1:$Port"
