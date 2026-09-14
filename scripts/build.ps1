$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $repoRoot 'web')
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location }
$webRoot = Join-Path $repoRoot 'server/Odysseum.Server/wwwroot'
New-Item -ItemType Directory -Force -Path $webRoot | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'web/dist/*') -Destination $webRoot -Recurse -Force
dotnet build (Join-Path $repoRoot 'server/Odysseum.Server/Odysseum.Server.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
