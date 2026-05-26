param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$errors = @()
function Add-Error([string]$message) { $script:errors += $message }

$executionAbstractions = Join-Path $Root 'MCPServer.Execution.Abstractions'
$executionPluginSsh = Join-Path $Root 'MCPServer.ExecutionPlugins.Ssh'
$agentRouterRoots = @(
    'MCPServer.AgentRouter.Abstractions',
    'MCPServer.AgentRouter.Domain',
    'MCPServer.AgentRouter.Application',
    'MCPServer.AgentRouter.Infrastructure',
    'MCPServer.AgentRouter.Hosting'
) | ForEach-Object { Join-Path $Root $_ }

if (Test-Path $executionAbstractions) {
    $text = Get-ChildItem $executionAbstractions -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.json', '.txt', '.xml' } |
        Get-Content -Raw

    foreach ($pattern in @('Ssh','SSH','Vault','Credential','PathResolver','SshToolSettings','SshCredentialReference')) {
        if ($text -match $pattern) {
            Add-Error "Execution abstractions should remain provider-neutral and must not contain '$pattern'."
        }
    }
}

foreach ($root in $agentRouterRoots) {
    if (-not (Test-Path $root)) { continue }
    $text = Get-ChildItem $root -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.json', '.txt', '.xml' } |
        Get-Content -Raw

    foreach ($pattern in @('ExecutionPlugins\.Ssh','MCPServer\.Ssh','SshAgentPlugin','SshAgentRuntime')) {
        if ($text -match $pattern) {
            Add-Error "AgentRouter core should not depend on SSH execution plugin/provider details ($pattern)."
        }
    }
}

if (Test-Path $executionPluginSsh) {
    $text = Get-ChildItem $executionPluginSsh -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.json', '.txt', '.xml' } |
        Get-Content -Raw

    if ($text -notmatch 'MCPServer\.Execution\.Abstractions') {
        Add-Error 'ExecutionPlugins.Ssh should depend on MCPServer.Execution.Abstractions.'
    }

    if ($text -notmatch 'MCPServer\.Ssh') {
        Add-Error 'ExecutionPlugins.Ssh should depend on MCPServer.Ssh.'
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Execution boundary verification passed.' -ForegroundColor Green
