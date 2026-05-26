param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$checks = @(
    'Verify-AgentRouterBoundary.ps1',
    'Verify-HostCompositionBoundary.ps1',
    'Verify-SidecarCompositionBoundary.ps1',
    'Verify-ExecutionBoundary.ps1',
    'Verify-SshPackageBoundary.ps1'
)

foreach ($check in $checks) {
    & (Join-Path $PSScriptRoot $check) -Root $Root
}

Write-Host 'All boundary verifiers passed.' -ForegroundColor Green

& (Join-Path $PSScriptRoot 'Verify-McpSpecBaseline.ps1')

& (Join-Path $PSScriptRoot "Verify-McpTaskParity.ps1")

& (Join-Path $PSScriptRoot "Verify-McpClientFeatureRuntime.ps1") -RepoRoot $Root
