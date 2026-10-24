using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using UnityEngine;

namespace MegabonkSplitscreen;

// Autotest: every chest item on P1 alone, then on P2 alone, in the same scripted fight - the results must match
// (user: "make tests that test every chest reward on both players to make sure they are the same").
// Per item X the script takes snapshots: X.A0 before P1 gets it, X.A1 right after, X.A2 after the fight; X.B0-B2 the
// same for P2. 'icheck X' then compares:
//  - stats the item gives (A1-A0 for P1 vs B1-B0 for P2) and, during the fight (stacking items), A2-A1 vs B2-B1;
//  - leaks: the player WITHOUT the item must not change (P2 in A, P1 in B);
//  - damage from non-weapon sources (the item's own damage, RunStats.damageSources) in A vs B;
//  - the holder's gold and HP change.
internal static class ItemCheck
{
    private class Snap
    {
        public readonly Dictionary<EStat, (float p1, float p2)> stats = new();
        public readonly Dictionary<string, float> dmg = new();
        public int hp1, hp2;
        public float gold1, gold2;
        public Vector3 pos1, pos2;
        public HashSet<string> fx1 = new(), fx2 = new();   // shown effects attached to each player (names)
    }

    // Active particle systems and renderers under a player's object (user: some item effects don't show on P2)
    private static HashSet<string> Fx(Transform root)
    {
        var s = new HashSet<string>();
        if (root == null) return s;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(false)) if (ps != null && ps.isPlaying && !PickupFx(ps.name)) s.Add(Clean(ps.name));
        foreach (var r in root.GetComponentsInChildren<Renderer>(false)) if (r != null && r.enabled && r.TryCast<SkinnedMeshRenderer>() == null && !PickupFx(r.name)) s.Add(Clean(r.name));
        return s;
    }
    private static string Clean(string n) => n.Replace("(Clone)", "").Trim();
    // Effects of picking things up (gold, xp, power-ups) - not the item's
    private static bool PickupFx(string n) => n.Contains("Pickup") || n.StartsWith("Sparkle");
    private static readonly Dictionary<string, Snap> snaps = new();

    public static string Take(string label)
    {
        var s = new Snap();
        var ps = MyPlayer.Instance?.inventory?.playerStats;
        // v0.49 sweep (Grandma's Tonic): P1's table can lag behind an item change - fresh before reading
        try { ps?.ForceUpdateStats(); } catch { }
        foreach (EStat st in Enum.GetValues(typeof(EStat)))
            s.stats[st] = (ps != null ? ps.GetStat(st) : 0f, P2Inventory.Get(st));
        var d = Assets.Scripts.Saves___Serialization.Progression.Stats.RunStats.damageSources;
        if (d != null) foreach (var kv in d) s.dmg[kv.Key] = kv.Value != null ? kv.Value.damage : 0f;
        var ph = MyPlayer.Instance?.inventory?.playerHealth;
        s.hp1 = ph != null ? ph.hp : 0;
        s.hp2 = P2Health.Hp;
        s.gold1 = MyPlayer.Instance?.inventory?.gold ?? 0f;
        s.gold2 = P2Gold.Amount;
        s.pos1 = MyPlayer.Instance != null ? MyPlayer.Instance.transform.position : Vector3.zero;
        s.pos2 = P2Movement.Pm != null ? P2Movement.Pm.transform.position : Vector3.zero;
        s.fx1 = Fx(MyPlayer.Instance != null ? MyPlayer.Instance.transform : null);
        s.fx2 = Fx(P2Weapons.Body != null ? P2Weapons.Body.transform : null);
        snaps[label] = s;
        return $"snapshot {label}";
    }

    public static string Remove(int pi, EItem e)
    {
        int n = 0;
        if (pi == 0)
        {
            var inv = MyPlayer.Instance?.inventory?.itemInventory;
            while (inv != null && inv.GetAmount(e) > 0 && n < 50) { inv.RemoveItem(e, false); n++; }
            // v0.49 sweep (Grandma's Tonic: P1's crit dropped only later, flagged as a leak): P1's stats now
            try { MyPlayer.Instance?.inventory?.playerStats?.ForceUpdateStats(); } catch { }
        }
        else if (P2Inventory.Items != null)
        {
            P2Inventory.AsP2("test unitem", items: true, stats: false, () =>
            {
                var inv = MyPlayer.Instance.inventory.itemInventory;
                while (inv.GetAmount(e) > 0 && n < 50) { inv.RemoveItem(e, false); n++; }
            });
        }
        return $"P{pi + 1} lost item {e} (x{n}): P1 [{P2Items.DescribeP1()}], P2 [{P2Items.Describe(P2Inventory.Items)}]";
    }

    private static readonly HashSet<string> Weapons = new(Enum.GetNames(typeof(EWeapon)), StringComparer.OrdinalIgnoreCase);

    public static string Check(string item)
    {
        string k = item;
        if (!snaps.TryGetValue(k + ".A0", out var a0) || !snaps.TryGetValue(k + ".A1", out var a1) || !snaps.TryGetValue(k + ".A2", out var a2) ||
            !snaps.TryGetValue(k + ".B0", out var b0) || !snaps.TryGetValue(k + ".B1", out var b1) || !snaps.TryGetValue(k + ".B2", out var b2))
            return $"icheck {item}: SNAPSHOTS MISSING";
        var problems = new List<string>();
        var notes = new List<string>();

        // Stats the item gives: P1's change in A vs P2's change in B; the other player unchanged
        var given = new List<string>();
        CompareStats("gives", a0, a1, b0, b1, problems, given, a0, a2, b0, b2);
        var fight = new List<string>();
        CompareStats("in the fight", a1, a2, b1, b2, problems, fight, a0, a2, b0, b2);
        if (given.Count > 0) notes.Add("gives " + string.Join(", ", given));
        if (fight.Count > 0) notes.Add("fight " + string.Join(", ", fight));

        // Non-weapon damage (the item's own)
        var sources = a2.dmg.Keys.Concat(b2.dmg.Keys).Distinct().Where(s => !Weapons.Contains(s)).ToList();
        var dmgParts = new List<string>();
        foreach (var src in sources)
        {
            float da = Get(a2.dmg, src) - Get(a1.dmg, src), db = Get(b2.dmg, src) - Get(b1.dmg, src);
            if (da < 0.5f && db < 0.5f) continue;
            string v = da >= 0.5f && db >= 0.5f ? (db / da < 0.33f || db / da > 3f ? "uneven" : "ok") : da >= 0.5f ? "P2 MISSING" : "P1 MISSING";
            // Chance-based procs in a 5 s fight: only a clear one-sided result (40+ damage against none) counts
            bool clear = Mathf.Max(da, db) >= 40f;
            if (!clear && v != "ok") v += " (few procs)";
            dmgParts.Add($"{src} P1 {da:F0} / P2 {db:F0} {v}");
            if (clear && v.EndsWith("MISSING")) problems.Add($"damage {src} {v}");
        }
        if (dmgParts.Count > 0) notes.Add("damage " + string.Join(", ", dmgParts));
        if (given.Count == 0 && fight.Count == 0 && dmgParts.Count == 0) notes.Add("NOTHING SEEN (no stat or damage change for either player in this fight)");

        // Effects the item put on its holder (new since before it was given, right after or after the fight), and on the
        // wrong player
        var fxA = a1.fx1.Union(a2.fx1).Except(a0.fx1).ToHashSet();
        var fxB = b1.fx2.Union(b2.fx2).Except(b0.fx2).ToHashSet();
        var wrongA = a1.fx2.Union(a2.fx2).Except(a0.fx2).ToHashSet();   // P1's item -> on P2
        var wrongB = b1.fx1.Union(b2.fx1).Except(b0.fx1).ToHashSet();   // P2's item -> on P1
        if (fxA.Count > 0 || fxB.Count > 0) notes.Add($"fx P1 [{string.Join(", ", fxA)}] / P2 [{string.Join(", ", fxB)}]");
        var missing = fxA.Except(fxB).Except(wrongB).ToList();
        if (missing.Count > 0) problems.Add($"fx on P1 but not on P2: {string.Join(", ", missing)}");
        var onP1 = wrongB.Except(wrongA).ToList();
        if (onP1.Count > 0) problems.Add($"P2's item fx shown on P1: {string.Join(", ", onP1)}");

        // Holder's gold and HP (both players are hurt the same in the script; a difference = the item's healing)
        float g1 = a2.gold1 - a0.gold1, g2 = b2.gold2 - b0.gold2;
        if (Mathf.Abs(g1) >= 1f || Mathf.Abs(g2) >= 1f) notes.Add($"gold P1 {g1:+0;-0} / P2 {g2:+0;-0}");
        int h1 = a2.hp1 - a1.hp1, h2 = b2.hp2 - b1.hp2, o1 = a2.hp2 - a1.hp2, o2 = b2.hp1 - b1.hp1;
        notes.Add($"hp holder P1 {h1:+0;-0} / P2 {h2:+0;-0} (other player {o1:+0;-0} / {o2:+0;-0})");
        // The holder's way through the fight (walking items: Golden Sneakers, Campfire...): an uneven walk explains them
        notes.Add($"moved P1 {Vector3.Distance(a1.pos1, a2.pos1):F0} m / P2 {Vector3.Distance(b1.pos2, b2.pos2):F0} m");

        string verdict = problems.Count == 0 ? "OK" : "PROBLEM " + string.Join("; ", problems);
        return $"icheck {item}: {verdict} | {string.Join(" | ", notes)}";
    }

    private static float Get(Dictionary<string, float> d, string k) => d.TryGetValue(k, out var v) ? v : 0f;

    private static bool Same(float x, float y) => Mathf.Abs(x - y) <= 0.002f + 0.01f * Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));

    // A: P1 holds (P1 changes, P2 must not); B: P2 holds (P2 changes, P1 must not)
    private static void CompareStats(string when, Snap a0, Snap a1, Snap b0, Snap b1, List<string> problems, List<string> notes, Snap phaseAStart, Snap phaseAEnd, Snap phaseBStart, Snap phaseBEnd)
    {
        foreach (EStat st in Enum.GetValues(typeof(EStat)))
        {
            float holderA = a1.stats[st].p1 - a0.stats[st].p1, otherA = a1.stats[st].p2 - a0.stats[st].p2;
            float holderB = b1.stats[st].p2 - b0.stats[st].p2, otherB = b1.stats[st].p1 - b0.stats[st].p1;
            if (!Same(otherA, 0f)) problems.Add($"LEAK {when}: P1's item moved P2's {st} by {otherA:+0.###;-0.###}");
            // v0.49 sweep: P1's stacks from phase A (Demonic Blood, Tonic...) linger after the test takes the item away
            // and drop during phase B - exactly minus what P1 had gained: the test's removal, not P2's item
            float p1GainedInA = phaseAEnd.stats[st].p1 - phaseAStart.stats[st].p1;
            bool leftover = !Same(otherB, 0f) && Same(otherB, -p1GainedInA);
            if (leftover) notes.Add($"(P1's {st} {otherB:+0.###;-0.###}: P1's removed item's leftover)");
            else if (!Same(otherB, 0f)) problems.Add($"LEAK {when}: P2's item moved P1's {st} by {otherB:+0.###;-0.###}");
            if (Same(holderA, 0f) && Same(holderB, 0f)) continue;
            bool same = Same(holderA, holderB);
            notes.Add($"{st} P1 {holderA:+0.###;-0.###} / P2 {holderB:+0.###;-0.###}{(same ? "" : " DIFFERENT")}");
            // Random/time-based stacks during the fight may differ a little: only the item's own stats must match - and
            // an item that updates on a timer (Spiky Shield's Thorns) may land before or after the snapshot: the whole
            // phase (gives + fight) is compared before calling it different
            if (!same && when == "gives")
            {
                float wholeA = phaseAEnd.stats[st].p1 - phaseAStart.stats[st].p1;
                float wholeB = phaseBEnd.stats[st].p2 - phaseBStart.stats[st].p2;
                if (!Same(wholeA, wholeB)) problems.Add($"{st} {when} differs");
            }
        }
    }

    // idelta <from> <to>: each player's stat changes between two snapshots
    public static string Delta(string from, string to)
    {
        if (!snaps.TryGetValue(from, out var a) || !snaps.TryGetValue(to, out var b)) return $"idelta {from} {to}: SNAPSHOTS MISSING";
        var p1 = new List<string>();
        var p2 = new List<string>();
        foreach (EStat st in Enum.GetValues(typeof(EStat)))
        {
            float d1 = b.stats[st].p1 - a.stats[st].p1, d2 = b.stats[st].p2 - a.stats[st].p2;
            if (!Same(d1, 0f)) p1.Add($"{st} {d1:+0.###;-0.###}");
            if (!Same(d2, 0f)) p2.Add($"{st} {d2:+0.###;-0.###}");
        }
        return $"idelta {from} -> {to}: P1 [{string.Join(", ", p1)}], P2 [{string.Join(", ", p2)}], gold P1 {b.gold1 - a.gold1:+0;-0} / P2 {b.gold2 - a.gold2:+0;-0}";
    }

    public static void Reset() => snaps.Clear();
}
