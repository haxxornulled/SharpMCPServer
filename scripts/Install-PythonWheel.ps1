[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$WheelDirectory = $(Join-Path (Get-Location) 'python\dist')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-FirstWheel {
    param(
        [Parameter(Mandatory)]
        [string]$Directory
    )

    if (-not (Test-Path -LiteralPath $Directory)) {
        throw "Wheel directory '$Directory' does not exist."
    }

    $wheel = Get-ChildItem -LiteralPath $Directory -Filter '*.whl' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $wheel) {
        $available = Get-ChildItem -LiteralPath $Directory -File | Select-Object -ExpandProperty Name
        throw "No Python wheel was found in '$Directory'. Available files: $($available -join ', ')"
    }

    return $wheel
}

Write-Host "Locating Python wheel in '$WheelDirectory'..."
$pythonVersion = & python -V 2>&1
Write-Host "Python version: $pythonVersion"
Write-Host "Pip version: $(& python -m pip --version)"
$wheel = Resolve-FirstWheel -Directory $WheelDirectory
Write-Host "Installing wheel '$($wheel.FullName)'..."

& python -m pip install --no-input --disable-pip-version-check --force-reinstall --no-deps $wheel.FullName
if ($LASTEXITCODE -ne 0) {
    throw "python -m pip install failed with exit code $LASTEXITCODE."
}

Write-Host "Installed wheel '$($wheel.Name)' successfully."
