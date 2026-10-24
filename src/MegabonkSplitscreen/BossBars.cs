using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Boss health bars at the bottom of the screen, stacking upwards (user: at the top they were covered by the
// splitscreen minimaps). The game shows them as EnemyHpBar objects in a HUD container at the top; that container is
// re-anchored to the bottom centre and, if it lays the bars out vertically, the order is flipped so the first bar sits
// at the bottom. Only HUD bars (screen-space canvas) count - world-space bars over enemies are left alone.
internal static class BossBars
{
    private const float BottomMargin = 60f;   // canvas units above the bottom edge
    private static readonly HashSet<IntPtr> moved = new();
    private static float nextCheck;

    // Called every frame in a run (from P2Body.Update), checks twice a second
    public static void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;
        DumpWhenBoss();
        MoveTargetOfInterest();
        foreach (var bar in Object.FindObjectsOfType<EnemyHpBar>())
        {
            if (bar == null) continue;
            var rt = bar.GetComponent<RectTransform>();
            var canvas = bar.GetComponentInParent<Canvas>();
            if (rt == null || canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
            var container = rt.parent != null ? rt.parent.GetComponent<RectTransform>() : null;
            if (container == null || canvas.transform == container) container = rt;   // the bar itself sits on the canvas
            if (!moved.Add(container.Pointer)) continue;
            Move(container, bar);
        }
    }

    private static void Move(RectTransform container, EnemyHpBar bar)
    {
        UiDump.DumpOnce("bossbar_dump.txt", container);
        var before = $"anchors {container.anchorMin}-{container.anchorMax}, pivot {container.pivot}, pos {container.anchoredPosition}";
        float x = (container.anchorMin.x + container.anchorMax.x) * 0.5f;
        container.anchorMin = new Vector2(container.anchorMin.x, 0f);
        container.anchorMax = new Vector2(container.anchorMax.x, 0f);
        container.pivot = new Vector2(container.pivot.x, 0f);
        container.anchoredPosition = new Vector2(container.anchoredPosition.x, BottomMargin);
        string layout = "no layout group";
        var v = container.GetComponent<VerticalLayoutGroup>();
        if (v != null)
        {
            v.childAlignment = v.childAlignment switch
            {
                TextAnchor.UpperLeft => TextAnchor.LowerLeft,
                TextAnchor.UpperRight => TextAnchor.LowerRight,
                TextAnchor.UpperCenter or TextAnchor.MiddleCenter => TextAnchor.LowerCenter,
                _ => v.childAlignment,
            };
            v.reverseArrangement = true;
            layout = "vertical layout flipped (first bar at the bottom)";
        }
        ModLog.Info($"Boss bars moved to the bottom: '{container.name}' (was {before}; centre x {x:F2}), {layout}");
    }

    // TEMP (v0.31.0: the curse's two bosses showed their bars at the top and nothing here found them - the bars aren't
    // EnemyHpBars on a screen canvas): once a boss/elite is alive, dump the game HUD and log every EnemyHpBar, so the
    // real objects are known before changing anything (user: log names instead of guessing)
    private static bool bossDumped;
    private static float bossSeenAt = -1f;
    private static void DumpWhenBoss()
    {
        if (bossDumped) return;
        var em = Assets.Scripts.Managers.EnemyManager.Instance;
        if (em?.enemies == null) return;
        string boss = null;
        foreach (var kv in em.enemies)
        {
            var e = kv.Value;
            if (e == null || e.hp <= 0f) continue;
            bool isBoss = false, isElite = false;
            try { isBoss = e.IsBoss(); isElite = e.IsElite(); }
            catch { }
            if (isBoss) { boss = $"{e.enemyData?.enemyName} (boss {isBoss}, elite {isElite})"; break; }
        }
        if (boss == null) { bossSeenAt = -1f; return; }
        // 2 s after it appears, so its bar is up
        if (bossSeenAt < 0f) bossSeenAt = Time.unscaledTime;
        if (Time.unscaledTime - bossSeenAt < 2f) return;
        bossDumped = true;
        var roots = new List<Transform>();
        foreach (var c in Object.FindObjectsOfType<Canvas>())
            if (c != null && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) roots.Add(c.transform);
        UiDump.DumpOnce("bosshud_dump.txt", roots.ToArray());
        var bars = new List<string>();
        foreach (var b in Object.FindObjectsOfType<EnemyHpBar>())
        {
            if (b == null) continue;
            var cv = b.GetComponentInParent<Canvas>();
            var path = new List<string>();
            for (var t = b.transform; t != null; t = t.parent) path.Add(t.name);
            path.Reverse();
            bars.Add($"{string.Join("/", path)} (canvas {cv?.renderMode}, enemy {b.enemy?.enemyData?.enemyName})");
        }
        ModLog.Info($"[bossbar] {boss} alive: {roots.Count} screen canvases dumped to bosshud_dump.txt; EnemyHpBars: {(bars.Count > 0 ? string.Join(" | ", bars) : "none")}");
    }

    // The real boss bars (bosshud_dump.txt, v0.31.1): 'TargetOfInterestUI' {VerticalLayoutGroup, TargetOfInterestUi},
    // anchored top-centre at y -140, rows 'Group1', 'Group1 (1)'... (HorizontalLayoutGroup) of 'TOI_Prefab' bars.
    // Moved to the bottom centre; its layout aligned to the bottom and reversed, so the first row is lowest and more
    // rows stack upwards.
    private static IntPtr movedToi;
    private static void MoveTargetOfInterest()
    {
        var toi = Object.FindObjectOfType<TargetOfInterestUi>();
        if (toi == null || toi.Pointer == movedToi) return;
        movedToi = toi.Pointer;
        var rt = toi.GetComponent<RectTransform>();
        if (rt == null) return;
        string before = $"anchors {rt.anchorMin}-{rt.anchorMax}, pivot {rt.pivot}, pos {rt.anchoredPosition}";
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, BottomMargin);
        var v = toi.GetComponent<VerticalLayoutGroup>();
        if (v != null)
        {
            v.childAlignment = TextAnchor.LowerCenter;
            v.reverseArrangement = true;
        }
        ModLog.Info($"Boss bars ('{toi.name}') moved to the bottom (was {before}), layout {(v != null ? "bottom-up" : "none found")}");
    }

    public static void Reset()
    {
        movedToi = IntPtr.Zero;
        moved.Clear();
        bossDumped = false;
    }
}
