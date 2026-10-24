# Leak sweep (user: test different character combos so nothing leaks between the two players).
# For each mixed pair A+B: P1's whole stat table must equal P1's in an A+A run, P2's must equal P2's in a B+B run;
# both players' health is watched with enemies cleared (no hits: nothing should change it by itself).
# Runs one game launch per test with a 15 s cooldown and stops on any graphics-driver error (charsweep's rules).
#   tools\leaksweep.ps1 -Out <folder> [-CompareOnly]
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [switch]$CompareOnly,
    [string]$GameDir = "H:\SteamLibrary\steamapps\common\Megabonk",
    [int]$CooldownSeconds = 15
)
$pairs = @(@('Cl4nk','Calcium'), @('Calcium','Cl4nk'), @('Megachad','Vlad'), @('Athena','Robinette'), @('Ogre','Fox'), @('Noelle','Bush'))
$singles = ($pairs | ForEach-Object { $_ }) | Sort-Object -Unique
$runs = @($pairs | ForEach-Object { ,@($_[0], $_[1]) }) + @($singles | ForEach-Object { ,@($_, $_) })
$tests = Join-Path $PSScriptRoot "..\tests\leak"
$modDir = Join-Path $GameDir "BepInEx\plugins\MegabonkSplitscreen"
New-Item -ItemType Directory -Force $tests, $Out | Out-Null

if (-not $CompareOnly) {
    $sweepStart = Get-Date
    $first = $true
    foreach ($r in $runs) {
        $name = "$($r[0])_$($r[1])"
        $script = @("# Leak check: P1 $($r[0]), P2 $($r[1]) - stat tables + health, no enemies", "chars $($r[0]) $($r[1])", "wait 2", "open", "wait 1", "clear", "clearloot", "statdump t0", "health")
        for ($i = 0; $i -lt 6; $i++) { $script += @("clear", "wait 3") }
        $script += @("health", "statdump t20", "quit")
        Set-Content -Path (Join-Path $tests "$name.txt") -Value $script -Encoding ascii
        if (-not $first) { Start-Sleep -Seconds $CooldownSeconds }
        $first = $false
        $gpu = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; StartTime = $sweepStart } -ErrorAction SilentlyContinue).Count
        if ($gpu -gt 0) { Write-Output "STOPPED before $name - $gpu graphics driver error(s)"; break }
        $dir = Join-Path $Out $name
        New-Item -ItemType Directory -Force $dir | Out-Null
        & (Join-Path $PSScriptRoot "autotest.ps1") (Join-Path $tests "$name.txt") -TimeoutSeconds 240 | Out-File (Join-Path $dir "result.txt") -Encoding utf8
        Copy-Item (Join-Path $modDir "MegabonkSplitscreen.log") $dir -ErrorAction SilentlyContinue
        Write-Output "$name done"
    }
}

# ---------- Compare ----------
function Table($name, $player, $label) {
    $f = Join-Path $Out "$name\result.txt"
    if (-not (Test-Path $f)) { return $null }
    $line = (Get-Content $f | Select-String "statdump $label $player " | Select-Object -First 1).Line
    if (-not $line) { return $null }
    $t = @{}
    foreach ($kv in ($line -split "statdump $label $player ", 2)[1] -split ';') { $k, $v = $kv -split '=', 2; $t[$k] = [double]$v }
    return $t
}
function Compare-StatTables($a, $b) {
    if ($a -eq $null -or $b -eq $null) { return "missing data" }
    $d = @()
    foreach ($k in $a.Keys) { if ([math]::Abs($a[$k] - $b[$k]) -gt 0.0005 + 0.001 * [math]::Abs($b[$k])) { $d += "$k $($a[$k]) (alone: $($b[$k]))" } }
    if ($d.Count -eq 0) { return "same" } else { return ($d -join ', ') }
}
foreach ($p in $pairs) {
    $name = "$($p[0])_$($p[1])"
    "== P1 $($p[0]) + P2 $($p[1])"
    foreach ($label in 't0', 't20') {
        "   $label P1 vs $($p[0]) alone: $(Compare-StatTables (Table $name 'P1' $label) (Table "$($p[0])_$($p[0])" 'P1' $label))"
        "   $label P2 vs $($p[1]) alone: $(Compare-StatTables (Table $name 'P2' $label) (Table "$($p[1])_$($p[1])" 'P2' $label))"
    }
    $h = Get-Content (Join-Path $Out "$name\result.txt") -ErrorAction SilentlyContinue | Select-String 'health:' | ForEach-Object { ($_.Line -split 'health: ', 2)[1] }
    "   health: $($h -join '  ->  ')"
}
