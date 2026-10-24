using System.Reflection;
using Assets.Scripts.Actors.Player;
using UnityEngine;

namespace MegabonkSplitscreen;

// TEMP diagnostics (user: P2's Calcium - on a skateboard, a bit slidy - doesn't animate or move like P1's Calcium;
// "log the animation parameters, and how he moves" with Calcium as P1). Names first, no guessing:
//  - movement: the per-character movement values (PlayerMovement.movementValues: PlayerMovementValues, with
//    currentCharacter, counterMovement = friction, moveSpeed, maxRunSpeed...) of P1 and of P2's copy, whether the two
//    share one object, and every PlayerMovement number that differs between them; repeated every 15 s for a minute
//  - animation: P1's animator (controller name, layers, parameters with their values when they change) and the state
//    on every layer when it changes, with P1's speed and grounded; the game scripts on P1's character
internal static class CharacterDiag
{
    private static float startedAt = -1f, nextMove;
    private static int moveLogs;
    private static bool componentsLogged;
    private static readonly Dictionary<int, int> layerStates = new();
    private static int animLogs;
    private static string lastParams = "";
    private static float nextParams;

    public static void Update()
    {
        var p1 = MyPlayer.Instance;
        if (p1 == null || !P2Movement.Active) { startedAt = -1f; return; }
        if (startedAt < 0f) { startedAt = Time.time; nextMove = Time.time + 2f; }
        try
        {
            if (Time.time >= nextMove && moveLogs < 5) { nextMove = Time.time + 15f; moveLogs++; LogMovement(p1); }
            if (!componentsLogged) { componentsLogged = true; LogComponents(p1); }
            if (Time.time - startedAt < 120f) LogAnimation(p1);
            if (p1.character == ECharacter.Monke || P2Inventory.P2Character?.eCharacter == ECharacter.Monke) LogClimb(p1);
        }
        catch (Exception e) { if (animLogs++ < 3) ModLog.Warn($"[calcium] diag failed: {e.Message}"); }
    }

    private static void LogMovement(MyPlayer p1)
    {
        var a = p1.playerMovement ?? PlayerMovement.Instance;
        var b = P2Movement.Pm;
        if (a == null || b == null) return;
        var va = a.movementValues;
        var vb = b.movementValues;
        ModLog.Info($"[calcium] P1 is {p1.character}; P1 movement values {Describe(va)}");
        ModLog.Info($"[calcium] P2 ({P2Inventory.P2Character?.eCharacter}) movement values {Describe(vb)}; same object as P1's: {(va != null && vb != null && va.Pointer == vb.Pointer)}");
        var diffs = new List<string>();
        foreach (var p in typeof(PlayerMovement).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !(p.PropertyType == typeof(float) || p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType.IsEnum)) continue;
            object x, y;
            try { x = p.GetValue(a); y = p.GetValue(b); } catch { continue; }
            if (!Equals(x, y)) diffs.Add($"{p.Name} P1 {Fmt(x)} / P2 {Fmt(y)}");
        }
        ModLog.Info($"[calcium] PlayerMovement numbers that differ: {(diffs.Count == 0 ? "none" : string.Join(", ", diffs))}");
    }

    private static string Describe(Il2CppSystem.Object v)
    {
        if (v == null) return "none";
        var parts = new List<string>();
        foreach (var p in typeof(Assets.Scripts.Player.Movement.PlayerMovementValues).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.Name.EndsWith("k__BackingField")) continue;
            try { parts.Add($"{p.Name}={Fmt(p.GetValue(v.Cast<Assets.Scripts.Player.Movement.PlayerMovementValues>()))}"); } catch { }
        }
        return string.Join(", ", parts);
    }

    private static string Fmt(object o) => o is float f ? f.ToString("F2") : o?.ToString() ?? "null";

    private static void LogComponents(MyPlayer p1)
    {
        var names = new List<string>();
        foreach (var mb in p1.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var t = mb.GetIl2CppType();
            if (t.Assembly.GetName().Name != "Assembly-CSharp") continue;
            names.Add($"{t.Name} on '{mb.name}'");
        }
        ModLog.Info($"[calcium] game scripts on P1 ({p1.character}): {string.Join(", ", names)}");
        var anim = p1.playerRenderer?.animator;
        if (anim != null)
            ModLog.Info($"[calcium] P1 animator '{anim.runtimeAnimatorController?.name}' on '{anim.name}': {anim.layerCount} layers, {anim.parameterCount} parameters, speed {anim.speed:F2}, root motion {anim.applyRootMotion}");
    }

    private static void LogAnimation(MyPlayer p1)
    {
        var anim = p1.playerRenderer?.animator;
        if (anim == null || animLogs >= 120) return;
        var pm = PlayerMovement.Instance;
        Vector3 v = pm?.rb != null ? pm.rb.velocity : Vector3.zero;
        for (int l = 0; l < anim.layerCount; l++)
        {
            var info = anim.GetCurrentAnimatorStateInfo(l);
            int s = info.shortNameHash;
            if (layerStates.TryGetValue(l, out int was) && was == s) continue;
            layerStates[l] = s;
            animLogs++;
            ModLog.Info($"[calcium] P1 layer {l} ('{anim.GetLayerName(l)}', weight {anim.GetLayerWeight(l):F2}) -> state {s} (length {info.length:F2}s, speed {info.speed:F2}, loop {info.loop}); " +
                        $"P1 flat speed {new Vector2(v.x, v.z).magnitude:F1}, vy {v.y:F1}, grounded {pm?.grounded}");
        }
        // Parameter values (read one by one by index - the parameter array itself fails in this interop build)
        if (Time.time < nextParams) return;
        nextParams = Time.time + 0.2f;
        var parts = new List<string>();
        for (int i = 0; i < anim.parameterCount; i++)
        {
            AnimatorControllerParameter p;
            try { p = anim.GetParameter(i); } catch { break; }
            if (p == null) continue;
            string val = p.type switch
            {
                AnimatorControllerParameterType.Float => anim.GetFloat(p.nameHash).ToString("F1"),
                AnimatorControllerParameterType.Int => anim.GetInteger(p.nameHash).ToString(),
                AnimatorControllerParameterType.Bool => anim.GetBool(p.nameHash) ? "on" : "off",
                _ => "trigger"
            };
            parts.Add($"{p.name}={val}");
        }
        string now = string.Join(" ", parts);
        if (now == lastParams) return;
        lastParams = now;
        animLogs++;
        ModLog.Info($"[calcium] P1 parameters: {(now.Length == 0 ? "(none)" : now)}");
    }

    // TEMP (user: the Monkey climbs walls; replicate on P2). Real names (types dump + string table): PlayerMovement
    // wallClimbing, lastMovementState, canWallClimbAtTime, lastTouchedWallTime, wallNormal, minWallClimbSpeed,
    // wallClimbCooldown, climbCancel(Ticks), event A_StartedWallClimb; passive WallClimb (HasPassive). Logged for each
    // player when climbing / state / wall contact changes, with input and animator state.
    private static readonly string[] lastClimb = new string[2];
    private static int climbLogs;
    private static bool climbOnce;
    private static void LogClimb(MyPlayer p1)
    {
        if (!climbOnce)
        {
            climbOnce = true;
            bool p1Has = p1.inventory?.HasPassive(Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive.EPassive.WallClimb) ?? false;
            bool p2Has = false;
            bool entered = P2Inventory.EnterP2();
            try { p2Has = p1.inventory?.HasPassive(Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive.EPassive.WallClimb) ?? false; }
            finally { if (entered) P2Inventory.Exit(); }
            var ev = PlayerMovement.A_StartedWallClimb;
            string listeners = ev == null ? "none" : string.Join(", ", ev.GetInvocationList().Select(l => { try { return $"{l.Method?.DeclaringType?.Name}.{l.Method?.Name}"; } catch { return "?"; } }));
            ModLog.Info($"[climb] WallClimb passive: P1 {p1Has}, P2 (in P2's context) {p2Has}; A_StartedWallClimb listeners: {listeners}");
        }
        if (climbLogs >= 120) return;
        for (int i = 0; i < 2; i++)
        {
            var pm = i == 0 ? p1.playerMovement : P2Movement.Pm;
            if (pm == null) continue;
            bool touching = Time.time - pm.lastTouchedWallTime < 0.2f;
            string key = $"climbing {pm.wallClimbing}, state {pm.lastMovementState}, grounded {pm.grounded}, touching wall {touching}";
            if (key == lastClimb[i]) continue;
            lastClimb[i] = key;
            climbLogs++;
            Vector3 v = pm.rb != null ? pm.rb.velocity : Vector3.zero;
            var anim = i == 0 ? p1.playerRenderer?.animator : P2Body.Anim;
            string input = i == 0 && p1.playerInput != null
                ? $"input move ({p1.playerInput.moveHorizontal:F1},{p1.playerInput.moveVertical:F1}) jump {p1.playerInput.jumping} holdingWallrun {p1.playerInput.holdingWallrun}"
                : $"stick {P2Control.StickNow}";
            ModLog.Info($"[climb] P{i + 1}: {key}; vel {v}, wallNormal {pm.wallNormal}, canWallClimbAtTime {pm.canWallClimbAtTime:F1} (now {Time.time:F1}), " +
                        $"minWallClimbSpeed {pm.minWallClimbSpeed:F1}, cooldown {pm.wallClimbCooldown:F1}, climbCancel {pm.climbCancel}/{pm.climbCancelTicks}; {input}; " +
                        $"anim state {(anim != null ? anim.GetCurrentAnimatorStateInfo(0).shortNameHash : 0)}");
        }
    }

    public static void Reset()
    {
        climbOnce = false;
        climbLogs = 0;
        startedAt = -1f;
        moveLogs = 0;
        componentsLogged = false;
        layerStates.Clear();
        animLogs = 0;
        lastParams = "";
    }
}
