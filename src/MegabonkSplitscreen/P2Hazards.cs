using Assets.Scripts.Actors.Player;
using HarmonyLib;
using UnityEngine;

namespace MegabonkSplitscreen;

// Map hazards for P2 (user: "environmental stuff like tornadoes dont affect p2").
// p1_down_rewards test (v0.49.2): a tornado 6.7 m from P1 threw P1 up (44 m/s), 7.6 m from P2 did nothing - Tornado
// (Start/Spawn/FindNewDir/Update/FixedUpdate, a 'force'; no trigger callbacks) works on "the player" in its physics
// step. After its own step, when P2 is near, the step runs once more with P2 as the player: P2's movement copy in
// PlayerMovement.Instance and in P1's MyPlayer.playerMovement, P1's object at P2's spot - all put back after.
internal static class P2Hazards
{
    private const float Reach = 40f;   // only tornadoes this near P2 are run for P2
    private static bool inP2Step;
    private static int logs, errors;

    public static void Apply(Harmony harmony)
    {
        var m = AccessTools.DeclaredMethod(typeof(Tornado), "FixedUpdate");
        if (m == null || !P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) { ModLog.Warn("P2Hazards: Tornado.FixedUpdate not hookable - tornadoes don't reach P2"); return; }
        harmony.Patch(m, postfix: new HarmonyMethod(typeof(P2Hazards), nameof(AfterTornadoStep)));
        ModLog.Info("P2Hazards: tornadoes reach P2 too");
    }

    private static void AfterTornadoStep(Tornado __instance)
    {
        if (inP2Step || __instance == null) return;
        var p1 = MyPlayer.Instance;
        var pm = P2Movement.Pm;
        if (p1 == null || pm == null || !P2Movement.Active || P2Health.IsDead) return;
        if ((__instance.transform.position - pm.transform.position).sqrMagnitude > Reach * Reach) return;
        var p1Pm = PlayerMovement.Instance;
        var p1Own = p1.playerMovement;
        Vector3 p1Pos = P2Items.P1Home(p1);
        // The tornado's own movement must not run twice: its body's velocity and position are kept as they were
        var rb = __instance.rb;
        Vector3 tPos = rb != null ? rb.position : __instance.transform.position, tVel = rb != null ? rb.velocity : Vector3.zero;
        Vector3 before = pm.rb != null ? pm.rb.velocity : Vector3.zero;
        inP2Step = true;
        try
        {
            PlayerMovement.Instance = pm;
            p1.playerMovement = pm;
            p1.transform.position = pm.transform.position;
            __instance.FixedUpdate();
        }
        catch (Exception e) { if (errors++ < 3) ModLog.Warn($"P2Hazards: tornado step for P2 failed: {e.Message}"); }
        finally
        {
            p1.transform.position = p1Pos;
            p1.playerMovement = p1Own;
            if (p1Pm != null) PlayerMovement.Instance = p1Pm;
            if (rb != null) { rb.position = tPos; rb.velocity = tVel; }
            inP2Step = false;
        }
        Vector3 after = pm.rb != null ? pm.rb.velocity : Vector3.zero;
        if ((after - before).sqrMagnitude > 0.01f && logs++ < 6)
            ModLog.Info($"[hazard] tornado {Vector3.Distance(__instance.transform.position, pm.transform.position):F1} m from P2: P2's speed {before} -> {after}");
    }
}
