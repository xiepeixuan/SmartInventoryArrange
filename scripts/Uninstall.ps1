param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir
)
$ErrorActionPreference = 'Stop'
$target = Join-Path (Join-Path $GameDir 'Mods') 'SmartInventoryArrange.dll'
$marker = Join-Path $PSScriptRoot '.installed-backup'
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
if (Test-Path -LiteralPath $marker) {
    $backup = (Get-Content -LiteralPath $marker -Raw).Trim()
    if ($backup -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $target -Force }
    Remove-Item -LiteralPath $marker -Force
}
Write-Output "Rollback complete: $target"
