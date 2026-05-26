[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$RuntimeIdentifier = 'win-x64',

    [ValidateNotNullOrEmpty()]
    [string]$ProjectPath,

    [ValidateNotNullOrEmpty()]
    [string]$DestinationDirectory,

    [switch]$BuildWheel,

    [ValidateNotNullOrEmpty()]
    [string]$WheelOutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RepoRoot {
    $scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $PSScriptRoot
    } else {
        Split-Path -Parent $PSCommandPath
    }

    $root = (Resolve-Path (Join-Path $scriptRoot '..')).ProviderPath
    return [System.IO.Path]::GetFullPath($root)
}

function Resolve-FullPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).ProviderPath)
    }

    return [System.IO.Path]::GetFullPath($Path)
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

function Invoke-PythonWheelBuild {
    param(
        [Parameter(Mandatory)]
        [string]$PythonProjectDirectory,

        [Parameter(Mandatory)]
        [string]$OutputDirectory
    )

    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }

    Write-Host "Building Python wheel into $OutputDirectory..."
    & python -m pip wheel --no-deps --wheel-dir $OutputDirectory $PythonProjectDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "python -m pip wheel failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-RepoRoot
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot 'MCPServer.AgentRouter.PythonBridge.Native\MCPServer.AgentRouter.PythonBridge.Native.csproj'
}

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $repoRoot 'python\src\mcpserver_agentrouter_bridge\native'
}

if ($BuildWheel -and [string]::IsNullOrWhiteSpace($WheelOutputDirectory)) {
    $WheelOutputDirectory = Join-Path $repoRoot 'python\dist'
}

$projectPath = Resolve-FullPath -Path $ProjectPath
$destinationDirectoryPath = if (Test-Path -LiteralPath $DestinationDirectory) {
    Resolve-FullPath -Path $DestinationDirectory
} else {
    Resolve-FullPath -Path (New-Item -ItemType Directory -Path $DestinationDirectory -Force).FullName
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

if ($BuildWheel) {
    $pythonProjectDirectory = Resolve-FullPath -Path (Join-Path $repoRoot 'python')
    $wheelOutputDirectoryPath = if (Test-Path -LiteralPath $WheelOutputDirectory) {
        Resolve-FullPath -Path $WheelOutputDirectory
    } else {
        Resolve-FullPath -Path (New-Item -ItemType Directory -Path $WheelOutputDirectory -Force).FullName
    }
    Assert-UnderRoot -Path $pythonProjectDirectory -Root $repoRoot -Label 'Python project'
    Assert-UnderRoot -Path $wheelOutputDirectoryPath -Root $repoRoot -Label 'Wheel output'
    Invoke-PythonWheelBuild -PythonProjectDirectory $pythonProjectDirectory -OutputDirectory $wheelOutputDirectoryPath
}
