using Inventory__Items__Pickups;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Split XP bar (user's design): the game's full-width XP bar at the top becomes two halves.
//  - P1: from the left edge to the middle. P1's own bar is moved into a container that covers its parent exactly
//    and is squashed to half width around the bar's left edge. However the game fills the bar (scale, anchors,
//    size), it now fills from the left edge to the middle.
//  - P2: a red copy of the bar in a second such container, mirrored (scale -0.5) so it starts at the right edge and
//    ends in the middle. The mod fills it from P2's XP (XpUtility.CurrentLevelProgress).
// P2 doesn't collect XP yet (gems still fly to P1), so P2's half stays empty until that is added.
internal static class P2XpBar
{
    private static readonly Color P2Color = new(0.9f, 0.15f, 0.15f, 1f);

    public static int Xp { get; private set; }

    private static XpBarUI p1Ui;
    private static RectTransform p1Half, p2Half, p2Fill;
    private static RawImage p2Img;
    private static Material normalMaterial;

    public static void ResetForNewRun() => Xp = 0;

    private static int addLogs;
    public static void AddXp(int amount)
    {
        if (Autotest.NoXp) return;   // autotest: no level-up screens mid-test
        int before = XpUtility.XpToLevel(Xp);
        Xp += amount;
        int after = XpUtility.XpToLevel(Xp);
        if (addLogs++ < 5) ModLog.Info($"P2 +{amount} XP -> {Xp}");
        if (after > before)
        {
            ModLog.Info($"P2 reached level {after}");
            P2LevelUp.QueueLevelUps(after - before);
            for (int lv = before + 1; lv <= after; lv++) P2Abilities.OnLevelUp(lv);   // P2's passive grows with P2's level
        }
    }

    public static void Start()
    {
        if (p1Half != null && p2Half != null) return;   // same scene, already split
        try
        {
            Build();
        }
        catch (Exception e)
        {
            ModLog.Error($"XP bar split failed: {e}");
        }
    }

    private static void Build()
    {
        p1Ui = Object.FindObjectOfType<XpBarUI>();
        var bar = p1Ui != null ? p1Ui.xpBar : null;
        var barRt = bar != null ? bar.rectTransform : null;
        var parent = barRt != null ? barRt.parent.TryCast<RectTransform>() : null;
        if (barRt == null || parent == null)
        {
            ModLog.Warn("XP bar split: XpBarUI / its bar not found");
            return;
        }
        UiDump.DumpOnce("xpbar_dump.txt", p1Ui.transform);
        ModLog.Info($"XP bar before split: '{barRt.name}' anchors {barRt.anchorMin}-{barRt.anchorMax} pivot {barRt.pivot} " +
                    $"pos {barRt.anchoredPosition} size {barRt.sizeDelta} scale {barRt.localScale} in '{parent.name}' ({parent.rect.width:F0} wide)");

        // The bar's left/right edges in the parent's normalized x (0..1). Unscaled: the game fills the bar by its
        // localScale.x (0 at the start of a run), so measuring through the transform gave 0..0 in v0.13.0/v0.14.0
        // and the red half was mirrored around the left corner.
        Rect pr = parent.rect;
        float x0 = barRt.localPosition.x + barRt.rect.xMin;
        float x1 = barRt.localPosition.x + barRt.rect.xMax;
        float left = (x0 - pr.xMin) / pr.width;
        float right = (x1 - pr.xMin) / pr.width;
        if (right - left < 0.01f)
        {
            ModLog.Warn($"XP bar edges {left:F2}..{right:F2} look wrong, using the full width");
            left = 0f;
            right = 1f;
        }
        int index = barRt.GetSiblingIndex();

        // P2 copy first (made from the untouched bar), then move P1's bar into its half
        var copy = Object.Instantiate(barRt.gameObject, parent);
        copy.name = "MegabonkSplitscreen_P2XpFill";
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(mb);
        p2Fill = copy.GetComponent<RectTransform>();
        p2Img = copy.GetComponent<RawImage>();

        // Mirror around pivot p so the bar's left edge lands on its right edge and its right edge on the middle:
        // x' = p - 0.5 (x - p)  =>  p = (right + 0.5 left) / 1.5
        p2Half = MakeHalf(parent, "MegabonkSplitscreen_P2XpHalf", (right + 0.5f * left) / 1.5f, -0.5f, index);
        p2Fill.SetParent(p2Half, false);
        // Fill grows from the copy's left edge (screen right): pivot x 0, keep the rect where it is
        SetPivotX(p2Fill, 0f);
        if (p2Img != null)
        {
            p2Img.color = P2Color;
            if (p1Ui.defaultMaterial != null) p2Img.material = p1Ui.defaultMaterial;
            // The material to go back to after the rainbow (v0.19.0: XpBarUI.defaultMaterial can be empty, so the
            // rainbow was never replaced). Graphic.material returns Unity's default UI material when none is set.
            normalMaterial = p2Img.material;
        }

        p1Half = MakeHalf(parent, "MegabonkSplitscreen_P1XpHalf", left, 0.5f, index);
        barRt.SetParent(p1Half, false);

        ModLog.Info($"XP bar split: bar spans x {left:F2}..{right:F2} of '{parent.name}'; P1 half and red P2 half made");
    }

    // A container covering `parent` exactly (so children keep their layout), scaled horizontally around pivotX.
    private static RectTransform MakeHalf(RectTransform parent, string name, float pivotX, float scaleX, int index)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(pivotX, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = new Vector3(scaleX, 1f, 1f);
        rt.SetSiblingIndex(index);
        return rt;
    }

    private static void SetPivotX(RectTransform rt, float px)
    {
        float dx = (px - rt.pivot.x) * rt.rect.width;
        rt.pivot = new Vector2(px, rt.pivot.y);
        rt.anchoredPosition += new Vector2(dx, 0f);
    }

    // Called every frame from P2Body.Update.
    public static void Update()
    {
        if (p2Fill == null) return;
        // Level-up waiting or open: full bar with the game's rainbow material, like P1's
        bool rainbow = P2LevelUp.Pending > 0 || P2LevelUp.P2ScreenOpen;
        float ratio = rainbow ? 1f : Mathf.Clamp01(XpUtility.CurrentLevelProgress(Xp));
        p2Fill.localScale = new Vector3(ratio, 1f, 1f);
        if (p2Img != null)
        {
            var mat = rainbow && p1Ui != null && p1Ui.rainbow != null ? p1Ui.rainbow : normalMaterial;
            if (p2Img.material == null || mat == null || p2Img.material.Pointer != mat.Pointer) p2Img.material = mat;
            var col = rainbow ? Color.white : P2Color;
            if (p2Img.color != col) p2Img.color = col;
        }
    }
}
