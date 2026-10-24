using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Player 2's character select panel: a visual copy of P1's grid window (with its info panel child),
// mirrored onto the right half. The copy is made inert so it can't touch the game's selection state:
//  - instantiated under an inactive holder, so none of its scripts run Awake/OnEnable
//  - every game script (Assembly-CSharp) and every Selectable (Button, Scrollbar) is removed
// The mod drives what it shows (next step). Shown/hidden together with P1's grid window.
internal static class P2Panel
{
    private const float EdgeMargin = 62f;   // same as P1's left margin, on the right

    private static GameObject panel;
    private static GameObject p1Window;

    public static void Ensure(CharacterMenu menu)
    {
        if (menu == null) return;
        p1Window = menu.gameObject;
        if (panel != null) return;

        var area = menu.transform.parent;
        var holder = new GameObject("MegabonkSplitscreen_P2Holder");
        holder.SetActive(false);
        holder.transform.SetParent(area, false);

        var copy = Object.Instantiate(menu.gameObject, holder.transform);
        copy.name = "MegabonkSplitscreen_P2Panel";
        int removed = StripBehaviour(copy);

        copy.transform.SetParent(area, false);
        Object.Destroy(holder);

        SetTitle(copy, "PLAYER 2");
        panel = copy;
        try
        {
            Layout(menu);
        }
        catch (Exception e)
        {
            ModLog.Error($"P2 panel layout failed: {e}");
        }
        panel.SetActive(p1Window.activeInHierarchy);
        ModLog.Info($"P2 panel created ({removed} game scripts/selectables removed)");
        try
        {
            P2Select.Bind(menu, panel);
        }
        catch (Exception e)
        {
            ModLog.Error($"P2 select bind failed: {e}");
        }
    }

    // Mirror P1's layout: grid against the right edge, info panel on its left (mirrored local x).
    private static void Layout(CharacterMenu menu)
    {
        var p1Grid = menu.transform.TryCast<RectTransform>();
        var grid = panel.transform.TryCast<RectTransform>();
        var area = p1Grid.parent.TryCast<RectTransform>();

        float scale = p1Grid.localScale.x;
        float gridX = area.rect.width / 2f - EdgeMargin - p1Grid.rect.width * scale / 2f;
        grid.localScale = p1Grid.localScale;
        grid.anchoredPosition = new Vector2(gridX, p1Grid.anchoredPosition.y);

        // GetComponentInChildren<CharacterInfoUI> threw an interop VerificationException; FindObjectOfType works
        // (the copy's CharacterInfoUI was removed, so this is P1's).
        var p1Info = Object.FindObjectOfType<CharacterInfoUI>();
        var p1InfoRt = p1Info != null ? p1Info.transform.TryCast<RectTransform>() : null;
        var infoT = p1InfoRt != null ? panel.transform.Find(p1InfoRt.name) : null;
        var info = infoT != null ? infoT.TryCast<RectTransform>() : null;
        if (info != null)
        {
            info.localScale = p1InfoRt.localScale;
            info.anchoredPosition = new Vector2(-p1InfoRt.anchoredPosition.x, p1InfoRt.anchoredPosition.y);
        }
        else
        {
            ModLog.Warn("P2 panel: info panel copy not found, left in place");
        }
        ModLog.Info($"P2 panel at centre ({gridX:F0}, {p1Grid.anchoredPosition.y:F0}), scale {scale}");
    }

    // Removes game scripts first (some require a Button), then the remaining Selectables.
    private static int StripBehaviour(GameObject root)
    {
        int removed = 0;
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string asm = mb.GetIl2CppType().Assembly.GetName().Name;
            if (asm != "Assembly-CSharp") continue;
            Object.DestroyImmediate(mb);
            removed++;
        }
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null) continue;
            Object.DestroyImmediate(sel);
            removed++;
        }
        return removed;
    }

    // Header title: drop its localisation component so the text isn't switched back, then set it.
    private static void SetTitle(GameObject root, string text)
    {
        foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp == null || tmp.name != "T_Title") continue;
            foreach (var mb in tmp.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetIl2CppType().Name == "LocalizeStringEvent") Object.DestroyImmediate(mb);
            tmp.text = text;
            return;
        }
        ModLog.Warn("P2 panel: title text not found");
    }

    // P2 left the lobby: the panel goes (a new join makes a new one)
    public static void Remove()
    {
        if (panel != null) Object.Destroy(panel);
        panel = null;
    }

    // Called every frame: keep the panel shown exactly while P1's grid window is.
    public static void Sync()
    {
        if (panel == null || p1Window == null) return;
        bool show = p1Window.activeInHierarchy;
        if (panel.activeSelf != show) panel.SetActive(show);
    }
}
