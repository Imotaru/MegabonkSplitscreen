using Assets.Scripts.Actors.Player;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MegabonkSplitscreen;

// TEMP measuring (user: P2 takes no fall damage; rule: log the real values before building on them).
// In the game PlayerHealth.OnPlayerLanded(speed) - listening to the static PlayerMovement.A_Landed - applies fall
// damage, with PlayerHealth.minFallDamageSpeed / maxFallDamageSpeed. P2's movement copy fires A_Landed too, but the
// mod mutes it (it would hurt P1). This logs P1's landings (speed -> HP lost, max HP, the two limits) and P2's landing
// speeds, so P2's fall damage can then use the game's real rule.
internal static class FallDamage
{
    private static Il2CppSystem.Action<float> original;
    private static bool listening;
    private static float landedAt = -1f, landedSpeed;
    private static int hpBefore = -1, logs;

    public static void Init()
    {
        if (listening) return;
        try
        {
            original = PlayerMovement.A_Landed;
            var mine = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnLanded));
            PlayerMovement.A_Landed = original == null ? mine : Il2CppSystem.Delegate.Combine(original, mine).Cast<Il2CppSystem.Action<float>>();
            listening = true;
            ModLog.Info("FallDamage: listening to P1's landings (measuring)");
        }
        catch (Exception e) { ModLog.Warn($"FallDamage: couldn't listen: {e.Message}"); }
    }

    // P1's landings (P2's are muted while P2's copy ticks)
    private static void OnLanded(float speed)
    {
        var ph = MyPlayer.Instance?.inventory?.playerHealth;
        landedAt = Time.time;
        landedSpeed = speed;
        hpBefore = ph != null ? ph.hp : -1;
    }

    // Called every frame in a run (from P2Body.Update): P1's HP loss right after a landing
    public static void Update()
    {
        if (landedAt < 0f || Time.time - landedAt < 0.15f) return;
        var ph = MyPlayer.Instance?.inventory?.playerHealth;
        int hpNow = ph != null ? ph.hp : -1;
        if (logs++ < 25 && (hpBefore - hpNow > 0 || Mathf.Abs(landedSpeed) > 10f))
            ModLog.Info($"[fall] P1 landed at speed {landedSpeed:F1}: lost {hpBefore - hpNow} HP ({hpBefore} -> {hpNow}, max {ph?.maxHp}), " +
                        $"limits min {ph?.minFallDamageSpeed:F1} max {ph?.maxFallDamageSpeed:F1}, FallDamageReduction {MyPlayer.Instance?.inventory?.playerStats?.GetStat(Assets.Scripts.Menu.Shop.EStat.FallDamageReduction):F2}");
        landedAt = -1f;
    }

    public static void Shutdown()
    {
        if (!listening) return;
        listening = false;
        try { PlayerMovement.A_Landed = original; }
        catch { }
    }
}
