# Runs one autotest: puts a test script in the mod's folder as autotest_run.txt (the mod renames it on start, so
# the next normal launch is a normal game), starts Megabonk through Steam, waits for the game to quit, then prints
# the [test] results. Refuses to run while the game is open.
#   tools\autotest.ps1 tests\monkey_climb.txt [-TimeoutSeconds 420]
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [string]$GameDir = "H:\SteamLibrary\steamapps\common\Megabonk",
    [int]$TimeoutSeconds = 420
)

$modDir = Join-Path $GameDir "BepInEx\plugins\MegabonkSplitscreen"
if (Get-Process -Name Megabonk -ErrorAction SilentlyContinue) { Write-Output "Megabonk is running - close it first."; exit 1 }
if (-not (Test-Path $Script)) { Write-Output "Test script not found: $Script"; exit 1 }

$result = Join-Path $modDir "autotest_result.txt"
if (Test-Path $result) { Remove-Item $result -Force }
Get-ChildItem $modDir -Filter "autotest_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item $Script (Join-Path $modDir "autotest_run.txt") -Force

Write-Output "Starting Megabonk (Steam app 3405340) with $Script"
Start-Process "steam://rungameid/3405340"

# Wait for the game to start, then to exit
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not (Get-Process -Name Megabonk -ErrorAction SilentlyContinue)) {
    if ((Get-Date) -gt $deadline) { Write-Output "The game never started."; exit 1 }
    Start-Sleep -Seconds 2
}
$launched = Get-Date
$modLog = Join-Path $modDir "MegabonkSplitscreen.log"
while (Get-Process -Name Megabonk -ErrorAction SilentlyContinue) {
    # Hang catcher (v0.48.4: a freeze after restarting from the death screen; the mod's own watchdog thread froze with
    # the game, so no dump): the mod's heartbeat writes the log every 2 s - 20 s of silence after it started = stuck.
    # A full dump with Windows' own comsvcs.dll MiniDump goes to H:\CrashDumps, then the test game is closed.
    $written = if (Test-Path $modLog) { (Get-Item $modLog).LastWriteTime } else { $launched }
    if ($written -gt $launched -and ((Get-Date) - $written).TotalSeconds -gt 20) {
        $proc = Get-Process -Name Megabonk | Select-Object -First 1
        $dump = "H:\CrashDumps\Megabonk.hang.$($proc.Id).dmp"
        Write-Output "The mod log has been silent for 20 s (last write $($written.ToString('HH:mm:ss'))) - writing $dump"
        & rundll32.exe C:\Windows\System32\comsvcs.dll, MiniDump $proc.Id $dump full
        $until = (Get-Date).AddSeconds(120)
        Start-Sleep -Seconds 10
        while ((Get-Date) -lt $until -and (-not (Test-Path $dump) -or ((Get-Date) - (Get-Item $dump).LastWriteTime).TotalSeconds -lt 5)) { Start-Sleep -Seconds 2 }
        if (Test-Path $dump) { Write-Output "Dump written ($([int]((Get-Item $dump).Length / 1MB)) MB) - closing the game." }
        else { Write-Output "No dump was written - closing the game." }
        $proc | Stop-Process -Force
        break
    }
    if ((Get-Date) -gt $deadline) {
        Write-Output "Timed out after $TimeoutSeconds s - closing the game."
        Get-Process -Name Megabonk -ErrorAction SilentlyContinue | Stop-Process -Force
        break
    }
    Start-Sleep -Seconds 2
}

# A script the mod never picked up (e.g. an old mod version) must not fire on the user's next normal launch
$leftover = Join-Path $modDir "autotest_run.txt"
if (Test-Path $leftover) { Remove-Item $leftover -Force; Write-Output "The mod never picked the script up (removed)." }

if (Test-Path $result) { Get-Content $result } else { Write-Output "No autotest_result.txt written." }
Get-ChildItem $modDir -Filter "autotest_*.png" -ErrorAction SilentlyContinue | ForEach-Object { "screenshot: $($_.FullName)" }
