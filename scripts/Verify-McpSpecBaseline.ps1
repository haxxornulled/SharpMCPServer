param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$oldVersionPatterns = @('2025-06-18', '2025-09-03', '2024-11-05')
$legacyPhasePatterns = @('phase1', 'Phase 1', 'phase 1', '0.1.0-phase1')
$include = @('*.cs', '*.csproj', '*.props', '*.targets', '*.ps1', '*.md', '*.json', '*.yml', '*.yaml')
$excludeDirs = @('bin', 'obj', 'TestResults', '.git')

$files = Get-ChildItem -Path $Root -Recurse -File -Include $include | Where-Object {
    $full = $_.FullName
    foreach ($dir in $excludeDirs) {
        if ($full -match "[\\/]$dir[\\/]") { return $false }
    }
    return $true
}

$issues = @()
foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw
    foreach ($pattern in $oldVersionPatterns + $legacyPhasePatterns) {
        if ($content -match [regex]::Escape($pattern)) {
            $issues += [pscustomobject]@{ File = $file.FullName; Pattern = $pattern }
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Error ("Spec/version drift detected:`n" + (($issues | Sort-Object File, Pattern | ForEach-Object { "- $($_.Pattern) :: $($_.File)" }) -join "`n"))
}

Write-Host 'MCP spec baseline verification passed.' -ForegroundColor Green
