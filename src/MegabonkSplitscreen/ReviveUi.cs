using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Death pieces 4 + 7 (user's plan): an arrow on the alive player's screen guiding them to the downed player's
// gravestone, and a "REVIVED" text in the middle of the revived player's view - spinning in, with an impact frame.
// One screen-space overlay canvas (1 unit = 1 pixel), placed in each player's own camera rect (split view) or the whole
// screen (merged); after the cameras moved (Ticker.LateUpdate).
internal static class ReviveUi
{
    private static Canvas canvas;
    private static readonly RawImage[] arrows = new RawImage[2];
    private static readonly TMPro.TextMeshProUGUI[] labels = new TMPro.TextMeshProUGUI[2];
    private static Texture2D arrowTex;
    private static readonly Color ArrowGreen = new(0.35f, 1f, 0.4f, 1f);
    private const float ArrowSize = 51f, EdgeMargin = 70f, AboveStone = 2.8f;   // 51: user, 80 % of 64

    // REVIVED: one per revived player
    private class Banner { public int player; public float start; public TMPro.TextMeshProUGUI text; public Image flash; }
    private static readonly List<Banner> banners = new();
    private const float BannerTime = 1.8f;

    private static bool Ensure()
    {
        if (canvas != null) return true;
        var go = new GameObject("MegabonkSplitscreen_ReviveUi");
        Object.DontDestroyOnLoad(go);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        arrowTex = MakeArrowTexture();
        for (int i = 0; i < 2; i++)
        {
            var a = new GameObject($"Arrow to P{i + 1}");
            a.transform.SetParent(go.transform, false);
            var img = a.AddComponent<RawImage>();
            img.texture = arrowTex;
            img.color = ArrowGreen;
            img.raycastTarget = false;
            Place(img.rectTransform, Vector2.zero, new Vector2(ArrowSize, ArrowSize));
            arrows[i] = img;
            labels[i] = MakeText($"Label P{i + 1}", 28f);
            a.SetActive(false);
            labels[i].gameObject.SetActive(false);
        }
        ModLog.Info("[reviveui] overlay made (arrows, labels)");
        return true;
    }

    // ---------- Piece 4: the arrow ----------
    public static void LateTick()
    {
        try
        {
            UpdateBanners();
            for (int p = 0; p < 2; p++) UpdateArrow(p);
        }
        catch (Exception e) { if (errors++ < 3) ModLog.Warn($"[reviveui] {e.Message}"); }
    }
    private static int errors, arrowLogs;

    private static void UpdateArrow(int p)
    {
        var grave = Gravestones.Position(p);
        bool downP = p == 0 ? Downed.P1Down : P2Health.IsDead;
        bool downOther = p == 0 ? P2Health.IsDead : Downed.P1Down;
        // v0.49.3 (user: the gravestone marker drew over the pause menu): hidden while the game is paused
        bool want = grave != null && downP && !downOther && !Revive.Charging(p) && !Revive.Rising(p) && !Assets.Scripts.Utility.MyTime.paused;
        if (!want)
        {
            if (arrows[p] != null && arrows[p].gameObject.activeSelf) { arrows[p].gameObject.SetActive(false); labels[p].gameObject.SetActive(false); }
            return;
        }
        Ensure();
        var cam = ViewOf(1 - p, out Rect rect);
        if (cam == null) return;
        Vector3 target = grave.Value + Vector3.up * AboveStone;
        Vector3 sp = cam.WorldToScreenPoint(target);
        var inner = new Rect(rect.x + EdgeMargin, rect.y + EdgeMargin, rect.width - 2f * EdgeMargin, rect.height - 2f * EdgeMargin);
        bool onScreen = sp.z > 0f && inner.Contains(new Vector2(sp.x, sp.y));
        Vector2 pos;
        // v0.48.2 (user: on screen it pointed up, off screen the opposite way): the texture's tip points UP on screen
        // (0 = up), so: 180 = down at the stone, and atan2 - 90 points it along d
        float angle;
        if (onScreen)
        {
            pos = new Vector2(sp.x, sp.y + 10f * Mathf.Sin(Time.unscaledTime * 5f));
            angle = 180f;
        }
        else
        {
            Vector2 c = inner.center;
            Vector2 d = new Vector2(sp.x, sp.y) - c;
            if (sp.z < 0f) d = -d;   // behind the camera
            if (d.sqrMagnitude < 1f) d = Vector2.down;
            float k = Mathf.Min(inner.width / 2f / Mathf.Max(0.001f, Mathf.Abs(d.x)), inner.height / 2f / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            pos = c + d * k;
            angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
        }
        var rt = arrows[p].rectTransform;
        rt.anchoredPosition = pos;
        rt.localEulerAngles = new Vector3(0f, 0f, angle);
        float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f);
        rt.localScale = Vector3.one * pulse;
        if (!arrows[p].gameObject.activeSelf) arrows[p].gameObject.SetActive(true);

        // "P1 23 m" next to it (towards the screen's inside)
        var alive = p == 0 ? P2Weapons.Body?.transform.position : Assets.Scripts.Actors.Player.MyPlayer.Instance?.transform.position;
        float dist = alive != null ? Vector3.Distance(alive.Value, grave.Value) : 0f;
        var lt = labels[p];
        lt.text = $"P{p + 1}  {dist:F0} m";
        Vector2 inward = (rect.center - pos).normalized;
        lt.rectTransform.anchoredPosition = onScreen ? pos + new Vector2(0f, 46f) : pos + inward * 95f;   // (screenshot: 62 touched the arrow)
        if (!lt.gameObject.activeSelf) lt.gameObject.SetActive(true);
        if (arrowLogs++ < 3) ModLog.Info($"[reviveui] arrow to P{p + 1}'s gravestone in P{2 - p}'s view: {(onScreen ? "above it" : "at the edge")} at {pos}, {dist:F0} m");
    }

    // The camera and screen rect (pixels) of a player's view: the shared camera when merged
    private static Camera ViewOf(int player, out Rect rect)
    {
        var p1Cam = PlayerCamera.Instance?.camera;
        var p2Cam = P2Camera.Cam;
        bool merged = p2Cam == null || !p2Cam.enabled || P2Camera.MergeAmount >= 0.999f;
        var cam = merged || player == 0 ? p1Cam : p2Cam;
        rect = cam != null ? cam.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
        return cam;
    }

    // ---------- Piece 7: REVIVED ----------
    public static void ShowRevived(int player)
    {
        Ensure();
        var b = new Banner { player = player, start = Time.unscaledTime };
        var flashGo = new GameObject($"ImpactFrame P{player + 1}");
        flashGo.transform.SetParent(canvas.transform, false);
        b.flash = flashGo.AddComponent<Image>();
        b.flash.raycastTarget = false;
        b.text = MakeText($"Revived P{player + 1}", 120f);
        b.text.text = "REVIVED!";
        b.text.color = new Color(0.55f, 1f, 0.45f, 1f);
        try { b.text.outlineWidth = 0.25f; b.text.outlineColor = new Color32(10, 40, 10, 255); } catch { }
        banners.Add(b);
        ModLog.Info($"[reviveui] REVIVED for P{player + 1}");
        UpdateBanners();
    }

    private static void UpdateBanners()
    {
        for (int i = banners.Count - 1; i >= 0; i--)
        {
            var b = banners[i];
            float t = Time.unscaledTime - b.start;
            if (t > BannerTime || b.text == null)
            {
                if (b.text != null) Object.Destroy(b.text.gameObject);
                if (b.flash != null) Object.Destroy(b.flash.gameObject);
                banners.RemoveAt(i);
                continue;
            }
            // The revived player's half - or the whole screen while the views are merged or merging (revive test
            // screenshot: the players met at the gravestone, the views were merging, the text was cut at P2's thin strip)
            Rect rect;
            if (P2Camera.Cam == null || !P2Camera.Cam.enabled || P2Camera.MergeAmount > 0.001f) rect = new Rect(0f, 0f, Screen.width, Screen.height);
            else ViewOf(b.player, out rect);
            // Impact frame: a white frame, then a dark one, then gone (over the revived player's view)
            if (b.flash != null)
            {
                Place(b.flash.rectTransform, rect.center, rect.size);
                if (t < 0.05f) b.flash.color = new Color(1f, 1f, 1f, 0.9f);
                else if (t < 0.11f) b.flash.color = new Color(0f, 0f, 0f, 0.7f);
                else b.flash.color = new Color(1f, 1f, 1f, Mathf.Max(0f, 0.25f * (1f - (t - 0.11f) / 0.25f)));
            }
            // The text: spins in from big (ease-out with a little overshoot), holds, swells and fades
            float a, scale, spin;
            if (t < 0.4f)
            {
                float k = t / 0.4f;
                float e = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);   // ease-out-back
                scale = Mathf.LerpUnclamped(3f, 1f, e);
                spin = Mathf.LerpUnclamped(-540f, 0f, e);
                a = Mathf.Clamp01(k * 2f);
            }
            else if (t < 1.3f) { scale = 1f + 0.03f * Mathf.Sin((t - 0.4f) * 12f); spin = 0f; a = 1f; }
            else { float k = (t - 1.3f) / (BannerTime - 1.3f); scale = 1f + 0.35f * k; spin = 0f; a = 1f - k; }
            var rt = b.text.rectTransform;
            rt.anchoredPosition = rect.center;
            rt.localScale = Vector3.one * scale * Mathf.Min(1f, rect.width / 960f + 0.25f);
            rt.localEulerAngles = new Vector3(0f, 0f, spin);
            var c = b.text.color;
            b.text.color = new Color(c.r, c.g, c.b, a);
        }
    }

    // ---------- Helpers ----------
    private static TMPro.TextMeshProUGUI MakeText(string name, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        var font = Object.FindObjectOfType<TMPro.TextMeshProUGUI>()?.font;   // the game's font
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        Place(t.rectTransform, Vector2.zero, new Vector2(900f, size * 1.4f));
        return t;
    }

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    // A chunky down-pointing arrow (white, tinted by the image colour) with a dark outline
    private static Texture2D MakeArrowTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        bool Inside(float x, float y, float grow)
        {
            // head: triangle from y 4..34 (tip at the bottom), shaft: x 22..42, y 34..60
            float hy = y - 4f;
            bool head = hy >= -grow && hy <= 30f + grow && Mathf.Abs(x - 31.5f) <= hy * (28f / 30f) + grow;
            bool shaft = x >= 22f - grow && x <= 42f + grow && y >= 34f - grow && y <= 60f + grow;
            return head || shaft;
        }
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // texture rows go bottom-up: flip so the tip points down on screen
                int fy = n - 1 - y;
                Color32 c = new(0, 0, 0, 0);
                if (Inside(x, fy, 0f)) c = new Color32(255, 255, 255, 255);
                else if (Inside(x, fy, 3f)) c = new Color32(20, 40, 20, 255);
                px[y * n + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    public static void Reset()
    {
        foreach (var b in banners) { if (b.text != null) Object.Destroy(b.text.gameObject); if (b.flash != null) Object.Destroy(b.flash.gameObject); }
        banners.Clear();
        for (int i = 0; i < 2; i++) if (arrows[i] != null) { arrows[i].gameObject.SetActive(false); labels[i].gameObject.SetActive(false); }
    }
}
