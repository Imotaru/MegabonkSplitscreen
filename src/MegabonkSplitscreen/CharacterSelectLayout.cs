using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Character select UI layout once player 2 has joined (user's design):
//  - P1's character grid window (W_Character, has CharacterMenu) at 75%, kept against the left edge,
//    top edge level with the info panel's
//  - P1's info panel (W_Stats, has CharacterInfoUI) at 50%, right of the grid, above P1's avatar
// Sizes and positions are in canvas units (1920x1080 reference) and computed from the real rects.
internal static class CharacterSelectLayout
{
    private const float GridScale = 0.75f;
    private const float InfoScale = 0.5f;
    private const float EdgeMargin = 62f;   // the grid's original distance from the left edge
    private const float Gap = 20f;          // between grid and info panel
    private const float PanelTop = 110f;    // top edge of both the grid and the info panel, from the top of the screen

    public static void ApplyP1()
    {
        var menu = Object.FindObjectOfType<CharacterMenu>();
        var grid = menu != null ? menu.transform.TryCast<RectTransform>() : null;
        var area = grid != null && grid.parent != null ? grid.parent.TryCast<RectTransform>() : null;
        if (grid == null || area == null)
        {
            ModLog.Warn("CharacterSelectLayout: character grid window not found");
            return;
        }

        // The grid's parent ('Character') has a HorizontalLayoutGroup that could snap it back.
        var le = grid.GetComponent<LayoutElement>();
        if (le == null) le = grid.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        float areaW = area.rect.width, areaH = area.rect.height;
        float gridW = grid.rect.width, gridH = grid.rect.height;

        // Grid: anchored at the area's centre, so x/y are measured from the centre (y up).
        float gridX = -areaW / 2f + EdgeMargin + gridW * GridScale / 2f;
        float gridY = areaH / 2f - PanelTop - gridH * GridScale / 2f;
        grid.localScale = new Vector3(GridScale, GridScale, 1f);
        grid.anchoredPosition = new Vector2(gridX, gridY);
        ModLog.Info($"P1 grid: scale {GridScale}, centre ({gridX:F0}, {gridY:F0}) (area {areaW:F0}x{areaH:F0})");

        var info = Object.FindObjectOfType<CharacterInfoUI>();
        var infoRt = info != null ? info.transform.TryCast<RectTransform>() : null;
        if (infoRt == null)
        {
            ModLog.Warn("CharacterSelectLayout: info panel not found");
            return;
        }
        if (infoRt.parent != grid.transform)
        {
            ModLog.Warn($"CharacterSelectLayout: info panel parent is '{infoRt.parent?.name}', expected the grid window; not moved");
            return;
        }

        // Target centre in area coordinates, then converted into the (scaled) grid window's local space.
        float infoW = infoRt.rect.width, infoH = infoRt.rect.height;
        float targetX = -areaW / 2f + EdgeMargin + gridW * GridScale + Gap + infoW * InfoScale / 2f;
        float targetY = areaH / 2f - PanelTop - infoH * InfoScale / 2f;
        float localScale = InfoScale / GridScale;
        infoRt.localScale = new Vector3(localScale, localScale, 1f);
        infoRt.anchoredPosition = new Vector2((targetX - gridX) / GridScale, (targetY - gridY) / GridScale);
        ModLog.Info($"P1 info panel: overall scale {InfoScale}, centre ({targetX:F0}, {targetY:F0}) from screen centre");
    }
}
