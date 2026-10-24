# Hang catcher for normal play (v0.48.4: the game froze after a death-screen restart, and Windows writes no dump for
# a hang). Start it before or while playing; it waits for Megabonk, and if the mod's log (heartbeat every 2 s) goes
# silent for 20 s while the game is open, it writes a full dump to H:\CrashDumps with Windows' own comsvcs.dll.
# It does not close the game. Stops by itself when the game exits.
#   powershell -ExecutionPolicy Bypass -File tools\hangwatch.ps1
param([string]$GameDir = "H:\SteamLibrary\steamapps\common\Megabonk")

$modLog = Join-Path $GameDir "BepInEx\plugins\MegabonkSplitscreen\MegabonkSplitscreen.log"
Write-Output "Waiting for Megabonk..."
while (-not (Get-Process -Name Megabonk -ErrorAction SilentlyContinue)) { Start-Sleep -Seconds 2 }
$proc = Get-Process -Name Megabonk | Select-Object -First 1
$started = Get-Date
Write-Output "Watching Megabonk (pid $($proc.Id))"
$dumped = $false
while (-not $proc.HasExited) {
    Start-Sleep -Seconds 2
    $written = if (Test-Path $modLog) { (Get-Item $modLog).LastWriteTime } else { $started }
    if (-not $dumped -and $written -gt $started -and ((Get-Date) - $written).TotalSeconds -gt 20) {
        $dump = "H:\CrashDumps\Megabonk.hang.$($proc.Id).dmp"
        Write-Output "$(Get-Date -Format HH:mm:ss) the mod log has been silent for 20 s - writing $dump (the game may stay frozen a minute)"
        & rundll32.exe C:\Windows\System32\comsvcs.dll, MiniDump $proc.Id $dump full
        $dumped = $true
    }
}
Write-Output "Megabonk exited."
if ($dumped) { Write-Output "Dump: H:\CrashDumps\Megabonk.hang.$($proc.Id).dmp" }
