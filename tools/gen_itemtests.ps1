# Writes tests\items\items_<n>.txt: every chest item (EItem) on P1 alone, then on P2 alone, in the same scenario;
# 'icheck <item>' compares the two (ItemCheck.cs). ~22 items per file (one game launch each).
# User (v0.49): the players far apart so they don't interfere, enemies only where the item needs them (they push the
# players around), the players kept still unless the item needs moving. So each item gets a scenario:
#   stat  - given, wait, compare (no enemies, nobody moves)            [default]
#   fight - enemies around the holder only, both still                 (on-hit / kill / area-damage items)
#   hurt  - the game's damage on both players, a few enemies at the holder (damage-taken / heal items)
#   move  - walking and jumping, no enemies                            (movement items)
#   still - standing still for a while, no enemies                     (Campfire, Idle Juice)
# The players stand 40 m apart; in phase B they swap places, so the holder always uses the same open spot.
#   powershell -ExecutionPolicy Bypass -File tools\gen_itemtests.ps1
param([int]$PerFile = 22)

$items = @('Key','Beer','SpikyShield','Bonker','SlipperyRing','CowardsCloak','GymSauce','Battery','PhantomShroud','ForbiddenJuice',
  'DemonBlade','GrandmasSecretTonic','GiantFork','MoldyCheese','GoldenSneakers','SpicyMeatball','Chonkplate','LightningOrb','IceCube',
  'DemonicBlood','DemonicSoul','BeefyRing','Dragonfire','GoldenGlove','GoldenShield','ZaWarudo','OverpoweredLamp','Feathers','Ghost',
  'SluttyCannon','TurboSocks','ShatteredWisdom','EchoShard','SuckyMagnet','Backpack','Clover','Campfire','Rollerblades','Skuleg',
  'EagleClaw','Scarf','Anvil','Oats','CursedDoll','EnergyCore','ElectricPlug','BobDead','SoulHarvester','Mirror','JoesDagger',
  'WeebHeadset','SpeedBoi','Gasmask','ToxicBarrel','HolyBook','BrassKnuckles','IdleJuice','Kevin','Borgar','Medkit','GamerGoggles',
  'UnstableTransfusion','BloodyCleaver','CreditCardRed','CreditCardGreen','BossBuster','LeechingCrystal','TacticalGlasses','Cactus',
  'CageKey','IceCrystal','TimeBracelet','GloveLightning','GlovePoison','GloveBlood','GloveCurse','GlovePower','Wrench','Beacon',
  'GoldenRing','QuinsMask','CryptKey','OldMask','Snek','Pot','BobsLantern','Pumpkin','WizardsHat')

$fight = @('Bonker','DemonBlade','GrandmasSecretTonic','MoldyCheese','SpicyMeatball','LightningOrb','IceCube','DemonicBlood','DemonicSoul',
  'Dragonfire','GoldenGlove','SluttyCannon','ShatteredWisdom','EchoShard','EagleClaw','Anvil','CursedDoll','EnergyCore','SoulHarvester',
  'JoesDagger','WeebHeadset','BrassKnuckles','Kevin','Borgar','UnstableTransfusion','BloodyCleaver','LeechingCrystal','IceCrystal',
  'GloveLightning','GlovePoison','GloveBlood','GloveCurse','GlovePower','QuinsMask','Snek','BobsLantern','ZaWarudo','GamerGoggles')
$hurt = @('SpikyShield','CowardsCloak','PhantomShroud','Chonkplate','GoldenShield','ElectricPlug','Mirror','SpeedBoi','ToxicBarrel',
  'HolyBook','Medkit','Cactus','Gasmask','Oats','Beer')
$move = @('GoldenSneakers','Feathers','Scarf','Rollerblades','BobDead')
$still = @('Campfire','IdleJuice')

function Kind($item) {
    if ($fight -contains $item) { 'fight' } elseif ($hurt -contains $item) { 'hurt' } elseif ($move -contains $item) { 'move' }
    elseif ($still -contains $item) { 'still' } else { 'stat' }
}

$dir = Join-Path $PSScriptRoot "..\tests\items"
New-Item -ItemType Directory -Force $dir | Out-Null

function Phase($item, $p, $tag) {
    $kind = Kind $item
    $lines = @(
        "home",
        $(if ($tag -eq 'B') { "swap" } else { "wait 0" }),
        "clear",
        "wait 1.5",            # landed (Scarf: in the air at the snapshot read as its bonus)
        "isnap $item.${tag}0",
        "item $p $item",
        "wait 1",
        "isnap $item.${tag}1"
    )
    switch ($kind) {
        'fight' { $lines += @("spawn $p Skeleton 8 5", "wait 5", $(if ($tag -eq 'B') { "shot fx_$item" } else { "wait 0" }), "wait 5") }
        'hurt'  { $lines += @("spawn $p Skeleton 5 6", "hurt p1 300", "hurt p2 300", "wait 1.5", "hurt p1 300", "hurt p2 300", "wait 1.5",
                              $(if ($tag -eq 'B') { "shot fx_$item" } else { "wait 0" }), "hurt p1 300", "hurt p2 300", "wait 2") }
        'move'  { $lines += @("go $p 1 0", "jump $p", "wait 1.5", "jump $p", "wait 1.5", $(if ($tag -eq 'B') { "shot fx_$item" } else { "wait 0" }),
                              "go $p -1 0", "jump $p", "wait 1.5", "jump $p", "wait 1.5", "stop $p") }
        'still' { $lines += @("wait 4", $(if ($tag -eq 'B') { "shot fx_$item" } else { "wait 0" }), "wait 4") }
        default { $lines += @("wait 1", $(if ($tag -eq 'B') { "shot fx_$item" } else { "wait 0" }), "wait 0.5") }
    }
    $lines += @("isnap $item.${tag}2", "unitem $p $item", "clear", "wait 1")
    $lines
}

$n = 0
for ($i = 0; $i -lt $items.Count; $i += $PerFile) {
    $n++
    $chunk = $items[$i..([Math]::Min($i + $PerFile, $items.Count) - 1)]
    $lines = @(
        "# Item sweep part ${n}: each item on P1 alone, then on P2 alone, same scenario; icheck compares (generated by tools\gen_itemtests.ps1)",
        "chars Monke Monke",
        "wait 2",
        "godp1",
        "godp2",
        "open",
        "clear",
        "clearloot",
        "nopowerups",
        "wait 1"
    )
    $first = $true
    foreach ($it in $chunk) {
        $lines += "log $it ($(Kind $it))"
        $lines += Phase $it 'p1' 'A'
        $lines += Phase $it 'p2' 'B'
        $lines += "icheck $it"
        if ($first) { $lines += "item p1 $it"; $lines += "item p2 $it"; $lines += "wait 0.5"; $lines += "shot items_rows_$n"; $lines += "unitem p1 $it"; $lines += "unitem p2 $it"; $first = $false }
    }
    $lines += "items"
    $lines += "quit"
    $path = Join-Path $dir "items_$n.txt"
    $lines | Out-File -Encoding utf8 $path
    Write-Output "$path ($($chunk.Count) items, $($lines.Count) lines)"
}
