using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// The "$gold" popup when a player earns gold (user: it shows for P1 on P2's screen - add one for P2 and move P1's).
// The game's popup is HUD 'T_Gold' (XpAndGoldText, prefix "$", anchored at the screen centre, +62 px right): its
// Add(amount) is called on each gold gain and it fades out by itself. In split screen the centre is P2's half.
//  - Placement: each player's popup at the same spot relative to the middle of that player's view - P1's half / P2's
//    half while split, gliding to the shared centre as the views merge (P2's then just below P1's).
//  - P2's popup: a copy of the game's (its own XpAndGoldText, so it counts and fades the same way).
//  - Whose gold: an Add while P2's gold is swapped in (P2's kill gold runs the game's ChangeGold on P2's purse) goes
//    to P2's popup; a coin first paid to P1 and then moved to P2 (P2Gold.Collected) is taken off P1's and added to P2's.
internal static class GoldPopups
{
    private const float MergedGapY = 40f;

    private static XpAndGoldText p1Popup, p2Popup;
    private static RectTransform p1Rt, p2Rt;
    private static Vector2 home;   // the game's own spot for P1's (relative to the screen centre)

    public static void Apply(Harmony harmony)
    {
        var m = AccessTools.DeclaredMethod(typeof(XpAndGoldText), nameof(XpAndGoldText.Add));
        if (m == null || !P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) { ModLog.Warn("GoldPopups: XpAndGoldText.Add not hookable - P1's popup shows both players' gold"); return; }
        harmony.Patch(m, prefix: new HarmonyMethod(typeof(GoldPopups), nameof(BeforeAdd)));
        ModLog.Info("GoldPopups: gold popups per player");
    }

    private static bool redirecting;
    private static bool BeforeAdd(XpAndGoldText __instance, int amount)
    {
        if (redirecting || __instance == null || p1Popup == null || p2Popup == null) return true;
        if (__instance.Pointer != p1Popup.Pointer) return true;   // P2's own, or another popup (xp)
        if (!P2Gold.SwappedIn) return true;
        redirecting = true;
        try { p2Popup.Add(amount); }
        finally { redirecting = false; }
        return false;
    }

    // From P2Gold.Collected: gold that showed on P1's popup was P2's
    public static void MovedToP2(int amount)
    {
        if (amount <= 0 || p1Popup == null || p2Popup == null) return;
        try
        {
            int left = Mathf.Max(0, p1Popup.amount - amount);
            p1Popup.amount = left;
            if (p1Popup.text != null) p1Popup.text.text = left > 0 ? p1Popup.prefix + left : "";
            redirecting = true;
            try { p2Popup.Add(amount); }
            finally { redirecting = false; }
        }
        catch (Exception e) { if (errors++ < 3) ModLog.Warn($"GoldPopups: moving {amount} to P2's popup failed: {e.Message}"); }
    }
    private static int errors;

    // Every frame (Ticker, after the cameras): make P2's popup, place both
    public static void LateUpdate()
    {
        bool run = PlayerJoin.HasP2 && P2Movement.Active;
        if (p1Popup == null)
        {
            if (!run) return;
            Find();
            if (p1Popup == null) return;
        }
        if (p1Rt == null) return;
        if (!run)
        {
            p1Rt.anchoredPosition = home;
            if (p2Rt != null && p2Rt.gameObject.activeSelf) p2Rt.gameObject.SetActive(false);
            return;
        }
        if (p2Rt != null && !p2Rt.gameObject.activeSelf) p2Rt.gameObject.SetActive(true);
        var parent = p1Rt.parent != null ? p1Rt.parent.TryCast<RectTransform>() : null;
        float width = parent != null && parent.rect.width > 1f ? parent.rect.width : 1920f;
        float s = Mathf.Clamp01(1f - 2f * P2Camera.SideWidth);   // 0 split .. 1 merged
        float p1Mid = Mathf.Lerp(0.25f, 0.5f, s), p2Mid = Mathf.Lerp(0.75f, 0.5f, s);
        p1Rt.anchoredPosition = new Vector2(home.x + (p1Mid - 0.5f) * width, home.y);
        if (p2Rt != null) p2Rt.anchoredPosition = new Vector2(home.x + (p2Mid - 0.5f) * width, home.y - MergedGapY * s);
    }

    private static void Find()
    {
        foreach (var t in Object.FindObjectsOfType<XpAndGoldText>(true))
            if (t != null && t.name == "T_Gold" && !t.name.Contains("MegabonkSplitscreen")) { p1Popup = t; break; }
        if (p1Popup == null) return;
        p1Rt = p1Popup.transform.TryCast<RectTransform>();
        home = p1Rt != null ? p1Rt.anchoredPosition : Vector2.zero;
        try
        {
            var copy = Object.Instantiate(p1Popup.gameObject, p1Popup.transform.parent);
            copy.name = "MegabonkSplitscreen_P2Gold_Popup";
            p2Popup = copy.GetComponent<XpAndGoldText>();
            p2Rt = copy.transform.TryCast<RectTransform>();
            p2Popup.amount = 0;
            if (p2Popup.text != null) p2Popup.text.text = "";
            ModLog.Info($"GoldPopups: P2's popup made (copy of '{p1Popup.name}' under '{p1Popup.transform.parent?.name}', prefix '{p1Popup.prefix}', home {home})");
        }
        catch (Exception e) { ModLog.Error($"GoldPopups: P2's popup failed: {e}"); p2Popup = null; p2Rt = null; }
    }

    // Autotest: both popups now
    public static string Describe() =>
        $"P1 popup {(p1Popup != null ? $"'{p1Popup.text?.text}' (amount {p1Popup.amount}) at {p1Rt?.anchoredPosition}" : "none")}; " +
        $"P2 popup {(p2Popup != null ? $"'{p2Popup.text?.text}' (amount {p2Popup.amount}) at {p2Rt?.anchoredPosition}" : "none")}; split {P2Camera.SideWidth:F2}";

    // New stage / run: the HUD is new
    public static void Reset() { p1Popup = p2Popup = null; p1Rt = p2Rt = null; }
}
