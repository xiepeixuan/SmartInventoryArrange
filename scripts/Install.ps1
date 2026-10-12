param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,
    [string]$DllPath = (Join-Path $PSScriptRoot '..\dist\SmartInventoryArrange.dll')
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $DllPath).Path
$mods = Join-Path $GameDir 'Mods'
$target = Join-Path $mods 'SmartInventoryArrange.dll'
$marker = Join-Path $PSScriptRoot '.installed-backup'
New-Item -ItemType Directory -Path $mods -Force | Out-Null
if (Test-Path -LiteralPath $target) {
    $backup = "$target.bak-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
    Copy-Item -LiteralPath $target -Destination $backup -Force
    Set-Content -LiteralPath $marker -Value $backup -NoNewline
}
Copy-Item -LiteralPath $source -Destination $target -Force
Write-Output "Installed: $target"
