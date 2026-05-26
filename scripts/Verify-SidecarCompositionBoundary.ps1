param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$errors = @()

function Add-Error([string]$message) {
    $script:errors += $message
}

$sidecarProgram = Join-Path $Root 'MCPServer.Host.Sidecar/Program.cs'
$sidecarFactory = Join-Path $Root 'MCPServer.Host.Sidecar/Composition/SshHostSidecarRuntimeFactory.cs'
$sidecarModule = Join-Path $Root 'MCPServer.Host.Sidecar/Composition/SshHostSidecarRuntimeModule.cs'

if (Test-Path $sidecarProgram) {
    $sidecarText = Get-Content $sidecarProgram -Raw

    if ($sidecarText -match 'new\s+DefaultSshPathResolver\(') {
        Add-Error 'Sidecar Program.cs should not construct DefaultSshPathResolver directly.'
    }

    if ($sidecarText -match 'new\s+StaticOptionsMonitor<SshToolSettings>\(') {
        Add-Error 'Sidecar Program.cs should not construct StaticOptionsMonitor<SshToolSettings> directly.'
    }

    if ($sidecarText -notmatch 'SshHostSidecarRuntimeFactory') {
        Add-Error 'Sidecar Program.cs should compose through SshHostSidecarRuntimeFactory.'
    }
}

if (Test-Path $sidecarFactory) {
    $factoryText = Get-Content $sidecarFactory -Raw

    if ($factoryText -notmatch 'using\s+Autofac;') {
        Add-Error 'SshHostSidecarRuntimeFactory.cs should import Autofac.'
    }

    if ($factoryText -notmatch 'ResolveProfileStoreDisplayPath\(ISshPathResolver\s+pathResolver,\s+SshToolSettings\s+settings\)') {
        Add-Error 'SshHostSidecarRuntimeFactory.cs should expose the path-resolver overload for display-path resolution.'
    }

    if ($factoryText -notmatch 'ResolveProfileStoreDisplayPath\(SshToolSettings\s+settings\)') {
        Add-Error 'SshHostSidecarRuntimeFactory.cs should retain the one-argument display-path overload for CLI paths.'
    }

    if ($factoryText -notmatch 'new\s+ContainerBuilder\(') {
        Add-Error 'SshHostSidecarRuntimeFactory.cs should own sidecar runtime container creation.'
    }
}

if (Test-Path $sidecarModule) {
    $moduleText = Get-Content $sidecarModule -Raw

    if ($moduleText -notmatch 'SshHostSidecarRuntimeModule') {
        Add-Error 'SshHostSidecarRuntimeModule.cs should define the sidecar-owned runtime module.'
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Sidecar composition boundary verification passed.' -ForegroundColor Green
