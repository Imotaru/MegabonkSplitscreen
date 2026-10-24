using HarmonyLib;
using UnityEngine;

namespace MegabonkSplitscreen;

// The menu highlight (SelectionArrow 'SelectionArrowNEW', the 4 corner arrows on a full-screen overlay) sizes itself
// from the selected button's rect, which ignores the scale of the grid it's in (the character grids are at 75%).
// v1.0.0-alpha (user: the arrows are a little too large for P1): the old fix scaled the size the game picked in
// OnButtonHover(RectTransform), but the game sets it again every frame ([arrowstate]: newSize back at 165 for a
// 135x135 button shown at 101 px) - and its OnButtonHover(MyButton) calls the other one directly, past the hook.
// Now after the arrow's own Update the whole arrow is scaled by the on-screen scale of the button's parent (the grid:
// not the button's own hover pop, which the game's arrows don't follow either).
internal static class SelectionArrowFix
{
    private static int logged;

    public static void Apply(Harmony harmony)
    {
        var target = AccessTools.DeclaredMethod(typeof(SelectionArrow), "Update");
        if (target == null || !P2Weapons.IsNativeCodeUnique(target) || P2Weapons.IsNativeCodeHooked(target))
        {
            ModLog.Warn("SelectionArrowFix: SelectionArrow.Update not hookable - the arrows keep the game's size");
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(SelectionArrowFix), nameof(AfterUpdate)));
    }

    // The scale that makes a highlight drawn in `arrowParent`'s space match `button` on screen
    public static float ScaleFor(RectTransform button, Transform arrowParent)
    {
        if (button == null) return 1f;
        var grid = button.parent;
        float buttonScale = grid != null ? grid.lossyScale.x : button.lossyScale.x;
        float own = arrowParent != null ? arrowParent.lossyScale.x : 1f;
        return own > 0.0001f ? buttonScale / own : 1f;
    }

    private static void AfterUpdate(SelectionArrow __instance)
    {
        var rt = __instance?.rectTransform;
        var sel = __instance?.selectedRect;
        if (rt == null || sel == null) return;
        float k = ScaleFor(sel, rt.parent);
        if (Mathf.Abs(rt.localScale.x - k) > 0.001f)
        {
            rt.localScale = new Vector3(k, k, 1f);
            if (logged++ < 6) ModLog.Info($"Highlight on '{sel.name}': scaled x{k:F2} (button rect {sel.rect.width:F0}, arrow size {rt.sizeDelta.x:F0})");
        }
    }
}
