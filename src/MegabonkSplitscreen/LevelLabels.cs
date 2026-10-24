using Inventory__Items__Pickups;
using Inventory__Items__Pickups.Xp_and_Levels;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Each player's level under their own minimap (user: "add a level counter for p2, move p1 level counter to be
// underneath p1 minimap, and do same for p2 counter").
// The game's level text is 'Top/T_Level' (XpBarUI.t_levelText, top right, 350x50 - HUD dump); the minimaps are
// 'Minimap' (P1, the game's) and 'MegabonkSplitscreen_P2Minimap' (P2Minimap's copy).
//  - While P2 plays, P1's T_Level is moved under P1's minimap (centred); put back where it was when P2 isn't playing.
//  - P2's label is an inert copy of it under P2's minimap, its number replaced by P2's level (P2XpBar's XP). When P2's
//    minimap is hidden (merged camera) P2's label sits under P1's.
internal static class LevelLabels
{
    private const float GapBelow = 6f;

    private static TextMeshProUGUI p1Text, p2Text;
    private static Transform p1OrigParent;
    private static int p1OrigIndex;
    private static Vector2 origAnchorMin, origAnchorMax, origPivot, origPos;
    private static TextAlignmentOptions origAlign;
    private static bool moved;
    private static Transform p2Host;
    private static int logs;

    public static void Update()
    {
        bool split = P2Movement.Active && P2Minimap.P1Frame != null;
        if (!split) { Restore(); return; }
        if (p1Text == null)
        {
            var bar = Object.FindObjectOfType<XpBarUI>();
            p1Text = bar != null ? bar.t_levelText : null;
            if (p1Text == null) return;
        }
        if (!moved) MoveP1();
        UpdateP2();
        PlaceForView();
    }

    // v0.42.0 (user: "when the camera is merged put the lvl labels next to the healthbars"): merged (P2's minimap
    // hidden) - P1's label right of P1's bars, P2's left of P2's (mirrored HUD); split - under each minimap
    private static Transform p1At, p2At;
    private const float BesideScale = 0.7f, BesideGap = 14f;
    private static void PlaceForView()
    {
        var p2Frame = P2Minimap.P2Frame;
        bool merged = p2Frame == null || !p2Frame.activeInHierarchy;
        Transform want1 = merged && P2Hud.P1Bars != null ? P2Hud.P1Bars : P2Minimap.P1Frame.transform;
        Transform want2 = merged ? (P2Hud.P2Bars != null ? P2Hud.P2Bars : p1Text.transform) : p2Frame.transform;
        if (want1 != p1At)
        {
            p1At = want1;
            if (want1 == P2Hud.P1Bars) PlaceBeside(p1Text, want1, rightOf: true);
            else { PlaceUnder(p1Text.rectTransform, want1); p1Text.rectTransform.localScale = Vector3.one; p1Text.alignment = TextAlignmentOptions.Top; }
            if (logs++ < 10) ModLog.Info($"[level] P1's level {(merged ? "next to P1's health bar" : "under P1's minimap")}");
        }
        if (p2Text != null && want2 != p2At)
        {
            p2At = want2;
            p2Host = want2;
            if (want2 == P2Hud.P2Bars) PlaceBeside(p2Text, want2, rightOf: false);
            else { PlaceUnder(p2Text.rectTransform, want2); p2Text.rectTransform.localScale = Vector3.one; p2Text.alignment = TextAlignmentOptions.Top; }
            if (logs++ < 10) ModLog.Info($"[level] P2's level {(merged ? "next to P2's health bar" : "under P2's minimap")}");
        }
    }

    // Vertically centred beside `bars`, on its right (P1) or left (P2, whose HUD is mirrored)
    private static void PlaceBeside(TextMeshProUGUI t, Transform bars, bool rightOf)
    {
        var rt = t.rectTransform;
        rt.SetParent(bars, false);
        rt.anchorMin = rt.anchorMax = new Vector2(rightOf ? 1f : 0f, 0.5f);
        rt.pivot = new Vector2(rightOf ? 0f : 1f, 0.5f);
        rt.anchoredPosition = new Vector2(rightOf ? BesideGap : -BesideGap, 0f);
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one * BesideScale;
        t.alignment = rightOf ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
    }

    private static void MoveP1()
    {
        var rt = p1Text.rectTransform;
        p1OrigParent = rt.parent;
        p1OrigIndex = rt.GetSiblingIndex();
        origAnchorMin = rt.anchorMin;
        origAnchorMax = rt.anchorMax;
        origPivot = rt.pivot;
        origPos = rt.anchoredPosition;
        origAlign = p1Text.alignment;
        PlaceUnder(rt, P2Minimap.P1Frame.transform);
        p1Text.alignment = TextAlignmentOptions.Top;
        moved = true;
        if (logs++ < 4) ModLog.Info($"[level] P1's level text '{p1Text.text}' moved under P1's minimap (was under '{p1OrigParent?.name}' at {origPos})");
    }

    // Top-centre of the label just under the bottom-centre of `frame`
    private static void PlaceUnder(RectTransform rt, Transform frame)
    {
        rt.SetParent(frame, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -GapBelow);
        rt.localRotation = Quaternion.identity;
    }

    private static void UpdateP2()
    {
        if (p2Text == null)
        {
            var hold = new GameObject("hold");
            hold.SetActive(false);
            var copy = Object.Instantiate(p1Text.gameObject, hold.transform);
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(mb);
            copy.name = "MegabonkSplitscreen_P2Level";
            p2Text = copy.GetComponent<TextMeshProUGUI>();
            copy.transform.SetParent(P2Minimap.P1Frame.transform, false);
            Object.Destroy(hold);
            copy.SetActive(true);
            p2Host = null;
        }
        // (placed by PlaceForView) P1's text with its number replaced by P2's level (keeps the game's wording and style)
        string src = p1Text.text ?? "";
        string level = XpUtility.XpToLevel(P2XpBar.Xp).ToString();
        var m = System.Text.RegularExpressions.Regex.Match(src, @"\d+");
        string want = m.Success ? src.Substring(0, m.Index) + level + src.Substring(m.Index + m.Length) : "LV " + level;
        if (p2Text.text != want) p2Text.text = want;
    }

    private static void Restore()
    {
        if (moved && p1Text != null)
        {
            var rt = p1Text.rectTransform;
            rt.SetParent(p1OrigParent, false);
            rt.SetSiblingIndex(p1OrigIndex);
            rt.anchorMin = origAnchorMin;
            rt.anchorMax = origAnchorMax;
            rt.pivot = origPivot;
            rt.anchoredPosition = origPos;
            rt.localScale = Vector3.one;
            p1Text.alignment = origAlign;
        }
        moved = false;
        p1At = p2At = null;
        if (p2Text != null) Object.Destroy(p2Text.gameObject);
        p2Text = null;
        p2Host = null;
    }

    public static void Reset()
    {
        Restore();
        p1Text = null;
    }
}
