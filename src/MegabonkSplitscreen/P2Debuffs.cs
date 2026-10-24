using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using HarmonyLib;
using UnityEngine;

namespace MegabonkSplitscreen;

// Player debuffs (user: the 30000 damage was a poison debuff - P2 has none; find them all to test each one).
// The game's player status effects (EStatusEffect): Haste, Rage, Shield, Stonks, TimeFreeze, Invulnerability (power-ups,
// P2PowerUps) and the debuffs Slow, Freeze, Bleed, Poison, BossPoison, given by PlayerStatusEffects.SlowPlayer /
// FreezePlayer / BleedPlayer / PoisonPlayer / BossPoisonPlayer. Step 1 (TEMP [debuff]): every debuff the game gives,
// whose list it went on and what was happening (the last enemy special attack hit); the sources in the game's data.
internal static class P2Debuffs
{
    public static readonly string[] Kinds = { "Slow", "Freeze", "Bleed", "Poison", "BossPoison" };

    public static void Apply(Harmony harmony)
    {
        int n = 0;
        foreach (var name in new[] { "SlowPlayer", "FreezePlayer", "BleedPlayer", "PoisonPlayer", "BossPoisonPlayer" })
        {
            var m = AccessTools.DeclaredMethod(typeof(PlayerStatusEffects), name);
            if (m == null) continue;
            if (!P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) { ModLog.Warn($"P2Debuffs: {name} shares native code or is hooked, not hooked"); continue; }
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(P2Debuffs), nameof(AfterDebuff)));
            n++;
        }
        // TEMP [dmg]: the damage calls while someone has a debuff (bleed crossed over to the other player, 10 HP)
        var ext = AccessTools.DeclaredMethod(typeof(Assets.Scripts.Inventory__Items__Pickups.PlayerHealth), "DamagePlayerExternal");
        if (ext != null && P2Weapons.IsNativeCodeUnique(ext) && !P2Weapons.IsNativeCodeHooked(ext))
        {
            harmony.Patch(ext, prefix: new HarmonyMethod(typeof(P2Debuffs), nameof(BeforeExternal)));
            n++;
        }
        ModLog.Info($"P2Debuffs: {n} of 6 debuff hooks (logging)");
    }

    private static int dmgLogs;
    private static void BeforeExternal(float damage, string damageSource)
    {
        var p1d = MyPlayer.Instance?.inventory?.statusEffects?.statusEffects;
        var p2d = P2PowerUps.Status?.statusEffects;
        if ((p1d == null || p1d.Count == 0) && (p2d == null || p2d.Count == 0)) return;
        if (dmgLogs++ >= 80) return;
        ModLog.Info($"[dmg] DamagePlayerExternal({damage:F1}, '{damageSource}') - borrowed for P2 {P2GameHealth.Borrowing}; " +
                    $"P1 [{Describe(MyPlayer.Instance?.inventory?.statusEffects)}], P2 [{P2PowerUps.DescribeStatus()}]; frame {Time.frameCount}");
    }

    private static int logs;
    private static void AfterDebuff(PlayerStatusEffects __instance, System.Reflection.MethodBase __originalMethod, float __0)
    {
        // during an enemy special attack's hit: P2BossAttacks gives P2 the same when P2 is in it ("SlowPlayer" -> "Slow")
        if (P2BossAttacks.InHit && __originalMethod != null) P2BossAttacks.NoteDebuff(__originalMethod.Name.Replace("Player", ""), __0);
        if (logs++ >= 60) return;
        var p1List = MyPlayer.Instance?.inventory?.statusEffects;
        string whose = __instance == null ? "?" : p1List != null && __instance.Pointer == p1List.Pointer ? "P1's"
            : P2PowerUps.Status != null && __instance.Pointer == P2PowerUps.Status.Pointer ? "P2's" : "another";
        ModLog.Info($"[debuff] {__originalMethod?.Name}({__0:F1} s) on {whose} list; last special attack hit {P2BossAttacks.LastHit} " +
                    $"({Time.time - P2BossAttacks.LastHitAt:F2} s ago); P1 at {MyPlayer.Instance?.transform.position}, P2 at {(P2Movement.Active ? P2Movement.BodyPosition().ToString() : "-")}");
    }

    // A debuff on one player through the game's own function (autotest)
    public static string Give(int player, string kind, float secs)
    {
        var list = player == 0 ? MyPlayer.Instance?.inventory?.statusEffects : P2PowerUps.EnsureStatus();
        if (list == null) return $"debuff: P{player + 1} has no status list";
        Action call = kind.ToLowerInvariant() switch
        {
            "slow" => () => list.SlowPlayer(secs),
            "freeze" => () => list.FreezePlayer(secs),
            "bleed" => () => list.BleedPlayer(secs),
            "poison" => () => list.PoisonPlayer(secs),
            "bosspoison" => () => list.BossPoisonPlayer(secs),
            _ => null
        };
        if (call == null) return $"debuff: unknown '{kind}' ({string.Join(", ", Kinds)})";
        if (player == 0) call();
        else P2PowerUps.OnStatus(call);
        return $"P{player + 1} got {kind} for {secs:F1} s";
    }

    public static string Describe(PlayerStatusEffects list)
    {
        if (list?.statusEffects == null || list.statusEffects.Count == 0) return "none";
        var parts = new List<string>();
        foreach (var kv in list.statusEffects) parts.Add($"{kv.Key} ({(kv.Value != null ? kv.Value.expirationTime - Time.time : 0f):F1} s)");
        return string.Join(", ", parts);
    }

    // The game's data: enemies whose touch poisons, every enemy's special attacks (kind and prefab script)
    public static string Sources()
    {
        var dm = DataManager.Instance;
        if (dm?.enemyData == null) return "no enemy data";
        var poison = new List<string>();
        var attacks = new List<string>();
        foreach (var kv in dm.enemyData)
        {
            var ed = kv.Value;
            if (ed == null) continue;
            if (ed.isPoison) poison.Add($"{ed.enemyName} (maps {ed.maps}, from stage {ed.minStage})");
            var sas = ed.specialAttacks;
            if (sas == null) continue;
            foreach (var sa in sas)
            {
                if (sa == null) continue;
                var pf = sa.attackPrefab != null ? sa.attackPrefab.GetComponent<Assets.Scripts.Game.Combat.EnemySpecialAttacks.Implementations.EnemySpecialAttackPrefab>() : null;
                attacks.Add($"{ed.enemyName}: '{sa.attackName}' {(pf != null ? $"{pf.eAttack} ({pf.GetIl2CppType().Name})" : $"prefab '{sa.attackPrefab?.name}'")} radius {sa.attackRadius:F1} x{sa.damageMultiplier:F2}");
            }
        }
        return $"poison on touch: [{string.Join(", ", poison)}]; special attacks ({attacks.Count}): [{string.Join(" | ", attacks)}]";
    }
}
