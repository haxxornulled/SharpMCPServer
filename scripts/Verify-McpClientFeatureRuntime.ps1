param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$requiredFiles = @(
    "MCPServer.Infrastructure/Mcp/Stdio/StdioMcpClientFeatureTransport.cs",
    "MCPServer.Application/Mcp/Tools/ClientSampleTool.cs",
    "MCPServer.Application/Mcp/Tools/ClientElicitFormTool.cs",
    "MCPServer.Application/Mcp/Tools/ClientElicitUrlTool.cs",
    "MCPServer.Domain/Mcp/SamplingModels.cs",
    "MCPServer.Domain/Mcp/ElicitationModels.cs",
    "MCPServer.Domain/Mcp/TaskNotificationModels.cs"
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $RepoRoot $file
    if (-not (Test-Path $path)) {
        throw "Missing client-feature runtime file: $file"
    }
}

$capabilityFile = Join-Path $RepoRoot "MCPServer.Domain/Mcp/ClientCapabilityModels.cs"
$capabilityText = Get-Content $capabilityFile -Raw
if ($capabilityText -notmatch 'tasks\.requests\.sampling\.createMessage' -or $capabilityText -notmatch 'tasks\.requests\.elicitation\.create') {
    throw "Client capability parsing is missing nested task request support for sampling.createMessage and elicitation.create."
}

Write-Host "MCP client feature runtime surface verification passed."
