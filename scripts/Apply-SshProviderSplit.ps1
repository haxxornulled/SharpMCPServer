$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "MCPServer.slnx"))) {
    throw "Could not locate MCPServer.slnx from $repoRoot. Run this script from the extracted repository scripts folder."
}

$legacyDirs = @(
    "MCPServer.Tools.Ssh\Configuration",
    "MCPServer.Tools.Ssh\Infrastructure",
    "MCPServer.Tools.Ssh\Interfaces",
    "MCPServer.Tools.Ssh\Models",
    "MCPServer.Tools.Ssh\Services",
    "MCPServer.Tools.Ssh\Stores"
)

foreach ($relative in $legacyDirs) {
    $path = Join-Path $repoRoot $relative
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "Removed legacy SSH provider directory: $relative"
    }
}

Write-Host "SSH provider split cleanup complete."
