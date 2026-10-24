using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// TEMP diagnostic: writes the character select UI tree to ui_dump.txt next to the log, once per session,
// so the P2 panel can be laid out from real names, positions and sizes.
internal static class UiDump
{
    private static bool done;
    private static string dir;
    private const int MaxLines = 2500;

    public static void Init(string pluginDir) => dir = pluginDir;

    public static void DumpCharacterSelect(CharacterMenu menu)
    {
        if (!ModLog.Debug) return;   // dump files only with debug logging (1.0 alpha)
        if (done || menu == null || dir == null) return;
        done = true;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Screen {Screen.width}x{Screen.height}");

            sb.AppendLine("== Parents of CharacterMenu (bottom to top) ==");
            for (var t = menu.transform.parent; t != null; t = t.parent)
                sb.AppendLine("  " + Describe(t));

            sb.AppendLine("== CharacterMenu subtree ==");
            int lines = 0;
            Walk(menu.transform, 0, sb, ref lines);

            var info = Object.FindObjectOfType<CharacterInfoUI>();
            if (info != null && !info.transform.IsChildOf(menu.transform))
            {
                sb.AppendLine("== CharacterInfoUI (outside CharacterMenu) ==");
                for (var t = info.transform.parent; t != null; t = t.parent)
                    sb.AppendLine("  parent: " + Describe(t));
                Walk(info.transform, 0, sb, ref lines);
            }

            var arrow = SelectionArrow.Instance;
            if (arrow != null) sb.AppendLine("== SelectionArrow ==\n  " + Describe(arrow.transform));

            File.WriteAllText(Path.Combine(dir, "ui_dump.txt"), sb.ToString());
            ModLog.Info($"UiDump: wrote ui_dump.txt ({lines} elements)");
        }
        catch (Exception e)
        {
            ModLog.Error($"UiDump failed: {e}");
        }
    }

    // TEMP: HUD layout (health bar, weapon list, objective) for placing P2's HUD; once per session
    private static bool hudDone;
    public static void DumpHud(params Transform[] roots)
    {
        if (!ModLog.Debug) return;   // dump files only with debug logging (1.0 alpha)
        if (hudDone) return;
        hudDone = true;
        Dump("hud_dump.txt", roots);
    }

    // TEMP: any UI subtrees to a file, once per file name per session
    private static readonly HashSet<string> dumped = new();
    public static void DumpOnce(string file, params Transform[] roots)
    {
        if (!ModLog.Debug) return;   // dump files only with debug logging (1.0 alpha)
        if (!dumped.Add(file)) return;
        Dump(file, roots);
    }

    private static void Dump(string file, Transform[] roots)
    {
        if (dir == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Screen {Screen.width}x{Screen.height}");
            int lines = 0;
            foreach (var r in roots)
            {
                if (r == null) continue;
                sb.AppendLine($"== {r.name} parents (bottom to top) ==");
                for (var t = r.parent; t != null; t = t.parent) sb.AppendLine("  " + Describe(t));
                sb.AppendLine($"== {r.name} subtree ==");
                Walk(r, 0, sb, ref lines);
            }
            File.WriteAllText(Path.Combine(dir, file), sb.ToString());
            ModLog.Info($"UiDump: wrote {file} ({lines} elements)");
        }
        catch (Exception e)
        {
            ModLog.Error($"UI dump {file} failed: {e}");
        }
    }

    private static void Walk(Transform t, int depth, StringBuilder sb, ref int lines)
    {
        if (lines++ >= MaxLines) return;
        sb.AppendLine(new string(' ', depth * 2) + Describe(t));

        // Character grids repeat the same button many times; show 2, then a count.
        int shownButtons = 0, hiddenButtons = 0;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i);
            if (child.GetComponent<MyButtonCharacter>() != null && ++shownButtons > 2)
            {
                hiddenButtons++;
                continue;
            }
            Walk(child, depth + 1, sb, ref lines);
        }
        if (hiddenButtons > 0)
            sb.AppendLine(new string(' ', (depth + 1) * 2) + $"... {hiddenButtons} more character buttons");
    }

    private static string Describe(Transform t)
    {
        var sb = new StringBuilder();
        sb.Append(t.gameObject.activeSelf ? "" : "[off] ").Append('\'').Append(t.name).Append('\'');

        var rt = t.TryCast<RectTransform>();
        if (rt != null)
            sb.Append($" anchors({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})-({rt.anchorMax.x:F2},{rt.anchorMax.y:F2})" +
                      $" pivot({rt.pivot.x:F2},{rt.pivot.y:F2}) pos({rt.anchoredPosition.x:F0},{rt.anchoredPosition.y:F0})" +
                      $" size({rt.rect.width:F0}x{rt.rect.height:F0})");

        var names = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            string n = c.GetIl2CppType().Name;
            if (n != "Transform" && n != "RectTransform" && n != "CanvasRenderer") names.Add(n);
        }
        if (names.Count > 0) sb.Append(" {").Append(string.Join(", ", names)).Append('}');

        var canvas = t.GetComponent<Canvas>();
        if (canvas != null) sb.Append($" canvas(mode={canvas.renderMode}, sort={canvas.sortingOrder}, scale={canvas.scaleFactor:F2})");
        return sb.ToString();
    }
}
