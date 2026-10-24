using Assets.Scripts.Actors.Player;
using Assets.Scripts.Saves___Serialization.Progression.Stats;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// P2 on the run's end screen (user: give P2 its own summary - character, kills, level, inventory, damage).
// The game's end screen (DeathScreen.statsWindow) has DAMAGE (GameOverDamageSourcesUi: RunStats.damageSources, one
// DamageSourceEntry per source), SUMMARY (DeathStatsUi: portrait, name, "Kills / Time Alive / LVL", weapons/tomes/items
// from ShowInventory) and QUESTS - all P1's.
//  - Damage: RunStats.OnEnemyDamaged adds each hit to the shared list by source name; a hit that's P2's (P2Items.
//    HitOwnerOf) is moved to P2's own list, so P1's DAMAGE shows P1's damage only.
//  - P2's panels: copies of the game's DAMAGE and SUMMARY panels, filled with P2's: the game's DamageSourceEntry.Set on
//    P2's list; the game's ShowInventory with P2's weapon/tome/item holders in P1's inventory for that call; P2's
//    character, kills (P2Pickups.KillsP2) and level in P1's text format.
internal static class P2EndScreen
{
    private static readonly Dictionary<string, DamageSource> p2Damage = new();
    public static IReadOnlyDictionary<string, DamageSource> P2Damage => p2Damage;

    public static void Apply(Harmony harmony)
    {
        int n = 0;
        n += Hook(harmony, AccessTools.DeclaredMethod(typeof(RunStats), nameof(RunStats.OnEnemyDamaged)), nameof(BeforeDamaged), nameof(AfterDamaged));
        n += Hook(harmony, AccessTools.DeclaredMethod(typeof(DeathStatsUi), "Start"), null, nameof(AfterSummaryStart));
        n += Hook(harmony, AccessTools.DeclaredMethod(typeof(GameOverDamageSourcesUi), "Start"), null, nameof(AfterDamageStart));
        ModLog.Info($"P2EndScreen: {n} of 3 hooks");
    }

    private static int Hook(Harmony harmony, System.Reflection.MethodBase m, string pre, string post)
    {
        if (m == null || !P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) { ModLog.Warn($"P2EndScreen: {m?.DeclaringType?.Name}.{m?.Name} not hooked"); return 0; }
        harmony.Patch(m, prefix: pre != null ? new HarmonyMethod(typeof(P2EndScreen), pre) : null, postfix: post != null ? new HarmonyMethod(typeof(P2EndScreen), post) : null);
        return 1;
    }

    public static void Reset() { p2Damage.Clear(); built = 0; }

    // ---------- P2's damage ----------
    private static void BeforeDamaged(Assets.Scripts.Actors.Enemies.Enemy enemy, Assets.Scripts.Actors.DamageContainer dc, out float __state)
    {
        __state = -1f;
        if (dc == null || P2Inventory.Items == null || string.IsNullOrEmpty(dc.damageSource)) return;
        if (P2Items.HitOwnerOf(enemy) != 2) return;
        var all = RunStats.damageSources;
        __state = all != null && all.ContainsKey(dc.damageSource) ? all[dc.damageSource].damage : 0f;
    }

    private static int moveLogs;
    private static void AfterDamaged(Assets.Scripts.Actors.DamageContainer dc, float __state)
    {
        if (__state < 0f || dc == null) return;
        var all = RunStats.damageSources;
        string src = dc.damageSource;
        if (all == null || !all.ContainsKey(src)) return;
        var shared = all[src];
        float added = shared.damage - __state;
        if (added <= 0f) return;
        if (!p2Damage.TryGetValue(src, out var mine))
        {
            mine = new DamageSource(src, shared.addedAtTime);
            p2Damage[src] = mine;
        }
        mine.AddDamage(added);
        shared.damage = __state;
        if (shared.damage <= 0f) all.Remove(src);   // only P2 used it: not in P1's list
        if (moveLogs++ < 5) ModLog.Info($"[endscreen] P2's '{src}' hit ({added:F1}) counted for P2 (P2 total {mine.damage:F0})");
    }

    // ---------- P2's panels ----------
    private static int built;

    private static void AfterSummaryStart(DeathStatsUi __instance)
    {
        if (!PlayerJoin.HasP2 || __instance == null) return;
        try
        {
            UiDump.DumpOnce("endscreen_dump.txt", __instance.transform.root);
            var copy = CopyPanel(__instance.gameObject, out var holder);
            var ui = copy.GetComponent<DeathStatsUi>();
            // P2's inventory by the game's own code: P2's holders in P1's inventory for the call
            // P1's filled-in entries go (the game's own template, a switched-off child, stays - ShowInventory copies it)
            foreach (var parent in new[] { ui.weaponsParent, ui.tomesParent, ui.itemsParent }) ClearClones(parent);
            var inv = MyPlayer.Instance?.inventory;
            if (inv != null)
            {
                var w = inv.weaponInventory; var t = inv.tomeInventory; var it = inv.itemInventory;
                try
                {
                    if (P2Inventory.Weapons != null) inv.weaponInventory = P2Inventory.Weapons;
                    if (P2Inventory.TomesInv != null) inv.tomeInventory = P2Inventory.TomesInv;
                    if (P2Inventory.Items != null) inv.itemInventory = P2Inventory.Items;
                    ui.ShowInventory();
                }
                finally { inv.weaponInventory = w; inv.tomeInventory = t; inv.itemInventory = it; }
            }
            var ch = P2Inventory.P2Character;
            if (ui.t_characterName != null && ch != null) ui.t_characterName.text = Try(() => ch.GetName(), ch.eCharacter.ToString());
            if (ui.i_character != null && ch != null) ui.i_character.texture = ch.GetIcon();
            if (ui.t_stats != null && __instance.t_stats != null) ui.t_stats.text = P2StatsText(__instance.t_stats.text);
            // P1's "Kills" was both players' (the game counts one total): P1's own = the total minus P2's
            if (__instance.t_stats != null) __instance.t_stats.text = P1StatsText(__instance.t_stats.text);
            // shared run results stay on P1's panel only
            foreach (var shared in new Component[] { ui.t_gold, ui.t_unlocks, ui.t_silver })
                if (shared != null) shared.gameObject.SetActive(false);
            Object.DestroyImmediate(ui);   // its Start would fill P1's again
            Finish(copy, holder, __instance.gameObject, "SUMMARY");
            ModLog.Info($"[endscreen] P2's summary: {ch?.eCharacter}, '{P2StatsText(__instance.t_stats?.text ?? "")}'");
        }
        catch (Exception e) { ModLog.Error($"[endscreen] P2's summary failed: {e}"); }
    }

    private static void AfterDamageStart(GameOverDamageSourcesUi __instance)
    {
        if (!PlayerJoin.HasP2 || __instance == null) return;
        try
        {
            var copy = CopyPanel(__instance.gameObject, out var holder);
            var ui = copy.GetComponent<GameOverDamageSourcesUi>();
            var parent = ui.damageSourceParent;
            if (parent != null)
            {
                ClearClones(parent);   // P1's rows; the header row and the (switched-off) row template stay
                foreach (var ds in p2Damage.Values.OrderByDescending(d => d.damage))
                {
                    var go = Object.Instantiate(ui.damageSourcePrefab, parent);
                    go.SetActive(true);   // the template is a switched-off child
                    go.GetComponent<DamageSourceEntry>()?.Set(ds);
                }
            }
            Object.DestroyImmediate(ui);
            Finish(copy, holder, __instance.gameObject, "DAMAGE");
            ModLog.Info($"[endscreen] P2's damage: {string.Join(", ", p2Damage.Values.Select(d => $"{d.damageSource} {d.damage:F0}"))}");
        }
        catch (Exception e) { ModLog.Error($"[endscreen] P2's damage failed: {e}"); }
    }

    private static void ClearClones(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var ch = parent.GetChild(i);
            if (ch.name.Contains("(Clone)")) Object.DestroyImmediate(ch.gameObject);
        }
    }

    // A copy made under an inactive holder (no Awake/Start), placed next to the original afterwards
    private static GameObject CopyPanel(GameObject original, out GameObject holder)
    {
        holder = new GameObject("MegabonkSplitscreen_EndHolder");
        holder.SetActive(false);
        holder.transform.SetParent(original.transform.parent, false);
        var copy = Object.Instantiate(original, holder.transform);
        copy.name = original.name + "_P2";
        return copy;
    }

    private static void Finish(GameObject copy, GameObject holder, GameObject original, string what)
    {
        // P2's panel is for looking only: no window script (navigation, opening animation), no Confirm/back buttons
        foreach (var mb in copy.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetIl2CppType().Name == "Window") Object.DestroyImmediate(mb);
        foreach (var name in new[] { "B_Continue", "B_Back" })
            foreach (var t in copy.GetComponentsInChildren<Transform>(true).Where(x => x != null && x.name == name).ToList())
                Object.DestroyImmediate(t.gameObject);
        copy.transform.SetParent(original.transform.parent, false);
        copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
        Object.Destroy(holder);
        built++;
        Layout.Arrange(original, copy, what);
    }

    // P1's text with P2's numbers: "Kills: 4 ... LVL 0" -> P2's kills and level
    private static string P2StatsText(string p1)
    {
        int level = Inventory__Items__Pickups.XpUtility.XpToLevel(P2XpBar.Xp);
        string s = System.Text.RegularExpressions.Regex.Replace(p1, @"(Kills:\s*)\d[\d,.]*", "${1}" + P2Pickups.KillsP2);
        return System.Text.RegularExpressions.Regex.Replace(s, @"(LVL\s*)\d+", "${1}" + level);
    }

    private static string P1StatsText(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"Kills:\s*(\d+)");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int total)) return text;
        return System.Text.RegularExpressions.Regex.Replace(text, @"(Kills:\s*)\d+", "${1}" + Mathf.Max(0, total - P2Pickups.KillsP2));
    }

    private static string Try(Func<string> f, string fallback) { try { return f(); } catch { return fallback; } }

    // ---------- Where the panels go ----------
    // First pass (layout not seen yet - endscreen_dump.txt): each copy right after its original in the same parent,
    // P1's and P2's titles set
    private static class Layout
    {
        // The window row (StatsWindows, a HorizontalLayoutGroup: DAMAGE 560x930, SUMMARY 700x930, QUESTS) gets one column
        // per pair, the size of P1's panel: P1's panel in its top half, P2's in the bottom half
        private const float Gap = 10f;
        public static void Arrange(GameObject p1Panel, GameObject p2Panel, string what)
        {
            SetTitle(p1Panel, what, "PLAYER 1 " + what);
            SetTitle(p2Panel, what, "PLAYER 2 " + what);
            var rt1 = p1Panel.transform.TryCast<RectTransform>();
            var rt2 = p2Panel.transform.TryCast<RectTransform>();
            var row = p1Panel.transform.parent;
            if (rt1 == null || rt2 == null || row == null) return;
            // SUMMARY: P2's full-height panel right after P1's (in halves P1's Confirm was cut off and a long run's
            // inventory wouldn't fit); the row is scaled to the screen width once both pairs are in
            if (what == "SUMMARY") { FitRow(row); return; }
            Vector2 size = rt1.sizeDelta;
            if (size.x < 1f || size.y < 1f) size = what == "DAMAGE" ? new Vector2(560f, 930f) : new Vector2(700f, 930f);   // before the first layout pass
            var col = new GameObject("MegabonkSplitscreen_EndColumn_" + what);
            var crt = col.AddComponent<RectTransform>();
            crt.SetParent(row, false);
            crt.SetSiblingIndex(p1Panel.transform.GetSiblingIndex());
            crt.sizeDelta = size;
            var le = col.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = size.x;
            le.preferredHeight = le.minHeight = size.y;
            Place(rt1, crt, top: true);
            Place(rt2, crt, top: false);
            ModLog.Info($"[endscreen] {what}: P1 and P2 panels stacked in a {size.x:F0}x{size.y:F0} column (row '{row.name}')");
            FitRow(row);
        }

        // The whole window row scaled down to fit the screen (its layout lays the windows out wider than the screen)
        public static void FitRow(Transform row)
        {
            var rrt = row.TryCast<RectTransform>();
            if (rrt == null) return;
            var group = row.GetComponent<HorizontalLayoutGroup>();
            float total = 0f; int n = 0;
            for (int i = 0; i < row.childCount; i++)
            {
                var ch = row.GetChild(i);
                if (!ch.gameObject.activeSelf) continue;
                var rt = ch.TryCast<RectTransform>();
                var le = ch.GetComponent<LayoutElement>();
                float w = le != null && le.preferredWidth > 0f ? le.preferredWidth : rt != null ? rt.sizeDelta.x : 0f;
                if (w < 1f) w = ch.name.StartsWith("W_Stats") ? 700f : 560f;   // before the first layout pass
                total += w; n++;
            }
            if (group != null) total += group.spacing * Mathf.Max(0, n - 1) + group.padding.left + group.padding.right;
            float avail = rrt.rect.width > 1f ? rrt.rect.width : 1920f;
            float k = Mathf.Min(1f, (avail - 40f) / Mathf.Max(1f, total));
            rrt.localScale = new Vector3(k, k, 1f);
            // centred: the windows were laid out from the row's left edge (centre alignment didn't centre the overflow -
            // QUESTS was cut off), so the row is moved by where its windows really are after a layout pass
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rrt);
                float minX = float.MaxValue, maxX = float.MinValue;
                for (int i = 0; i < row.childCount; i++)
                {
                    var rt = row.GetChild(i).TryCast<RectTransform>();
                    if (rt == null || !rt.gameObject.activeSelf) continue;
                    // the windows are anchored at the row's lower-left corner (anchors 0,0): x from the left edge
                    float left = rt.anchoredPosition.x - rt.rect.width * rt.pivot.x;
                    minX = Mathf.Min(minX, left);
                    maxX = Mathf.Max(maxX, left + rt.rect.width);
                }
                if (minX < maxX)
                {
                    float off = (minX + maxX) / 2f - avail / 2f;   // the windows' middle vs the row's middle
                    rrt.anchoredPosition = new Vector2(-off * k, rrt.anchoredPosition.y);
                    ModLog.Info($"[endscreen] windows span {minX:F0}..{maxX:F0} of {avail:F0}: row moved {-off * k:F0}");
                }
            }
            catch (Exception e) { ModLog.Warn($"[endscreen] centring the row failed: {e.Message}"); }
            ModLog.Info($"[endscreen] window row: {n} windows, {total:F0} wide -> scale {k:F2}");
        }

        private static void Place(RectTransform panel, RectTransform col, bool top)
        {
            panel.SetParent(col, false);
            panel.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            panel.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.offsetMin = new Vector2(0f, top ? Gap : 0f);
            panel.offsetMax = new Vector2(0f, top ? 0f : -Gap);
        }
        // The panel's header title text (the text equal to `what`, any case) inside the panel
        private static void SetTitle(GameObject panel, string what, string text)
        {
            foreach (var tmp in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (tmp != null && tmp.text != null && tmp.text.Trim().Equals(what, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var mb in tmp.GetComponents<MonoBehaviour>())
                        if (mb != null && mb.GetIl2CppType().Name == "LocalizeStringEvent") Object.DestroyImmediate(mb);
                    tmp.text = text;
                    return;
                }
            ModLog.Info($"[endscreen] no '{what}' title inside '{panel.name}' (the header is outside the panel)");
        }
    }
}
