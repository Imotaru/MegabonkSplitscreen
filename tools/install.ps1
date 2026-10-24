# Copies the built plugin into the game's BepInEx\plugins\MegabonkSplitscreen folder and verifies it by hash.
# Refuses to run while the game is open.
param(
    [string]$GameDir = "H:\SteamLibrary\steamapps\common\Megabonk",
    [string]$Config = "Release"
)

$src = Join-Path $PSScriptRoot "..\src\MegabonkSplitscreen\bin\$Config\net6.0\MegabonkSplitscreen.dll"
$dstDir = Join-Path $GameDir "BepInEx\plugins\MegabonkSplitscreen"

if (Get-Process -Name Megabonk -ErrorAction SilentlyContinue) { Write-Output "Megabonk is running - close it first."; exit 1 }
if (-not (Test-Path $src)) { Write-Output "Build output not found: $src"; exit 1 }

New-Item -ItemType Directory -Force $dstDir | Out-Null
Copy-Item $src $dstDir -Force
$dst = Join-Path $dstDir "MegabonkSplitscreen.dll"

$a = (Get-FileHash $src).Hash
$b = (Get-FileHash $dst).Hash
if ($a -ne $b) { Write-Output "HASH MISMATCH after copy"; exit 1 }
Write-Output "Installed $dst"
Write-Output "SHA256 $b"
