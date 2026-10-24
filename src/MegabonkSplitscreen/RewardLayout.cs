using Assets.Scripts.UI.InGame.Levelup;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Reward screens in splitscreen (user's design): the offers on the side of the player they belong to (P1 left half,
// P2 right half), and the inventory and stats panels on the other half, stacked (inventory on top) and scaled down.
// The real layout (levelup_dump.txt, v0.33.0): under 'EncounterWindows' (full screen) the windows are
//   LevelupScreen/New/W_Offers (level-up, upgrade pickers, shady guy), ChestOpeningWindow/W_Offers,
//   MicrowaveWindow/W_Offers, ItemPickWindow/W_Offers - all anchored at the centre - and
//   EncounterWindow/EncounterWindowNew (charge shrine and other stat rewards);
// and 'InventoryOverlay' holds W_Inventory (anchored left-middle) and 'W_Stats (1)' (right-middle), both with
// ScaleOnAwake (the pop-in animation sets their scale).
// Applied every frame (late) while a screen is open, so it wins over the game's own placement and animation; the
// original layout is kept and put back when P2 isn't playing.
internal static class RewardLayout
{
    private const float TopMargin = 60f, BottomMargin = 40f, SideMargin = 30f, Gap = 16f;
    private const float MinScale = 0.5f, MaxScale = 1f;   // never bigger than the game's own size
    private static readonly string[] OfferPaths =
    {
        "LevelupScreen/New/W_Offers", "ChestOpeningWindow/W_Offers", "MicrowaveWindow/W_Offers",
        "ItemPickWindow/W_Offers", "EncounterWindow/EncounterWindowNew",
    };

    private struct Saved { public Vector2 aMin, aMax, pivot, pos; public Vector3 scale; }
    private static readonly Dictionary<IntPtr, (RectTransform rt, Saved s)> saved = new();
    private static readonly Dictionary<IntPtr, Vector3> lastScaleSet = new();
    private static EncounterWindows windows;
    private static RectTransform inventory, stats;
    private static readonly List<RectTransform> offers = new();
    private static bool logged;

    // Called from Ticker.LateUpdate
    public static void LateUpdate()
    {
        bool split = PlayerJoin.HasP2 && P2Movement.Active;
        if (!split) { RestoreAll(); return; }
        if (windows == null)
        {
            windows = Object.FindObjectOfType<EncounterWindows>();
            if (windows == null) return;
            Find();
        }

        RectTransform open = null;
        foreach (var o in offers) if (o != null && o.gameObject.activeInHierarchy) { open = o; break; }
        bool invOpen = inventory != null && inventory.gameObject.activeInHierarchy;
        bool statsOpen = stats != null && stats.gameObject.activeInHierarchy;
        if (open == null && !invOpen && !statsOpen) return;

        bool p2 = P2LevelUp.P2ScreenOpen || P2Rewards.EncounterIsP2 || P2Interact.ChestIsP2s;
        float own = p2 ? 0.75f : 0.25f, other = p2 ? 0.25f : 0.75f;

        if (open != null)
        {
            Keep(open);
            open.anchorMin = new Vector2(own, open.anchorMin.y);
            open.anchorMax = new Vector2(own, open.anchorMax.y);
            open.anchoredPosition = new Vector2(0f, open.anchoredPosition.y);
        }
        // The other half: inventory and stats as big as the half allows (1.0 alpha, user: make them bigger - at a fixed
        // x0.65 they used a third of the half and the stats text was tiny), at most the game's own size.
        var panels = new List<RectTransform>();
        if (invOpen) panels.Add(inventory);
        if (statsOpen) panels.Add(stats);
        var area = (inventory ?? stats).parent as RectTransform;
        float halfW = (area != null && area.rect.width > 10f ? area.rect.width : 1920f) * 0.5f;
        float fullH = area != null && area.rect.height > 10f ? area.rect.height : 1080f;
        float availW = halfW - 2f * SideMargin, availH = fullH - TopMargin - BottomMargin;
        float sumW = panels.Sum(p => p.rect.width), maxW = panels.Max(p => p.rect.width);
        float sumH = panels.Sum(p => p.rect.height), maxH = panels.Max(p => p.rect.height);
        float gaps = (panels.Count - 1) * Gap;
        // One scale for both, whichever arrangement is bigger (user: pick what keeps both readable - side by side fits
        // a 1920x1080 half at the game's full size, stacked only at ~x0.7)
        float sideBySide = Mathf.Min((availW - gaps) / sumW, availH / maxH);
        float stacked = Mathf.Min(availW / maxW, (availH - gaps) / sumH);
        bool across = sideBySide >= stacked;
        float scale = Mathf.Clamp(Mathf.Max(sideBySide, stacked), MinScale, MaxScale);
        if (across)
        {
            float total = sumW * scale + gaps, x = -total / 2f;
            foreach (var p in panels)
            {
                Place(p, other, x + p.rect.width * scale / 2f, -TopMargin, scale);
                x += p.rect.width * scale + Gap;
            }
        }
        else
        {
            float y = -TopMargin;
            foreach (var p in panels) { Place(p, other, 0f, y, scale); y -= p.rect.height * scale + Gap; }
        }

        if (!logged)
        {
            logged = true;
            ModLog.Info($"Reward screen layout: '{open?.name}' on {(p2 ? "P2's (right)" : "P1's (left)")} half, inventory {invOpen} / stats {statsOpen} " +
                        $"{(across ? "side by side" : "stacked")} on the other half at x{scale:F2} (sizes {string.Join(", ", panels.Select(p => $"{p.rect.width:F0}x{p.rect.height:F0}"))}, half {halfW:F0}x{fullH:F0})");
        }
    }

    private static void Place(RectTransform rt, float x, float offsetX, float top, float scale)
    {
        Keep(rt);
        rt.anchorMin = rt.anchorMax = new Vector2(x, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(offsetX, top);
        // The pop-in animation sets the scale each frame while it runs: scale what it set; once it stops, keep ours
        Vector3 cur = rt.localScale;
        bool ours = lastScaleSet.TryGetValue(rt.Pointer, out var last) && (cur - last).sqrMagnitude <= 0.000001f;
        lastFactor.TryGetValue(rt.Pointer, out float factor);
        if (ours && Mathf.Approximately(factor, scale)) return;
        // ours still in place but the size changed (the other panel opened): swap our factor; else the game set it
        var mine = ours ? cur / Mathf.Max(factor, 0.0001f) * scale : cur * scale;
        rt.localScale = mine;
        lastScaleSet[rt.Pointer] = mine;
        lastFactor[rt.Pointer] = scale;
    }
    private static readonly Dictionary<IntPtr, float> lastFactor = new();

    private static void Find()
    {
        offers.Clear();
        var root = windows.transform;
        foreach (var path in OfferPaths)
        {
            var t = root.Find(path);
            var rt = t != null ? t.GetComponent<RectTransform>() : null;
            if (rt != null) offers.Add(rt);
        }
        var overlay = root.Find("InventoryOverlay");
        inventory = overlay?.Find("W_Inventory")?.GetComponent<RectTransform>();
        stats = overlay?.Find("W_Stats (1)")?.GetComponent<RectTransform>();
        ModLog.Info($"Reward screens found: {offers.Count} offer windows, inventory {(inventory != null)}, stats {(stats != null)}");
    }

    private static void Keep(RectTransform rt)
    {
        if (saved.ContainsKey(rt.Pointer)) return;
        saved[rt.Pointer] = (rt, new Saved { aMin = rt.anchorMin, aMax = rt.anchorMax, pivot = rt.pivot, pos = rt.anchoredPosition, scale = rt.localScale });
    }

    private static void RestoreAll()
    {
        if (saved.Count == 0) return;
        foreach (var (rt, s) in saved.Values)
        {
            if (rt == null) continue;
            rt.anchorMin = s.aMin;
            rt.anchorMax = s.aMax;
            rt.pivot = s.pivot;
            rt.anchoredPosition = s.pos;
            rt.localScale = s.scale;
        }
        saved.Clear();
        lastScaleSet.Clear();
        lastFactor.Clear();
    }

    // New scene: the windows are new objects
    public static void Reset()
    {
        saved.Clear();
        lastScaleSet.Clear();
        lastFactor.Clear();
        windows = null;
        logged = false;
    }
}
