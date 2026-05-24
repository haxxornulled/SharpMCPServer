[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sln = Join-Path $repoRoot "MCPServer.slnx"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [scriptblock] $ScriptBlock
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $ScriptBlock
}

function Assert-NoGeneratedArtifacts {
    $generated = Get-ChildItem -Path $repoRoot -Recurse -Force -Directory |
        Where-Object { $_.Name -in @("bin", "obj", ".vs", "TestResults") }

    if ($generated.Count -gt 0) {
        $paths = $generated | ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1) }
        throw "Generated artifacts are present in the source tree:`n$($paths -join "`n")"
    }
}

function Assert-NoStaleBoundaryFiles {
    $staleFiles = @(
        "MCPServer.Application\Mcp\IMcpLoggingState.cs",
        "MCPServer.Application\Mcp\IMcpMethodHandler.cs",
        "MCPServer.Application\Mcp\IMcpRequestDispatcher.cs",
        "MCPServer.Application\Mcp\IMcpRequestExecutionRegistry.cs",
        "MCPServer.Application\Mcp\IMcpResource.cs",
        "MCPServer.Application\Mcp\IMcpResourceRegistry.cs",
        "MCPServer.Application\Mcp\IMcpSessionState.cs",
        "MCPServer.Application\Mcp\IMcpTool.cs",
        "MCPServer.Application\Mcp\IMcpToolArgumentValidator.cs",
        "MCPServer.Application\Mcp\IMcpToolRegistry.cs",
        "MCPServer.Application\Mcp\JsonRpc\IJsonRpcMessageParser.cs",
        "MCPServer.Application\Mcp\JsonRpc\IJsonRpcResponseSerializer.cs",
        "MCPServer.Application\Mcp\McpToolArgumentValidator.cs",
        "MCPServer.Infrastructure\Mcp\JsonRpc\IJsonRpcMessageParser.cs",
        "MCPServer.Infrastructure\Mcp\JsonRpc\IJsonRpcResponseSerializer.cs"
    )

    $found = foreach ($relativePath in $staleFiles) {
        $path = Join-Path $repoRoot $relativePath
        if (Test-Path $path) { $relativePath }
    }

    if ($found.Count -gt 0) {
        throw "Stale boundary files still exist:`n$($found -join "`n")"
    }
}

function Assert-NoHiddenCompileMasks {
    $projectFiles = Get-ChildItem -Path $repoRoot -Recurse -Filter "*.csproj"
    $offenders = foreach ($projectFile in $projectFiles) {
        $text = Get-Content -Raw -Path $projectFile.FullName
        if ($text -match '<Compile\s+Remove=') {
            $projectFile.FullName.Substring($repoRoot.Length + 1)
        }
    }

    if ($offenders.Count -gt 0) {
        throw "Project files contain Compile Remove masks that can hide source drift:`n$($offenders -join "`n")"
    }
}

function Assert-NoConsoleProtocolLeakage {
    $sourceFiles = Get-ChildItem -Path $repoRoot -Recurse -Filter "*.cs" |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)\\' -and
            $_.FullName -notmatch '\\MCPServer.Client.Console\\' -and
            $_.FullName -notmatch '\\MCPServer.Host.Sidecar\\'
        }

    $violations = foreach ($file in $sourceFiles) {
        $matches = Select-String -Path $file.FullName -Pattern 'Console\.(Write|WriteLine|Error|Out)' -SimpleMatch:$false
        foreach ($match in $matches) {
            if ($match.Line -notmatch 'Console\.OpenStandard(Input|Output)') {
                "$($file.FullName.Substring($repoRoot.Length + 1)):$($match.LineNumber): $($match.Line.Trim())"
            }
        }
    }

    if ($violations.Count -gt 0) {
        throw "Potential stdout/stderr protocol leakage found:`n$($violations -join "`n")"
    }
}

Push-Location $repoRoot
try {
    Invoke-Step "Check source tree hygiene" { Assert-NoGeneratedArtifacts }
    Invoke-Step "Check clean architecture stale-file boundary" { Assert-NoStaleBoundaryFiles }
    Invoke-Step "Check project files do not hide stale source" { Assert-NoHiddenCompileMasks }
    Invoke-Step "Check stdio stdout discipline" { Assert-NoConsoleProtocolLeakage }

    if (-not $SkipRestore) {
        Invoke-Step "dotnet restore" { dotnet restore $sln }
    }

    Invoke-Step "dotnet build" { dotnet build $sln -c $Configuration --no-restore }
    Invoke-Step "dotnet test" { dotnet test $sln -c $Configuration --no-build }

    Write-Host "Phase 1 verification passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
