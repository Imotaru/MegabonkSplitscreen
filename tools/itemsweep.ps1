# Item sweep (user: every chest reward on both players must be the same): runs tests\items\items_*.txt, one game launch
# each, 15 s cooldown, stops on any graphics-driver (nvlddmkm) error; keeps each part's results, log and screenshots
# (item rows, one per item with P2 holding it: fx_<item>) in -Out and prints the icheck lines.
#   tools\itemsweep.ps1 -Out <folder> [-Parts 1,2,3,4]
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [int[]]$Parts = @(1, 2, 3, 4),
    [string]$GameDir = "H:\SteamLibrary\steamapps\common\Megabonk",
    [int]$CooldownSeconds = 15
)
$modDir = Join-Path $GameDir "BepInEx\plugins\MegabonkSplitscreen"
New-Item -ItemType Directory -Force $Out | Out-Null
$sweepStart = Get-Date
$first = $true
foreach ($n in $Parts) {
    $test = Join-Path $PSScriptRoot "..\tests\items\items_$n.txt"
    if (-not $first) { Start-Sleep -Seconds $CooldownSeconds }
    $first = $false
    $gpu = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; StartTime = $sweepStart } -ErrorAction SilentlyContinue).Count
    if ($gpu -gt 0) { Write-Output "STOP: $gpu graphics-driver error(s) since the sweep started"; break }
    Write-Output "=== part $n ==="
    & (Join-Path $PSScriptRoot "autotest.ps1") $test -TimeoutSeconds 900 | Out-Null
    $res = Join-Path $modDir "autotest_result.txt"
    if (Test-Path $res) {
        Copy-Item $res (Join-Path $Out "items_$n.txt") -Force
        Copy-Item (Join-Path $modDir "MegabonkSplitscreen.log") (Join-Path $Out "items_$n.log") -Force
        Get-ChildItem $modDir -Filter "autotest_*.png" | Where-Object { $_.Name -match 'items_rows|fx_' } | Copy-Item -Destination $Out -Force
        Get-Content $res | Where-Object { $_ -match 'icheck|ERROR|unknown command|usage' }
    } else { Write-Output "part ${n}: no result" }
}
