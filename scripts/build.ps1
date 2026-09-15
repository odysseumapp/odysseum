$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $repoRoot 'server/Odysseum.Server/Odysseum.Server.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
