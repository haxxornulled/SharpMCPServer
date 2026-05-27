[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [int]$TimeoutSeconds = 300,

    [string]$SmokeDirectory = $(if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'mcpserver-wheel-smoke' } else { Join-Path $env:TEMP 'mcpserver-wheel-smoke' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Heartbeat {
    param(
        [Parameter(Mandatory)]
        [string]$Message
    )

    Write-Host $Message
}

New-Item -ItemType Directory -Force -Path $SmokeDirectory | Out-Null
Write-Host "Starting Python wheel smoke test in '$SmokeDirectory'..."
Write-Host "Python version: $(& python -V 2>&1)"
Push-Location $SmokeDirectory
try {
    Remove-Item Env:MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY -ErrorAction SilentlyContinue
    $smokeScriptPath = Join-Path $SmokeDirectory 'wheel-smoke.py'
    Set-Content -LiteralPath $smokeScriptPath -Encoding utf8 -Value @'
from mcpserver_agentrouter_bridge import AgentRouterBridge
with AgentRouterBridge() as bridge:
    response = bridge.run({
        "objective": "review the workspace",
        "metadata": {"agent.workflowMode": "deterministic"},
    })
print(response["status"])
print(response["message"])
'@

    $stdoutPath = Join-Path $SmokeDirectory 'wheel-smoke.stdout.log'
    $stderrPath = Join-Path $SmokeDirectory 'wheel-smoke.stderr.log'
    Remove-Item $stdoutPath, $stderrPath -ErrorAction SilentlyContinue

    $process = Start-Process -FilePath python -ArgumentList @('-u', ('"{0}"' -f $smokeScriptPath)) -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $nextHeartbeat = (Get-Date).AddSeconds(15)

    while (-not $process.HasExited) {
        if ((Get-Date) -ge $deadline) {
            try {
                $process.Kill($true)
            }
            catch {
                Write-Host "Failed to terminate the smoke process after timeout: $($_.Exception.Message)"
            }

            throw "Timed out waiting for the Python wheel smoke test after $TimeoutSeconds second(s)."
        }

        if ((Get-Date) -ge $nextHeartbeat) {
            Write-Heartbeat "Python wheel smoke test still running..."
            $nextHeartbeat = (Get-Date).AddSeconds(15)
        }

        Start-Sleep -Milliseconds 500
    }

    if ($process.ExitCode -ne 0) {
        $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
        $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
        if ($stdout) {
            Write-Host 'Python wheel smoke stdout:'
            Write-Host $stdout
        }
        if ($stderr) {
            Write-Host 'Python wheel smoke stderr:'
            Write-Host $stderr
        }

        throw "Python wheel smoke test failed with exit code $($process.ExitCode)."
    }

    $stdout = if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { '' }
    $stderr = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { '' }
    if ($stdout) {
        Write-Host 'Python wheel smoke stdout:'
        Write-Host $stdout
    }
    if ($stderr) {
        Write-Host 'Python wheel smoke stderr:'
        Write-Host $stderr
    }

    Write-Host "Python wheel smoke test completed successfully."
}
finally {
    Pop-Location
}
