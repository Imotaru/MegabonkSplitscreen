using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// "SplitBonk V1.0 Alpha" at the top left of the main menu (user, for the V1.0 Alpha release), in small text.
// A copy of the game's own version text (VersionNumber.t_version, "version 1.0.69" at the bottom right) - same font
// and style - anchored to the top-left corner, shown while the main menu's main page (MainMenu.tabMenu) is open.
internal static class MenuLabel
{
    public const string Text = Plugin.ReleaseName;
    private const float Margin = 20f;

    private static GameObject label;
    private static MainMenu menu;
    private static float nextLook;

    // Called every frame by the Ticker; looks for the menu twice a second while there's none
    public static void Update()
    {
        if (label == null || menu == null)
        {
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 0.5f;
            Make();
            // (menu gone with the label still there - the scene change into a run - was a NullReferenceException)
            if (label == null || menu == null) return;
        }
        bool show = menu.tabMenu != null ? menu.tabMenu.activeInHierarchy : menu.gameObject.activeInHierarchy;
        if (label.activeSelf != show) label.SetActive(show);
    }

    private static void Make()
    {
        menu = Object.FindObjectOfType<MainMenu>();
        if (menu == null) return;
        var vn = Object.FindObjectOfType<VersionNumber>(true);
        var src = vn != null ? vn.t_version : null;
        if (src == null) { if (!warned) { warned = true; ModLog.Warn("MenuLabel: the game's version text wasn't found - no SplitBonk label"); } return; }
        try
        {
            label = Object.Instantiate(src.gameObject, src.transform.parent);
            label.name = "MegabonkSplitscreen_MenuLabel";
            // only the text: no game scripts (VersionNumber would write the version back), no localisation
            foreach (var mb in label.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp" || mb != null && mb.GetIl2CppType().Name == "LocalizeStringEvent")
                    Object.DestroyImmediate(mb);
            for (int i = label.transform.childCount - 1; i >= 0; i--) Object.Destroy(label.transform.GetChild(i).gameObject);
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.text = Text;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = false;
            var rt = label.transform.TryCast<RectTransform>();
            // top-left of the version text's own canvas area
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(Margin, -Margin);
            rt.sizeDelta = new Vector2(Mathf.Max(400f, rt.sizeDelta.x), rt.sizeDelta.y);
            ModLog.Info($"MenuLabel: '{Text}' made from the game's version text ('{src.name}' under '{src.transform.parent?.name}', font size {tmp.fontSize:F0})");
        }
        catch (Exception e) { ModLog.Error($"MenuLabel failed: {e}"); label = null; }
    }
    private static bool warned;
}
