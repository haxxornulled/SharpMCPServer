[CmdletBinding()]
param(
    [string]$SmokeDirectory = $(if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'mcpserver-wheel-smoke' } else { Join-Path $env:TEMP 'mcpserver-wheel-smoke' })
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $SmokeDirectory | Out-Null
Push-Location $SmokeDirectory
try {
    Remove-Item Env:MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY -ErrorAction SilentlyContinue
    @'
from mcpserver_agentrouter_bridge import AgentRouterBridge
with AgentRouterBridge() as bridge:
    response = bridge.run({
        "objective": "review the workspace",
        "metadata": {"agent.workflowMode": "deterministic"},
    })
print(response["status"])
print(response["message"])
'@ | python -
}
finally {
    Pop-Location
}
