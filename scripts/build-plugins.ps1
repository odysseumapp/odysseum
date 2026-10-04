param([string]$Configuration = 'Debug', [string]$PluginsDir = '')

# Builds every project under plugins/ into a plugins folder, one subfolder per plugin. Without -PluginsDir the folder is
# the one next to the server's build output (server/Odysseum.Server/bin/<Configuration>/net10.0/plugins), which the
# server loads by default.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$arguments = @('-c', $Configuration, '--nologo')
if ($PluginsDir) {
    $PluginsDir = [IO.Path]::GetFullPath($PluginsDir).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $arguments += "-p:OdysseumPluginsDir=$PluginsDir"
}
foreach ($project in Get-ChildItem -Path (Join-Path $repoRoot 'plugins') -Filter '*.csproj' -Recurse) {
    Write-Host "Building plugin $($project.BaseName)..."
    dotnet build $project.FullName @arguments
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed: $($project.BaseName)" }
}
