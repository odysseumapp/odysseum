param([string]$Configuration = 'Debug', [string]$PluginsDir = '')

# Builds every project under plugins/ into a plugins folder, one subfolder per plugin. Without -PluginsDir the folder is
# the one next to the server's build output (server/Odysseum.Server/bin/<Configuration>/net10.0/plugins), which the
# server loads by default. A plugin with a client/package.json gets its web UI module built first (into its wwwroot),
# with the project-local Node when the installed one is too old.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'runtime.ps1')
$arguments = @('-c', $Configuration, '--nologo')
if ($PluginsDir) {
    $PluginsDir = [IO.Path]::GetFullPath($PluginsDir).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $arguments += "-p:OdysseumPluginsDir=$PluginsDir"
}
foreach ($project in Get-ChildItem -Path (Join-Path $repoRoot 'plugins') -Filter '*.csproj' -Recurse) {
    $client = Join-Path $project.DirectoryName 'client'
    if (Test-Path -LiteralPath (Join-Path $client 'package.json')) {
        Write-Host "Building the client of plugin $($project.BaseName)..."
        $nodeDirectory = Get-OdysseumNodeDirectory $repoRoot -Install
        $npm = Join-Path $nodeDirectory 'npm.cmd'
        $previousPath = $env:PATH
        $env:PATH = $nodeDirectory + [IO.Path]::PathSeparator + $previousPath
        Push-Location $client
        try {
            if (Test-Path -LiteralPath 'package-lock.json') { & $npm ci --no-audit --no-fund } else { & $npm install --no-audit --no-fund }
            if ($LASTEXITCODE -ne 0) { throw "npm install failed: $($project.BaseName)" }
            & $npm run build
            if ($LASTEXITCODE -ne 0) { throw "Client build failed: $($project.BaseName)" }
        }
        finally { Pop-Location; $env:PATH = $previousPath }
    }
    Write-Host "Building plugin $($project.BaseName)..."
    dotnet build $project.FullName @arguments
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed: $($project.BaseName)" }
}
