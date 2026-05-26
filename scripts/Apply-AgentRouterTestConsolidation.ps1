param(
    [switch] $WhatIfOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$requiredProjects = @(
    'MCPServer.AgentRouter.Tests\MCPServer.AgentRouter.Tests.csproj',
    'MCPServer.AgentRouter.IntegrationTests\MCPServer.AgentRouter.IntegrationTests.csproj'
)

foreach ($project in $requiredProjects) {
    if (-not (Test-Path $project)) {
        throw "Required consolidated test project is missing: $project. Expand the consolidation patch before running cleanup."
    }
}

$retiredPaths = @(
    'MCPServer.AgentRouter.Domain.Tests',
    'MCPServer.AgentRouter.Application.Tests',
    'MCPServer.AgentRouter.Infrastructure.Tests',
    'MCPServer.AgentRouter.Hosting.Tests',
    'MCPServer.AgentRouter.Ssh.Tests',
    'MCPServer.AgentRouter.Application.Tests_DEFAULT_TMP.cs'
)

foreach ($path in $retiredPaths) {
    if (Test-Path $path) {
        if ($WhatIfOnly) {
            Write-Host "Would remove $path"
            continue
        }

        Remove-Item $path -Recurse -Force
        Write-Host "Removed $path"
    }
}

Write-Host 'AgentRouter test portfolio cleanup complete.'
Write-Host 'Run:'
Write-Host '  dotnet test .\MCPServer.AgentRouter.Tests\MCPServer.AgentRouter.Tests.csproj'
Write-Host '  dotnet test .\MCPServer.AgentRouter.IntegrationTests\MCPServer.AgentRouter.IntegrationTests.csproj'
Write-Host '  dotnet build .\MCPServer.slnx'
