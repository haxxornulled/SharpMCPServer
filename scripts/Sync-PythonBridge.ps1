[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$RuntimeIdentifier = 'win-x64',

    [ValidateNotNullOrEmpty()]
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\MCPServer.AgentRouter.PythonBridge.Native\MCPServer.AgentRouter.PythonBridge.Native.csproj'),

    [ValidateNotNullOrEmpty()]
    [string]$DestinationDirectory = (Join-Path $PSScriptRoot '..\python\src\mcpserver_agentrouter_bridge\native')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RepoRoot {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    return [System.IO.Path]::GetFullPath($root)
}

function Assert-UnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Label
    )

    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    if (-not $Path.StartsWith($Root, $comparison)) {
        throw "$Label path '$Path' must stay within repo root '$Root'."
    }
}

function Resolve-PublishArtifact {
    param(
        [Parameter(Mandatory)]
        [string]$PublishDirectory
    )

    $candidateNames = @(
        'MCPServer.AgentRouter.PythonBridge.Native.dll',
        'MCPServer.AgentRouter.PythonBridge.Native.so',
        'libMCPServer.AgentRouter.PythonBridge.Native.so',
        'MCPServer.AgentRouter.PythonBridge.Native.dylib',
        'libMCPServer.AgentRouter.PythonBridge.Native.dylib'
    )

    foreach ($candidateName in $candidateNames) {
        $candidatePath = Join-Path $PublishDirectory $candidateName
        if (Test-Path -LiteralPath $candidatePath) {
            return (Resolve-Path -LiteralPath $candidatePath).Path
        }
    }

    $availableFiles = Get-ChildItem -LiteralPath $PublishDirectory -File | Select-Object -ExpandProperty Name
    throw "Could not find the NativeAOT shared library in '$PublishDirectory'. Available files: $($availableFiles -join ', ')"
}

$repoRoot = Resolve-RepoRoot
$projectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$destinationDirectoryPath = if (Test-Path -LiteralPath $DestinationDirectory) {
    (Resolve-Path -LiteralPath $DestinationDirectory).Path
} else {
    (New-Item -ItemType Directory -Path $DestinationDirectory -Force | Resolve-Path).Path
}

Assert-UnderRoot -Path $projectPath -Root $repoRoot -Label 'Project'
Assert-UnderRoot -Path $destinationDirectoryPath -Root $repoRoot -Label 'Destination'

Write-Host "Publishing $projectPath for $RuntimeIdentifier ($Configuration)..."
& dotnet publish $projectPath -c $Configuration -r $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$projectDirectory = Split-Path -Path $projectPath -Parent
$publishDirectory = Join-Path $projectDirectory "bin\$Configuration\net10.0\$RuntimeIdentifier\publish"
if (-not (Test-Path -LiteralPath $publishDirectory)) {
    throw "Publish directory '$publishDirectory' was not created."
}

$artifactPath = Resolve-PublishArtifact -PublishDirectory $publishDirectory
$artifactName = Split-Path -Path $artifactPath -Leaf
$destinationPath = Join-Path $destinationDirectoryPath $artifactName

Copy-Item -LiteralPath $artifactPath -Destination $destinationPath -Force
Write-Host "Copied $artifactName to $destinationPath"
