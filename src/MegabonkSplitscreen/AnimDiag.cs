using HarmonyLib;
using UnityEngine;

namespace MegabonkSplitscreen;

// TEMP test-only (autotest "animhook"): which animator values the game changes on P1's animator - by hash, the names
// can't be read (Animator.GetParameter / .parameters fail through the interop). Slide test: the value that turns on
// when P1 starts sliding (P2's animator left the slide state at once - this value keeps P1's in it).
internal static class AnimDiag
{
    private static bool applied;
    private static readonly Dictionary<string, string> last = new();
    private static int logs;

    public static string Apply()
    {
        if (applied) return "animhook: already on";
        var h = new Harmony(Plugin.Guid + ".animdiag");
        int n = 0;
        foreach (var (name, types, post) in new[]
        {
            ("SetBool", new[] { typeof(int), typeof(bool) }, nameof(AfterBool)),
            ("SetFloat", new[] { typeof(int), typeof(float) }, nameof(AfterFloat)),
            ("SetInteger", new[] { typeof(int), typeof(int) }, nameof(AfterInt)),
            ("SetTrigger", new[] { typeof(int) }, nameof(AfterTrigger)),
        })
        {
            var m = AccessTools.Method(typeof(Animator), name, types);
            if (m == null || !P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) continue;
            h.Patch(m, postfix: new HarmonyMethod(typeof(AnimDiag), post));
            n++;
        }
        applied = true;
        return $"animhook: {n} of 4 Animator setters hooked (P1's animator changes logged as [animset])";
    }

    private static bool IsP1(Animator a)
    {
        var p1 = Assets.Scripts.Actors.Player.MyPlayer.Instance?.playerRenderer?.animator;
        return a != null && p1 != null && a.Pointer == p1.Pointer;
    }

    private static void Note(Animator a, string kind, int hash, string value)
    {
        if (!IsP1(a)) return;
        string key = kind + hash;
        if (last.TryGetValue(key, out var was) && was == value) return;
        last[key] = value;
        if (logs++ < 120)
        {
            var pm = Assets.Scripts.Actors.Player.MyPlayer.Instance?.playerMovement;
            ModLog.Info($"[animset] P1 {kind}({hash}) = {value}; P1 sliding {pm?.IsSlidingAnimation()}, grounded {pm?.grounded}");
        }
    }

    // TEMP test-only (autotest "tickrate"): how often the game ticks P1's health (P2's regen was 2.4x P1's - P2's tick
    // runs once a frame)
    private static int p1Ticks, frames0, fixed0;
    private static float since;
    public static int FixedSteps;
    public static string TickRate()
    {
        if (since <= 0f)
        {
            var m = AccessTools.DeclaredMethod(typeof(Assets.Scripts.Inventory__Items__Pickups.PlayerHealth), "Tick");
            if (m == null || !P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) return "tickrate: PlayerHealth.Tick not hookable";
            new Harmony(Plugin.Guid + ".tickdiag").Patch(m, prefix: new HarmonyMethod(typeof(AnimDiag), nameof(BeforeTick)));
            since = Time.unscaledTime; frames0 = Time.frameCount; fixed0 = FixedSteps; p1Ticks = 0;
            return "tickrate: counting";
        }
        float dt = Time.unscaledTime - since;
        return $"tickrate over {dt:F1} s: P1 health ticks {p1Ticks / dt:F1}/s, frames {(Time.frameCount - frames0) / dt:F1}/s, physics steps {(FixedSteps - fixed0) / dt:F1}/s";
    }
    private static void BeforeTick(Assets.Scripts.Inventory__Items__Pickups.PlayerHealth __instance)
    {
        var p1 = Assets.Scripts.Actors.Player.MyPlayer.Instance?.inventory?.playerHealth;
        if (p1 != null && __instance != null && __instance.Pointer == p1.Pointer && !P2GameHealth.Borrowing) p1Ticks++;
    }

    private static void AfterBool(Animator __instance, int __0, bool __1) => Note(__instance, "SetBool", __0, __1.ToString());
    private static void AfterFloat(Animator __instance, int __0, float __1) => Note(__instance, "SetFloat", __0, __1.ToString("F1"));
    private static void AfterInt(Animator __instance, int __0, int __1) => Note(__instance, "SetInteger", __0, __1.ToString());
    private static void AfterTrigger(Animator __instance, int __0) => Note(__instance, "SetTrigger", __0, "fired@" + Time.frameCount);
}
