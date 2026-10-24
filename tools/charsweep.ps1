# Runs tests\chars\<name>.txt for each character (all if none given) and keeps each run's results, screenshots and mod
# log in the output folder (autotest.ps1 clears the screenshots before every run).
#   tools\charsweep.ps1 -Out <folder> [-Chars Monke,Fox]
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [string[]]$Chars,
    [string]$GameDir = "H:\SteamLibrary\steamapps\common\Megabonk",
    [int]$CooldownSeconds = 15
)
# User (after the 2026-10-18 GPU hang + reboots during back-to-back launches): a 15 s cooldown between launches.
# The sweep also stops if the graphics driver logged an error (nvlddmkm) since it started.
$sweepStart = Get-Date
function Test-GpuErrors {
    $e = Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; StartTime = $sweepStart } -ErrorAction SilentlyContinue
    return @($e).Count
}
$modDir = Join-Path $GameDir "BepInEx\plugins\MegabonkSplitscreen"
$tests = Join-Path $PSScriptRoot "..\tests\chars"
if (-not $Chars) { $Chars = Get-ChildItem $tests -Filter *.txt | ForEach-Object { $_.BaseName } }
New-Item -ItemType Directory -Force $Out | Out-Null
$first = $true
foreach ($c in $Chars) {
    if (-not $first) { Start-Sleep -Seconds $CooldownSeconds }
    $first = $false
    $gpu = Test-GpuErrors
    if ($gpu -gt 0) { Write-Output "STOPPED before $c - $gpu graphics driver error(s) (nvlddmkm) logged since the sweep started."; break }
    $dir = Join-Path $Out $c
    New-Item -ItemType Directory -Force $dir | Out-Null
    & (Join-Path $PSScriptRoot "autotest.ps1") (Join-Path $tests "$c.txt") -TimeoutSeconds 240 | Out-File (Join-Path $dir "result.txt") -Encoding utf8
    Get-ChildItem $modDir -Filter "autotest_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $dir
    Copy-Item (Join-Path $modDir "MegabonkSplitscreen.log") $dir -ErrorAction SilentlyContinue
    Write-Output "$c done: $((Get-Content (Join-Path $dir 'result.txt') | Select-String 'statdiff|passives|kills|dmg' | ForEach-Object { $_.Line }) -join ' || ')"
}
