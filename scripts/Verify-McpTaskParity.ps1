param()
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$required = @(
    "MCPServer.Application/Mcp/Handlers/TasksListHandler.cs",
    "MCPServer.Application/Mcp/Handlers/TasksGetHandler.cs",
    "MCPServer.Application/Mcp/Handlers/TasksResultHandler.cs",
    "MCPServer.Application/Mcp/Handlers/TasksCancelHandler.cs",
    "MCPServer.Application/Mcp/McpTaskRegistry.cs",
    "MCPServer.Domain/Mcp/TaskModels.cs"
)
foreach($path in $required){ if(-not (Test-Path (Join-Path $root $path))){ throw "Missing required task parity file: $path" } }
Write-Host "MCP task parity files present." -ForegroundColor Green
