using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Player 2's HUD (user's layout): P2's health bar and weapon list directly beneath P1's, and the objective box
// ("Find the Boss Portal") beneath P2's.
// The game's top-left HUD is one column: 'Inventory' (InventoryHud, VerticalLayoutGroup) holding, top to bottom,
// 'Bars (1)' (HealthUI), 'Weapons', 'Tomes', 'ObjectiveUi' (hud_dump.txt). So the mod copies P1's health bar and
// weapon row and inserts the copies into that column right after 'Tomes': the game's own layout then places them
// beneath P1's block and pushes the objective box down below them.
// (v0.12.0 copied the whole full-screen 'Inventory' object and shifted it by its 1329 px height: off-screen.)
//  - Copies are inert like the P2 character panel: built under an inactive holder, game scripts removed.
//  - Every frame: P2's bar and "hp/max" text from P2Health, P2's weapon icons/levels from P2Weapons, and the copies
//    shown/hidden together with P1's originals.
internal static class P2Hud
{
    private static GameObject healthCopy, weaponsCopy;
    public static Transform P1Bars => p1Health != null ? p1Health.transform : null;   // P1's health/shield bars
    public static Transform P2Bars => healthCopy != null ? healthCopy.transform : null; // P2's mirrored copy
    private static HealthUI p1Health;
    private static InventoryHud p1Inv;
    private static Transform bar;
    private static TextMeshProUGUI hpText;

    private class WeaponSlot
    {
        public GameObject root;
        public GameObject p1;   // P1's matching slot (its on/off = an unlocked, empty slot)
        public RawImage icon;
        public TextMeshProUGUI level;
        public GameObject locked;
    }
    private static readonly List<WeaponSlot> weaponSlots = new();
    private static readonly List<WeaponSlot> tomeSlots = new();
    private static GameObject tomesCopy;

    public static void Start()
    {
        if (healthCopy != null && weaponsCopy != null) return;   // same scene, already made
        try
        {
            Build();
        }
        catch (Exception e)
        {
            ModLog.Error($"P2 HUD build failed: {e}");
        }
    }

    private static void Build()
    {
        p1Health = null;
        foreach (var h in Object.FindObjectsOfType<HealthUI>())
            if (h != null && !h.followPlayer) p1Health = h;
        p1Inv = Object.FindObjectOfType<InventoryHud>();
        if (p1Health == null || p1Inv == null || p1Inv.weaponParent == null)
        {
            ModLog.Warn($"P2 HUD: P1's health bar ({p1Health != null}) or weapon row ({p1Inv?.weaponParent != null}) not found");
            return;
        }

        // v0.32.0 (user): P2's HUD on the right, mirrored from P1's. The whole column ('Inventory': full screen,
        // VerticalLayoutGroup, InventoryHud) is copied next to P1's, keeping only the health bar and the weapon and
        // tome rows (the objective box stays in P1's column), then mirrored: the column and the rows' grids start from
        // the upper right, and the HP bar drains towards the screen edge.
        var column = p1Inv.transform;
        weaponSlots.Clear();
        tomeSlots.Clear();
        Transform M(Component part, GameObject copy) => part != null ? P2Select.Mirror(column, part.transform, copy.transform) : null;
        var panel = Copy(column.gameObject, "MegabonkSplitscreen_P2Hud", column.parent, column.GetSiblingIndex() + 1, copy =>
        {
            healthCopy = M(p1Health, copy)?.gameObject;
            weaponsCopy = M(p1Inv.weaponParent, copy)?.gameObject;
            tomesCopy = M(p1Inv.tomeParent, copy)?.gameObject;
            bar = M(p1Health.healthBar, copy);
            hpText = M(p1Health.t_hp, copy)?.GetComponent<TextMeshProUGUI>();
            foreach (var extra in new[] { p1Health.shieldBar, p1Health.overhealBar })
            {
                var t = M(extra, copy);
                if (t != null) t.localScale = new Vector3(0f, t.localScale.y, t.localScale.z);
            }
            // P2's shield bar: the copied 'ShieldBar' row (its fill and text), shown while P2 has a shield
            shieldFill = M(p1Health.shieldBar, copy);
            shieldText = M(p1Health.t_shield, copy)?.GetComponent<TextMeshProUGUI>();
            shieldRow = shieldFill != null && shieldFill.parent != null && shieldFill.parent != healthCopy?.transform ? shieldFill.parent.gameObject : shieldFill?.gameObject;
            if (weaponsCopy != null) GrabSlots(p1Inv.weaponParent, p1Inv.weaponContainers, weaponsCopy, weaponSlots);
            if (tomesCopy != null) GrabSlots(p1Inv.tomeParent, p1Inv.tomeContainers, tomesCopy, tomeSlots);
            // Only P2's rows stay (no objective box, nothing else of P1's column)
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                var child = copy.transform.GetChild(i).gameObject;
                if (child != healthCopy && child != weaponsCopy && child != tomesCopy) Object.DestroyImmediate(child);
            }
        });
        panelCopy = panel;
        Mirror(panel);
        UiDump.DumpOnce("p2hud_dump.txt", panel.transform);   // TEMP: the mirrored panel as built

        var names = new List<string>();
        for (int i = 0; i < panel.transform.childCount; i++) names.Add(panel.transform.GetChild(i).name);
        ModLog.Info($"P2 HUD: mirrored panel on the right [{string.Join(", ", names)}]; " +
                    $"bar {(bar != null)}, text {(hpText != null)}, {weaponSlots.Count} weapon slots " +
                    $"({weaponSlots.Count(s => s.root.activeSelf)} active), {tomeSlots.Count} tome slots");
    }

    private static GameObject panelCopy;
    private static Transform shieldFill;
    private static GameObject shieldRow;
    private static TextMeshProUGUI shieldText;

    private static void UpdateShield()
    {
        if (shieldRow == null) return;
        bool show = P2Health.MaxShield > 0 && !P2Health.IsDead;
        if (shieldRow.activeSelf != show) shieldRow.SetActive(show);
        if (!show) return;
        float frac = P2Health.Shield / P2Health.MaxShield;
        if (shieldFill != null) shieldFill.localScale = new Vector3(frac, shieldFill.localScale.y, shieldFill.localScale.z);
        if (shieldText != null)
        {
            // P1's text pattern with P2's numbers (first number = current, second = max)
            string src = p1Health?.t_shield != null ? p1Health.t_shield.text : "";
            var ms = System.Text.RegularExpressions.Regex.Matches(src, @"\d+");
            var sb = new System.Text.StringBuilder();
            int at = 0;
            for (int i = 0; i < ms.Count; i++)
            {
                sb.Append(src, at, ms[i].Index - at);
                sb.Append(i == 0 ? Mathf.CeilToInt(P2Health.Shield) : P2Health.MaxShield);
                at = ms[i].Index + ms[i].Length;
            }
            sb.Append(src, at, src.Length - at);
            shieldText.text = ms.Count == 0 ? $"{Mathf.CeilToInt(P2Health.Shield)}/{P2Health.MaxShield}" : sb.ToString();
        }
    }

    private static TextAnchor ToRight(TextAnchor a) => a switch
    {
        TextAnchor.UpperLeft => TextAnchor.UpperRight,
        TextAnchor.MiddleLeft => TextAnchor.MiddleRight,
        TextAnchor.LowerLeft => TextAnchor.LowerRight,
        _ => a,
    };

    // Left-right mirror of the copied column: alignment, padding, grid start corners, HP bar fill side
    private static void Mirror(GameObject panel)
    {
        var parts = new List<string>();
        foreach (var lg in panel.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true))
        {
            if (lg == null) continue;
            lg.childAlignment = ToRight(lg.childAlignment);
            var pad = lg.padding;
            if (pad != null) { int l = pad.left; pad.left = pad.right; pad.right = l; lg.padding = pad; }
            parts.Add($"{lg.name} layout");
        }
        foreach (var g in panel.GetComponentsInChildren<GridLayoutGroup>(true))
        {
            if (g == null) continue;
            g.startCorner = GridLayoutGroup.Corner.UpperRight;
            g.childAlignment = ToRight(g.childAlignment);
            parts.Add($"{g.name} grid");
        }
        // Rows placed by the column's layout keep their anchors on the left; the layout moves them, but anchor them
        // to the right as well so nothing depends on the left edge
        foreach (var row in new[] { healthCopy, weaponsCopy, tomesCopy })
        {
            var rt = row != null ? row.GetComponent<RectTransform>() : null;
            if (rt == null) continue;
            rt.anchorMin = new Vector2(1f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
        }
        // The bar shrinks towards its pivot: P1's from the left, P2's mirrored from the right
        if (bar is RectTransform barRt) barRt.pivot = new Vector2(1f, barRt.pivot.y);
        else if (bar != null) { var brt = bar.GetComponent<RectTransform>(); if (brt != null) brt.pivot = new Vector2(1f, brt.pivot.y); }
        var srt = shieldFill != null ? shieldFill.GetComponent<RectTransform>() : null;   // the shield bar too
        if (srt != null) srt.pivot = new Vector2(1f, srt.pivot.y);
        ModLog.Info($"P2 HUD mirrored: {string.Join(", ", parts)}; shield bar {(shieldRow != null ? shieldRow.name : "not found")}");
    }

    private static void GrabSlots(Transform row, Il2CppSystem.Collections.Generic.List<InventoryItemPrefabUI> containers,
        GameObject copy, List<WeaponSlot> into)
    {
        int n = containers != null ? containers.Count : 0;
        for (int i = 0; i < n; i++)
        {
            var c = containers[i];
            if (c == null) continue;
            var root = Mirror(row, c, copy);
            if (root == null) continue;
            into.Add(new WeaponSlot
            {
                root = root.gameObject,
                p1 = c.gameObject,
                icon = Mirror(row, c.icon, copy)?.GetComponent<RawImage>(),
                level = Mirror(row, c.t_level, copy)?.GetComponent<TextMeshProUGUI>(),
                locked = c.lockedOverlay != null ? Mirror(row, c.lockedOverlay.transform, copy)?.gameObject : null,
            });
        }
    }

    private static void FillSlots(List<WeaponSlot> slots, IList<(Texture icon, int level)> items)
    {
        // v0.42.6 (user/autotest: P2's HUD missed weapons P2 had): the slots that were on when the copy was made were
        // the only ones filled - now a slot is on when P2 has something for it, or when P1's matching slot is on
        // v0.43.7 (user: P2's HUD showed at most 4 tomes while P1's showed more): the copy only had the slots P1 had
        // when it was made - more are added (copies of the last one) when P2 has more items
        while (items.Count > slots.Count && slots.Count > 0 && slots[^1].root != null)
        {
            var last = slots[^1];
            var extra = Object.Instantiate(last.root, last.root.transform.parent);
            extra.name = last.root.name + "_more";
            slots.Add(new WeaponSlot
            {
                root = extra,
                icon = extra.GetComponentsInChildren<RawImage>(true).FirstOrDefault(r => last.icon != null && r.name == last.icon.name),
                level = extra.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => last.level != null && t.name == last.level.name),
                locked = null,
            });
        }
        int w = 0;
        foreach (var s in slots)
        {
            if (s.root == null) continue;
            bool want = w < items.Count || (s.p1 != null && s.p1.activeSelf);
            if (s.root.activeSelf != want) s.root.SetActive(want);
            if (!want) continue;
            bool has = w < items.Count;
            if (s.icon != null)
            {
                s.icon.enabled = has;
                if (has) s.icon.texture = items[w].icon;
            }
            if (s.level != null) s.level.text = has ? $"LVL {items[w].level}" : "";
            if (s.locked != null) s.locked.SetActive(false);
            w++;
        }
    }

    private static Transform Mirror(Transform srcRoot, Component part, GameObject copy) =>
        part != null ? P2Select.Mirror(srcRoot, part.transform, copy.transform) : null;

    // Inert copy of src, inserted into `parent` at `index`. `grab` finds the parts we drive before game scripts go.
    private static GameObject Copy(GameObject src, string name, Transform parent, int index, Action<GameObject> grab)
    {
        var holder = new GameObject("MegabonkSplitscreen_HudHolder");
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);
        var copy = Object.Instantiate(src, holder.transform);
        copy.name = name;
        grab(copy);
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetIl2CppType().Assembly.GetName().Name != "Assembly-CSharp") continue;
            Object.DestroyImmediate(mb);
        }
        copy.transform.SetParent(parent, false);
        copy.transform.SetSiblingIndex(index);
        Object.Destroy(holder);
        copy.SetActive(src.activeSelf);
        return copy;
    }

    // P2's items changed (P2Items): P2's item row is refilled
    public static void ItemsChanged() => P2ItemsHud.MarkDirty();

    // Called every frame from P2Body.Update.
    public static void Update()
    {
        P2Body.Sub("hud: items row", P2ItemsHud.Update);
        P2Body.Sub("hud: chest price", P2ChestPrice.Update);
        P2Body.Sub("hud: item fx", P2ItemFx.Update);
        if (healthCopy == null || p1Health == null) return;
        P2Body.Sub("hud: own", UpdateOwn);
    }

    private static void UpdateOwn()
    {

        if (healthCopy.activeSelf != p1Health.gameObject.activeSelf) healthCopy.SetActive(p1Health.gameObject.activeSelf);
        if (weaponsCopy != null && p1Inv != null && p1Inv.weaponParent != null
            && weaponsCopy.activeSelf != p1Inv.weaponParent.gameObject.activeSelf)
            weaponsCopy.SetActive(p1Inv.weaponParent.gameObject.activeSelf);

        float frac = P2Health.MaxHp > 0 ? (float)P2Health.Hp / P2Health.MaxHp : 0f;
        if (bar != null) bar.localScale = new Vector3(frac, bar.localScale.y, bar.localScale.z);
        if (hpText != null) hpText.text = P2Health.IsDead ? "DOWN" : $"{P2Health.Hp}/{P2Health.MaxHp}";

        // Fill the visible slots in order with P2's weapons and tomes
        P2Body.Sub("hud: slots", () => FillAllSlots());
        if (panelCopy != null) P2Body.Sub("hud: gold", () => P2Gold.UpdateHud(panelCopy.transform));
        P2Body.Sub("hud: shield", UpdateShield);
    }

    private static void FillAllSlots()
    {
        FillSlots(weaponSlots, P2Weapons.Levels.Select(x => ((Texture)x.data.icon, x.level)).ToList());
        if (tomesCopy != null && p1Inv?.tomeParent != null && tomesCopy.activeSelf != p1Inv.tomeParent.gameObject.activeSelf)
            tomesCopy.SetActive(p1Inv.tomeParent.gameObject.activeSelf);
        var levels = P2Inventory.TomesInv?.tomeLevels;
        FillSlots(tomeSlots, P2Inventory.Tomes.Select(t => ((Texture)t.icon, levels != null && levels.ContainsKey(t.eTome) ? levels[t.eTome] : 1)).ToList());
    }
}
