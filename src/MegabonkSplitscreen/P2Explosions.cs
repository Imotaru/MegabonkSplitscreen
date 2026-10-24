using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Exploding enemies hurt and push P2 too (user: they knocked P1 back, not P2).
// In the game, EffectManager.ExploderEnemy(enemy) starts the exploder's death; it shows a warning circle
// (EffectManager.WarningSphere(position, radius, time, ...)) and explodes when it runs out, hurting and pushing the one
// player (PlayerMovement.PushPlayer). For P2: a warning circle right after an exploder started (near it) schedules a
// check at the moment it goes off; P2 inside the radius takes the damage P1 takes from such a blast and is pushed
// away from its centre with the strength P1 gets, through the game's own PushPlayer on P2's movement copy.
// P1's push strength and blast damage are learned from P1's own blasts (defaults until then).
internal static class P2Explosions
{
    private static float exploderStartedAt = -1f;
    private static Vector3 exploderPos;
    private static readonly List<(Vector3 pos, float radius, float at)> pending = new();
    private static float pushStrength = 25f, upShare = 0.4f;
    private static int blastDamage = -1;
    private static bool learnedPush;
    private static float lastBlastAt = -1f;
    private static Vector3 lastBlastPos;
    private static int lastP1Hp = -1;

    public static void Apply(Harmony harmony)
    {
        int n = 0;
        n += Hook(harmony, typeof(EffectManager), nameof(EffectManager.ExploderEnemy), nameof(AfterExploder));
        n += Hook(harmony, typeof(EffectManager), nameof(EffectManager.WarningSphere), nameof(AfterWarning));
        n += Hook(harmony, typeof(PlayerMovement), nameof(PlayerMovement.PushPlayer), nameof(AfterPush));
        // v0.37.1 (user: explosions at P2 affect P1): the game's own blast code runs when the explosion starts; while it
        // runs, P2's copy collider is off (so "a player in range" can't be P2's body standing in for P1) and P1 is watched
        var start = AccessTools.DeclaredMethod(typeof(CombatExplosion), "Start");
        if (start != null && P2Weapons.IsNativeCodeUnique(start) && !P2Weapons.IsNativeCodeHooked(start))
        {
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(P2Explosions), nameof(BeforeBlastStart)),
                finalizer: new HarmonyMethod(typeof(P2Explosions), nameof(AfterBlastStart)));
            n++;
        }
        else ModLog.Warn("P2Explosions: CombatExplosion.Start shares native code, not hooked");
        ModLog.Info($"P2Explosions: {n} of 4 hooks");
    }

    // ---------- P1 guard ----------
    // P1's movement/health before something done for P2; after it, any change is logged and undone (P1 is far away)
    private struct P1State { public Vector3 vel; public bool pushed; public int hp; public float shield; public bool ok; }

    private static P1State CaptureP1()
    {
        var pm = PlayerMovement.Instance;
        var ph = MyPlayer.Instance?.inventory?.playerHealth;
        if (pm == null || pm.rb == null || ph == null || (P2Movement.Pm != null && pm.Pointer == P2Movement.Pm.Pointer)) return default;
        return new P1State { vel = pm.rb.velocity, pushed = pm.pushed, hp = ph.hp, shield = ph.shield, ok = true };
    }

    private static int guardLogs;
    private static void RestoreP1(P1State s, string what)
    {
        if (!s.ok) return;
        var pm = PlayerMovement.Instance;
        var ph = MyPlayer.Instance?.inventory?.playerHealth;
        if (pm == null || pm.rb == null || ph == null) return;
        bool changed = (pm.rb.velocity - s.vel).sqrMagnitude > 1f || pm.pushed != s.pushed || ph.hp != s.hp || Mathf.Abs(ph.shield - s.shield) > 0.01f;
        if (!changed) return;
        if (guardLogs++ < 10)
            ModLog.Warn($"[blast] {what} changed P1 (P1 far from it): velocity {s.vel} -> {pm.rb.velocity}, pushed {s.pushed} -> {pm.pushed}, hp {s.hp} -> {ph.hp}, shield {s.shield:F0} -> {ph.shield:F0} - undone");
        pm.rb.velocity = s.vel;
        pm.pushed = s.pushed;
        ph.hp = s.hp;
        ph.shield = s.shield;
    }

    public static void GuardP1(string what, Action a)
    {
        var s = CaptureP1();
        try { a(); }
        finally { RestoreP1(s, what); }
    }

    private static Collider p2ColliderOff;
    private static void BeforeBlastStart(CombatExplosion __instance, out P1State __state)
    {
        __state = default;
        if (__instance == null || !P2Movement.Active || MyPlayer.Instance == null) return;
        Vector3 c = __instance.transform.position;
        bool p1In = Vector3.Distance(MyPlayer.Instance.transform.position, c) <= __instance.radius + 1.5f;
        if (p1In) return;   // P1 really is in it: the game's own blast for P1, untouched
        __state = CaptureP1();
        var col = P2Movement.Pm?.playerCollider;
        if (col != null && col.enabled) { col.enabled = false; p2ColliderOff = col; }
    }

    private static Exception AfterBlastStart(Exception __exception, P1State __state)
    {
        if (p2ColliderOff != null) { p2ColliderOff.enabled = true; p2ColliderOff = null; }
        RestoreP1(__state, "the game's blast");
        return __exception;
    }

    private static int Hook(Harmony harmony, Type t, string name, string post)
    {
        var m = AccessTools.DeclaredMethod(t, name);
        if (m == null) return 0;
        if (!P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m))
        {
            ModLog.Warn($"P2Explosions: {t.Name}.{name} shares native code, not hooked");
            return 0;
        }
        harmony.Patch(m, postfix: new HarmonyMethod(typeof(P2Explosions), post));
        return 1;
    }

    private static void AfterExploder(Enemy enemy)
    {
        exploderStartedAt = Time.time;
        exploderPos = enemy != null ? enemy.transform.position : Vector3.zero;
        if (exploderLogs++ < 10)
        {
            var p1 = MyPlayer.Instance;
            ModLog.Info($"[blast] exploder {enemy?.enemyData?.enemyName} started at {exploderPos}: P1 {(p1 != null ? Vector3.Distance(p1.transform.position, exploderPos) : -1f):F1} away, " +
                        $"P2 {(P2Movement.Active ? Vector3.Distance(P2Movement.BodyPosition(), exploderPos) : -1f):F1} away");
        }
    }
    private static int exploderLogs;

    // TEMP (user: explosions at P2 pushed P1): any sudden jump in P1's speed, with what's nearby
    private static Vector3 p1LastVel;
    private static int p1PushLogs, lateLogs;
    private static float lastP2OnlyBlastAt = -10f;
    private static void WatchP1Push()
    {
        var pm = PlayerMovement.Instance;
        if (pm?.rb == null) return;
        Vector3 v = pm.rb.velocity;
        Vector3 dv = v - p1LastVel;
        p1LastVel = v;
        // TEMP: P1 kicked shortly after a blast only P2 was in (a delayed effect the guard can't see)
        if (dv.magnitude >= 15f && Time.time - lastP2OnlyBlastAt < 0.6f && lateLogs++ < 10)
            ModLog.Warn($"[blast] P1's speed jumped by {dv} {Time.time - lastP2OnlyBlastAt:F2} s after a blast only P2 was in");
        if (dv.magnitude < 22f || p1PushLogs >= 10) return;   // above a jump's ~16
        p1PushLogs++;
        ModLog.Info($"[blast] P1's speed jumped by {dv} (now {v}); last exploder {Time.time - exploderStartedAt:F2} s ago at {exploderPos} " +
                    $"({Vector3.Distance(pm.transform.position, exploderPos):F1} from P1, {(P2Movement.Active ? Vector3.Distance(P2Movement.BodyPosition(), exploderPos) : -1f):F1} from P2); " +
                    $"last P2 push {Time.time - P2Movement.LastPushAt:F2} s ago");
    }

    private static void AfterWarning(Vector3 position, float radius, float time)
    {
        // Only the exploder's own circle (other attacks use warning circles too)
        if (exploderStartedAt < 0f || Time.time - exploderStartedAt > 2f || (position - exploderPos).sqrMagnitude > 15f * 15f) return;
        exploderStartedAt = -1f;
        // Log only now: the blast itself comes from a CombatExplosion (v0.29.0's warning path never caught one)
        if (warnLogs++ < 5) ModLog.Info($"[blast] exploder warning at {position}, radius {radius:F1}, goes off in {time:F2} s");
    }
    private static int warnLogs;

    // P1's own push: learn its strength (only right after a blast, so other pushes don't count)
    private static void AfterPush(PlayerMovement __instance, Vector3 pushForce)
    {
        if (__instance == null || P2Movement.Pm != null && __instance.Pointer == P2Movement.Pm.Pointer) return;
        if (lastBlastAt < 0f || Time.time - lastBlastAt > 0.3f) return;
        float m = pushForce.magnitude;
        if (m < 0.01f) return;
        pushStrength = m;
        upShare = Mathf.Clamp01(pushForce.normalized.y);
        if (!learnedPush || pushLogs++ < 3)
            ModLog.Info($"[blast] P1 pushed {pushForce} (strength {m:F1}, up {upShare:F2}) {Vector3.Distance(__instance.transform.position, lastBlastPos):F1} from the blast");
        learnedPush = true;
    }
    private static int pushLogs;

    // Called every frame in a run (from P2Body.Update)
    public static void Update()
    {
        if (!Assets.Scripts.Utility.MyTime.paused) WatchP1Push();
        var p1 = MyPlayer.Instance;
        int p1Hp = p1?.inventory?.playerHealth != null ? p1.inventory.playerHealth.hp : -1;
        // P1's damage from a blast that just went off: learn it
        if (lastBlastAt >= 0f && Time.time - lastBlastAt < 0.3f && lastP1Hp > 0 && p1Hp >= 0 && p1Hp < lastP1Hp)
        {
            blastDamage = lastP1Hp - p1Hp;
            if (dmgLogs++ < 3) ModLog.Info($"[blast] P1 took {blastDamage} from a blast");
        }
        lastP1Hp = p1Hp;

        // The game's blasts: each CombatExplosion hurts and knocks back the player within its radius when it starts
        // (radius, playerDamage, playerKnockback on the component). New ones are applied to P2 with their own values.
        // v0.44.19 ([perf] log: this scene-wide search took ~1 ms of every frame): looked for 10 times a second
        if (Time.timeScale > 0f && !Assets.Scripts.Utility.MyTime.paused && Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.1f;
            foreach (var ce in Object.FindObjectsOfType<CombatExplosion>())
            {
                if (ce == null || !seenBlasts.Add(ce.Pointer)) continue;
                lastBlastAt = Time.time;
                lastBlastPos = ce.transform.position;
                if (valueLogs++ < 5)
                    ModLog.Info($"[blast] CombatExplosion '{ce.name}' at {ce.transform.position}: radius {ce.radius:F1}, playerDamage {ce.playerDamage:F1}, " +
                                $"playerKnockback {ce.playerKnockback:F1}, enemyKnockback {ce.enemyKnockback:F1}");
                Blast(ce.transform.position, ce.radius, ce.playerDamage, ce.playerKnockback);
            }
        }
        if (seenBlasts.Count > 500) seenBlasts.Clear();
    }
    private static float nextScan;
    private static readonly HashSet<IntPtr> seenBlasts = new();
    private static int valueLogs;
    private static int dmgLogs;

    private static void Blast(Vector3 pos, float radius, float damage, float knockback)
    {
        if (!P2Movement.Active || P2Health.IsDead) return;
        Vector3 p2 = P2Movement.BodyPosition();
        float d = Vector3.Distance(p2, pos);
        if (d > radius) return;
        Vector3 flat = p2 - pos;
        flat.y = 0f;
        Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
        float strength = knockback > 0f ? knockback : pushStrength;
        Vector3 force = (dir * (1f - upShare) + Vector3.up * Mathf.Max(upShare, 0.3f)).normalized * strength;
        bool p1In = MyPlayer.Instance != null && Vector3.Distance(MyPlayer.Instance.transform.position, pos) <= radius + 1.5f;
        int dmg = Mathf.RoundToInt(damage);
        if (p1In)
        {
            if (strength > 0f) P2Movement.Push(force);
            if (dmg > 0) P2Health.TakeDamage(dmg, "explosion");
        }
        else
        {
            lastP2OnlyBlastAt = Time.time;
            if (strength > 0f) GuardP1("P2's blast push", () => P2Movement.Push(force));
            if (dmg > 0) GuardP1("P2's blast damage", () => P2Health.TakeDamage(dmg, "explosion"));
        }
        if (blastLogs++ < 5)
            ModLog.Info($"[blast] P2 caught in a blast {d:F1} from it (radius {radius:F1}): {dmg} damage, pushed {force}");
    }
    private static int blastLogs;

    public static void Reset()
    {
        pending.Clear();
        exploderStartedAt = -1f;
    }
}
