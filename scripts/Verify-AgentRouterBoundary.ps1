param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$targets = @(
    'MCPServer.AgentRouter.Abstractions',
    'MCPServer.AgentRouter.Domain',
    'MCPServer.AgentRouter.Application',
    'MCPServer.AgentRouter.Infrastructure',
    'MCPServer.AgentRouter.Hosting'
) | ForEach-Object { Join-Path $Root $_ }

$patterns = @(
    'Ssh',
    'SSH',
    'Credential',
    'Vault',
    'PathResolver',
    'ToolAdapter',
    'SshToolSettings',
    'SshCredentialReference'
)

$hits = @()

foreach ($target in $targets) {
    if (-not (Test-Path $target)) {
        continue
    }

    Get-ChildItem -Path $target -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.json', '.txt', '.xml' } |
        ForEach-Object {
            $file = $_.FullName
            $content = Get-Content -Path $file -Raw
            foreach ($pattern in $patterns) {
                if ($content -match $pattern) {
                    $hits += [pscustomobject]@{
                        File = $file.Substring($Root.Length).TrimStart('\','/')
                        Pattern = $pattern
                    }
                }
            }
        }
}

if ($hits.Count -eq 0) {
    Write-Host 'AgentRouter boundary verification passed: no provider-specific nouns found in core packages.' -ForegroundColor Green
    exit 0
}

$hits | Sort-Object File, Pattern | Format-Table -AutoSize
Write-Error 'AgentRouter boundary verification failed: provider-specific nouns were found in core packages.'
exit 1
