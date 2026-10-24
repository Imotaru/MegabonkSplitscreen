using Assets.Scripts.Inventory__Items__Pickups.Items;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// P1's and P2's items apart in the HUD (user: "separate the p1 items from p2 in the hud, i'll let you decide").
// The game's item row (ItemsHud: one ItemsHudElementPrefab per item - icon + amount, Set(ItemData) / SetAmount) sits at
// the bottom centre, across both halves of the split screen. While P2 plays: P1's row moves to the middle of P1's half,
// and P2 gets its own row in the middle of P2's half - a copy of P1's row object (layout only, game script off) holding
// the game's own item cells filled from P2's item inventory. P1's row goes back when P2 isn't playing.
internal static class P2ItemsHud
{
    private static ItemsHud p1Hud;
    private static RectTransform p1Rect, p2Rect;
    private static Vector2 p1Home;
    private static bool shifted, dirty = true;
    private static float nextFind;
    private static int logs;

    public static void MarkDirty() => dirty = true;

    // Each frame in a run (P2Hud.Update)
    public static void Update()
    {
        if (p1Hud == null)
        {
            p2Rect = null;
            shifted = false;
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;
            p1Hud = Object.FindObjectOfType<ItemsHud>();
            if (p1Hud == null) return;
            p1Rect = p1Hud.GetComponent<RectTransform>();
            p1Home = p1Rect != null ? p1Rect.anchoredPosition : Vector2.zero;
            UiDump.DumpOnce("itemshud_dump.txt", p1Hud.transform);   // TEMP: the game's item row as found
        }
        bool p2Playing = PlayerJoin.HasP2 && P2Movement.Active;
        Place(p2Playing);
        if (!p2Playing) { if (p2Rect != null && p2Rect.gameObject.activeSelf) p2Rect.gameObject.SetActive(false); return; }
        if (p2Rect == null) Build();
        if (p2Rect == null) return;
        if (!p2Rect.gameObject.activeSelf) p2Rect.gameObject.SetActive(true);
        if (p2Rect.gameObject.activeSelf != p1Hud.gameObject.activeInHierarchy) p2Rect.gameObject.SetActive(p1Hud.gameObject.activeInHierarchy);
        if (dirty) Fill();
    }

    // Half the canvas width to either side of the middle (the split line)
    private static float Quarter()
    {
        var parent = p1Rect != null ? p1Rect.parent?.GetComponent<RectTransform>() : null;
        return parent != null ? parent.rect.width * 0.25f : Screen.width * 0.25f;
    }

    // v0.49 (screenshot with every item: the full-width rows ran into each other): each row is its half of the screen
    // wide - the row's grid then wraps a long list upwards inside its own half
    private static Vector2 homeMin, homeMax;
    private static void Place(bool split)
    {
        if (p1Rect == null) return;
        if (split && !shifted)
        {
            homeMin = p1Rect.anchorMin;
            homeMax = p1Rect.anchorMax;
            p1Rect.anchorMin = new Vector2(0f, homeMin.y);
            p1Rect.anchorMax = new Vector2(0.5f, homeMax.y);
            p1Rect.anchoredPosition = p1Home;
            shifted = true;
        }
        else if (!split && shifted)
        {
            p1Rect.anchorMin = homeMin;
            p1Rect.anchorMax = homeMax;
            p1Rect.anchoredPosition = p1Home;
            shifted = false;
        }
    }

    private static void Build()
    {
        var holder = new GameObject("MegabonkSplitscreen_ItemsHolder");
        holder.SetActive(false);
        holder.transform.SetParent(p1Rect.parent, false);
        var copy = Object.Instantiate(p1Hud.gameObject, holder.transform);
        copy.name = "MegabonkSplitscreen_P2Items";
        // Layout only: the game's script (it listens to the item events) and P1's cells go
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(mb);
        for (int i = copy.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(copy.transform.GetChild(i).gameObject);
        copy.transform.SetParent(p1Rect.parent, false);
        copy.transform.SetSiblingIndex(p1Rect.GetSiblingIndex() + 1);
        Object.Destroy(holder);
        p2Rect = copy.GetComponent<RectTransform>();
        p2Rect.anchorMin = new Vector2(0.5f, p1Rect.anchorMin.y);
        p2Rect.anchorMax = new Vector2(1f, p1Rect.anchorMax.y);
        p2Rect.anchoredPosition = p1Home;
        copy.SetActive(true);
        dirty = true;
        ModLog.Info($"[itemshud] P2's item row made: P1's row '{p1Hud.name}' at {p1Home} -> {p1Rect.anchoredPosition}, P2's at {p2Rect.anchoredPosition} (canvas quarter {Quarter():F0})");
    }

    private static void Fill()
    {
        dirty = false;
        for (int i = p2Rect.childCount - 1; i >= 0; i--) Object.Destroy(p2Rect.GetChild(i).gameObject);
        var items = P2Inventory.Items?.items;
        var dm = DataManager.Instance;
        if (items == null || dm == null || p1Hud.prefab == null) return;
        var shown = new List<string>();
        foreach (var kv in items)
        {
            if (kv.Value == null || kv.Value.amount <= 0) continue;
            var data = dm.GetItem(kv.Key);
            if (data == null) continue;
            // The prefab is the row's own switched-off template child ('ItemUiPrefab', itemshud_dump.txt)
            var cell = Object.Instantiate(p1Hud.prefab, p2Rect);
            cell.name = "P2_" + kv.Key;
            cell.SetActive(true);
            var el = cell.GetComponent<ItemsHudElementPrefab>();
            try { el?.Set(data); el?.SetAmount(kv.Value.amount); }
            catch (Exception e) { if (logs++ < 3) ModLog.Warn($"[itemshud] P2's {kv.Key} cell: {e.Message}"); }
            shown.Add($"{kv.Key} x{kv.Value.amount}");
        }
        if (logs++ < 10) ModLog.Info($"[itemshud] P2's item row: {(shown.Count == 0 ? "empty" : string.Join(", ", shown))}");
    }

    public static void Reset()
    {
        if (p1Rect != null && shifted) { p1Rect.anchorMin = homeMin; p1Rect.anchorMax = homeMax; p1Rect.anchoredPosition = p1Home; }
        shifted = false;
        if (p2Rect != null) Object.Destroy(p2Rect.gameObject);
        p2Rect = null;
        p1Hud = null;
        dirty = true;
    }
}
