using Assets.Scripts.Actors.Player;
using HarmonyLib;
using UnityEngine;
using PH = Assets.Scripts.Inventory__Items__Pickups.PlayerHealth;

namespace MegabonkSplitscreen;

// P2's health runs on the game's own PlayerHealth code (user: every tome effect should work for P2 - armor, evasion,
// damage reduction, damage cooldown, shield recharge, regen, thorns, overheal, fall damage...).
// Like P2's stats: P1's PlayerHealth briefly holds P2's health state (every number field: hp, shield, timers,
// cooldowns...), inside P2's stat context, while one of its methods runs (DamagePlayer, DamagePlayerExternal, Tick,
// OnPlayerLanded, UpdateMaxValues); P2's results are read back and P1's state is put back exactly. During that:
//  - PlayerHealth's static events are muted (P1's HUD, hit flash, camera shake, damage popups don't react)
//  - P2's movement copy is "the" player (a hit's knockback pushes P2)
//  - KillPlayer / PlayerDied are skipped (P2 reaching 0 HP must not end the run) - P2Health handles P2 going down
// If the death methods can't be hooked safely, this stays off and P2Health uses its own simple rules.
internal static class P2GameHealth
{
    public static bool On { get; private set; }
    private static System.Reflection.PropertyInfo[] fields;
    private static System.Reflection.PropertyInfo[] events;
    private static readonly Dictionary<string, object> p2State = new();
    private static readonly Dictionary<string, object> p1State = new();
    private static bool borrowing;
    public static bool Borrowing => borrowing;
    private static int errors;
    private static int aegisBefore = -1, aegisLogs;

    public static void Apply(Harmony harmony, BepInEx.Configuration.ConfigFile config)
    {
        bool wanted = config.Bind("Experimental", "P2GameHealth", true,
            "Player 2's health uses the game's own health code (armor, evasion, shield, regen, thorns...). " +
            "Set to false to go back to the mod's simple health for player 2.").Value;
        if (!wanted) { ModLog.Info("P2GameHealth: OFF (config)"); return; }
        int hooked = 0;
        foreach (var name in new[] { "KillPlayer", "PlayerDied" })
        {
            var m = AccessTools.DeclaredMethod(typeof(PH), name);
            if (m == null || !P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) continue;
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(P2GameHealth), nameof(SkipWhileBorrowing)));
            hooked++;
        }
        if (hooked < 2) { ModLog.Warn($"P2GameHealth: death methods not hookable safely ({hooked}/2) - OFF"); return; }
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        fields = typeof(PH).GetProperties(flags)
            .Where(p => p.CanRead && p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(float) || p.PropertyType == typeof(bool)))
            .ToArray();
        events = typeof(PH).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.CanRead && p.CanWrite && typeof(Il2CppSystem.Delegate).IsAssignableFrom(p.PropertyType))
            .ToArray();
        // Lifesteal (user: P2's lifesteal tome did nothing): the game heals "the player" when an enemy is damaged
        // (PlayerHealth.OnEnemyDamaged -> TryLifestealHit, stored in lifestealHeal, applied in Tick). Damage has no
        // owner, so an enemy nearer P2 counts as P2's hit: the call runs on P2's borrowed health (P2's Lifesteal stat,
        // P2's heal) and not for P1.
        var oed = AccessTools.DeclaredMethod(typeof(PH), nameof(PH.OnEnemyDamaged));
        if (oed != null && P2Weapons.IsNativeCodeUnique(oed) && !P2Weapons.IsNativeCodeHooked(oed))
            harmony.Patch(oed, prefix: new HarmonyMethod(typeof(P2GameHealth), nameof(BeforeEnemyDamaged)));
        else ModLog.Warn("P2GameHealth: PlayerHealth.OnEnemyDamaged shares native code - lifesteal stays P1's");
        On = true;
        ModLog.Info($"P2GameHealth: ON - {fields.Length} health fields swapped ({string.Join(", ", fields.Select(f => f.Name))}), {events.Length} events muted");
    }

    // KillPlayer / PlayerDied: skipped for P2 (borrowed health) and for P1 while P2 is alive (Downed: the run ends only
    // when both players are down)
    private static bool SkipWhileBorrowing() => !borrowing && Downed.AllowP1Death();

    private static int lifestealLogs;
    private static bool BeforeEnemyDamaged(PH __instance, Assets.Scripts.Actors.Enemies.Enemy enemy, Assets.Scripts.Actors.DamageContainer dc)
    {
        if (borrowing || !On || enemy == null || p2State.Count == 0 || P2Health.IsDead) return true;
        var ph = P1Health();
        var p2 = P2Movement.Pm;
        var p1 = MyPlayer.Instance;
        if (ph == null || __instance.Pointer != ph.Pointer || p2 == null || p1 == null) return true;
        Vector3 e = enemy.transform.position;
        if ((e - p2.transform.position).sqrMagnitude >= (e - p1.transform.position).sqrMagnitude) return true;   // P1's hit
        // P2's hit: only worth the swap if P2 has lifesteal
        if (P2Inventory.Get(Assets.Scripts.Menu.Shop.EStat.Lifesteal) <= 0f) return false;
        int before = Get<int>("lifestealHeal"), hp0 = Get<int>("hp");
        Borrow("lifesteal", h => h.OnEnemyDamaged(enemy, dc));
        if (lifestealLogs < 6 && (Get<int>("lifestealHeal") != before || Get<int>("hp") != hp0))
        {
            lifestealLogs++;
            ModLog.Info($"[lifesteal] P2's hit on {enemy.enemyData?.enemyName}: lifestealHeal {before} -> {Get<int>("lifestealHeal")}, hp {hp0} -> {Get<int>("hp")} (P2 Lifesteal {P2Inventory.Get(Assets.Scripts.Menu.Shop.EStat.Lifesteal):F2})");
        }
        return false;
    }

    private static PH P1Health() => MyPlayer.Instance?.inventory?.playerHealth;

    // P2's starting state: P1's (same settings), then P2's max values from P2's stats, full HP and shield
    public static void Init()
    {
        var ph = P1Health();
        if (!On || ph == null) return;
        p2State.Clear();
        foreach (var f in fields) p2State[f.Name] = f.GetValue(ph);
        Borrow("init", h =>
        {
            h.overheal = 0f;
            h.UpdateMaxValues();
            foreach (var st in new[] { Assets.Scripts.Menu.Shop.EStat.MaxHealth, Assets.Scripts.Menu.Shop.EStat.Shield, Assets.Scripts.Menu.Shop.EStat.HealthRegen }) h.OnStatUpdate(st);
            h.hp = h.maxHp;
            h.shield = h.maxShield;
        });
        ModLog.Info($"P2GameHealth: P2 starts at {Get<int>("hp")}/{Get<int>("maxHp")} HP, shield {Get<float>("shield"):F0}/{Get<float>("maxShield"):F0}");
    }

    public static T Get<T>(string name) => p2State.TryGetValue(name, out var v) && v is T t ? t : default;
    public static void Set(string name, object value) { if (p2State.ContainsKey(name)) p2State[name] = value; }

    // Runs `call` on P1's PlayerHealth holding P2's state, in P2's stat context; returns false if it failed
    public static bool Borrow(string what, Action<PH> call)
    {
        var ph = P1Health();
        if (!On || ph == null || p2State.Count == 0 || borrowing) return false;
        // P2's stat table ready before the swap (a recompute inside would fire stat events at the swapped health)
        P2Inventory.Get(Assets.Scripts.Menu.Shop.EStat.MaxHealth);
        p1State.Clear();
        foreach (var f in fields) p1State[f.Name] = f.GetValue(ph);
        var muted = new List<(System.Reflection.PropertyInfo p, object v)>();
        // P1's listeners muted; P2's passive's listeners (e.g. Flex stopping a hit, Shadowstep on an evade) hear P2's
        foreach (var e in events)
        {
            var was = e.GetValue(null);
            muted.Add((e, was));
            e.SetValue(null, WithPopups(e, was, P2Abilities.RoutedFor(e)));
        }
        var p1Pm = PlayerMovement.Instance;
        bool entered = false, abilities = false;
        // "The player's" Aegis is P2's for P2's hits (user: P2's Aegis had no effect - the game used P1's shields)
        var aa = MyPlayer.Instance?.playerConstantAttacks;
        var p1Aegis = aa?.aegisAttack;
        bool aegisSwapped = false;
        borrowing = true;
        try
        {
            if (aa != null)
            {
                var p2Aegis = P2Weapons.P2Aegis();
                if ((p2Aegis?.Pointer ?? IntPtr.Zero) != (p1Aegis?.Pointer ?? IntPtr.Zero)) { aa.aegisAttack = p2Aegis; aegisSwapped = true; }
                aegisBefore = p2Aegis != null ? p2Aegis.currentAmount : -1;
            }
            foreach (var f in fields) f.SetValue(ph, p2State[f.Name]);
            entered = P2Inventory.EnterP2();
            abilities = P2Abilities.Enter();
            if (P2Movement.Pm != null) PlayerMovement.Instance = P2Movement.Pm;
            // v0.49 item sweep: effects of P2's items on P2's health events (Holy Book, Mirror, Toxic Barrel, Electric
            // Plug) showed on P1 - P1's object stands at P2's spot, anything attached to it goes to P2
            P2Items.AtP2Spot("health " + what, () => call(ph));
            foreach (var f in fields) p2State[f.Name] = f.GetValue(ph);
            return true;
        }
        catch (Exception e)
        {
            if (errors++ < 5) ModLog.Warn($"P2GameHealth {what} failed: {e.Message}{(aegisSwapped ? " (P2's aegis in place: " + (aa.aegisAttack != null ? "P2's" : "none") + ")" : "")}");
            return false;
        }
        finally
        {
            // TEMP: P2's shield used by this hit
            var used = P2Weapons.P2Aegis();
            if (used != null && aegisBefore >= 0 && used.currentAmount != aegisBefore && aegisLogs++ < 6)
                ModLog.Info($"[aegis] P2's {what}: P2's Aegis shields {aegisBefore} -> {used.currentAmount}");
            if (aegisSwapped) aa.aegisAttack = p1Aegis;
            if (abilities) P2Abilities.Exit();
            if (entered) P2Inventory.Exit();
            if (p1Pm != null) PlayerMovement.Instance = p1Pm;
            foreach (var f in fields) f.SetValue(ph, p1State[f.Name]);
            foreach (var (p, v) in muted) p.SetValue(null, v);
            borrowing = false;
        }
    }

    // v0.49.1 (user: P2's lifesteal showed no "+1", P2's shield hits showed red "-hp" not the blue shield number): the
    // game's popups come from EffectManager's listeners on these events (OnDamage(ph, dc, shieldDamage), OnHeal(ph,
    // value, isShield)) - muted for P2 until now, the mod drew its own red number. They hear P2's events too (popups made
    // while P2's health runs are moved above P2 - P2Popups.MoveToP2). Every listener of P1's events is logged once.
    public static bool GamePopups { get; private set; }
    private static readonly HashSet<string> listenerLogs = new();
    private static object WithPopups(System.Reflection.PropertyInfo e, object p1Listeners, object p2Routed)
    {
        var all = p1Listeners as Il2CppSystem.Delegate;
        if (all == null) return p2Routed;
        if (listenerLogs.Add(e.Name))
            ModLog.Info($"[popup] P1's PlayerHealth.{e.Name} listeners: " + string.Join(", ", all.GetInvocationList().Select(l => { try { return $"{l.Method?.DeclaringType?.Name}.{l.Method?.Name}"; } catch { return "?"; } })));
        Il2CppSystem.Delegate popups = null;
        foreach (var l in all.GetInvocationList())
        {
            string n;
            try { n = l.Method?.DeclaringType?.Name; } catch { continue; }
            if (n != "EffectManager") continue;
            popups = popups == null ? l : Il2CppSystem.Delegate.Combine(popups, l);
        }
        if (popups == null) return p2Routed;
        GamePopups = true;
        var combined = p2Routed is Il2CppSystem.Delegate r ? Il2CppSystem.Delegate.Combine(r, popups) : popups;
        try { return typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod(nameof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase.Cast))!.MakeGenericMethod(e.PropertyType).Invoke(combined, null); }
        catch { return p2Routed; }
    }

    public static void Reset() => p2State.Clear();
}
