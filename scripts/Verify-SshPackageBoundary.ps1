param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$errors = @()
function Add-Error([string]$message) { $script:errors += $message }

$toolsRoot = Join-Path $Root 'MCPServer.Tools.Ssh'
$providerRoot = Join-Path $Root 'MCPServer.Ssh'

if (Test-Path $toolsRoot) {
    $text = Get-ChildItem $toolsRoot -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.json', '.txt', '.xml' } |
        Get-Content -Raw

    foreach ($pattern in @('SqliteSshCredentialVault','DefaultSshCredentialResolver','SshExecutionPolicy','SshExecutionService','SshAgentRuntime')) {
        if ($text -match $pattern) {
            Add-Error "Tools.Ssh should remain an MCP adapter surface and must not own provider runtime/storage concern '$pattern'."
        }
    }
}

if (Test-Path $providerRoot) {
    $text = Get-ChildItem $providerRoot -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.json', '.txt', '.xml' } |
        Get-Content -Raw

    if ($text -match 'IAgentPlugin') {
        Add-Error 'MCPServer.Ssh should not know about generic agent plugin abstractions.'
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'SSH package boundary verification passed.' -ForegroundColor Green
