param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$errors = @()

function Add-Error([string]$message) {
    $script:errors += $message
}

$hostProgram = Join-Path $Root 'MCPServer.Host/Program.cs'
$sidecarProgram = Join-Path $Root 'MCPServer.Host.Sidecar/Program.cs'

if (Test-Path $hostProgram) {
    $hostText = Get-Content $hostProgram -Raw

    if ($hostText -match 'RegisterModule\(new\s+AgentRouterApplicationModule') {
        Add-Error 'Host Program.cs should not register AgentRouterApplicationModule directly.'
    }

    if ($hostText -match 'RegisterModule\(new\s+AgentRouterInfrastructureModule') {
        Add-Error 'Host Program.cs should not register AgentRouterInfrastructureModule directly.'
    }

    if ($hostText -match 'RegisterModule\(new\s+AgentRouterHostingModule') {
        Add-Error 'Host Program.cs should not register AgentRouterHostingModule directly.'
    }

    if ($hostText -notmatch 'McpServerHostRuntimeModule') {
        Add-Error 'Host Program.cs should compose through McpServerHostRuntimeModule.'
    }
}

if (Test-Path $sidecarProgram) {
    $sidecarText = Get-Content $sidecarProgram -Raw

    if ($sidecarText -match 'new\s+DefaultSshPathResolver\(') {
        Add-Error 'Sidecar Program.cs should not construct DefaultSshPathResolver directly.'
    }

    if ($sidecarText -match 'new\s+StaticOptionsMonitor<SshToolSettings>\(') {
        Add-Error 'Sidecar Program.cs should not construct StaticOptionsMonitor<SshToolSettings> directly.'
    }

    if ($sidecarText -notmatch 'SshHostSidecarRuntimeFactory') {
        Add-Error 'Sidecar Program.cs should use SshHostSidecarRuntimeFactory.'
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Host composition boundary verification passed.' -ForegroundColor Green
