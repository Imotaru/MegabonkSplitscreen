using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Billboards face P2's camera in P2's view (user: pickups like the clock and shield turn towards P1's camera, so in
// P2's half they looked flat). The game's LookAtCamera turns them towards the game's camera (P1's). Just before P2's
// camera draws, each one is turned by the rotation between P1's and P2's cameras (yaw only for xzOnly ones), and put
// back right after, so P1's view and the game's own logic don't change.
// Works with both of Unity's per-camera callbacks (built-in pipeline: Camera.onPreCull/onPostRender; scriptable
// pipelines: RenderPipelineManager.begin/endCameraRendering) - whichever the game uses fires.
internal static class Billboards
{
    private static readonly List<LookAtCamera> found = new();
    private static readonly List<Pickup> foundPickups = new();
    private static bool pickupsLogged, textsLogged;
    private static readonly List<TMPro.TextMeshPro> foundTexts = new();
    private static readonly List<(Transform t, Quaternion rot)> saved = new();
    private static float nextFind;
    private static bool hooked;
    private static string firedBy;

    public static void Init()
    {
        if (hooked) return;
        hooked = true;
        try
        {
            oldPreCull = Camera.onPreCull;
            oldPostRender = Camera.onPostRender;
            Camera.onPreCull = Combine(Camera.onPreCull, DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(c => Before(c, "onPreCull"))));
            Camera.onPostRender = Combine(Camera.onPostRender, DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(c => After(c))));
        }
        catch (Exception e) { ModLog.Warn($"Billboards: built-in camera callbacks failed: {e.Message}"); }
        try
        {
            RenderPipelineManager.add_beginCameraRendering(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(
                new Action<ScriptableRenderContext, Camera>((_, c) => Before(c, "beginCameraRendering"))));
            RenderPipelineManager.add_endCameraRendering(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(
                new Action<ScriptableRenderContext, Camera>((_, c) => After(c))));
        }
        catch (Exception e) { ModLog.Warn($"Billboards: render pipeline callbacks failed: {e.Message}"); }
        ModLog.Info("Billboards: per-camera turning on");
    }

    // Quitting the game (user: the game hung after P2 chose Quit): Unity may still call camera callbacks while the mod's
    // runtime is shutting down - put the game's own callbacks back first
    private static Camera.CameraCallback oldPreCull, oldPostRender;
    public static void Shutdown()
    {
        if (!hooked) return;
        hooked = false;
        try
        {
            Camera.onPreCull = oldPreCull;
            Camera.onPostRender = oldPostRender;
        }
        catch { }
    }

    private static Camera.CameraCallback Combine(Camera.CameraCallback a, Camera.CameraCallback b) =>
        a == null ? b : Il2CppSystem.Delegate.Combine(a, b).Cast<Camera.CameraCallback>();

    // TEMP [perf] (user: the game stutters a bit - logging or hooks?): the billboard code's own time per frame
    private static readonly System.Diagnostics.Stopwatch perf = new();
    private static int perfFrames, perfLogs;
    private static void PerfTick()
    {
        if (++perfFrames < 300) return;
        if (perfLogs++ < 6) ModLog.Info($"[perf] billboards: {perf.Elapsed.TotalMilliseconds / perfFrames:F2} ms per frame (over {perfFrames} frames)");
        perf.Reset();
        perfFrames = 0;
    }

    private static void Before(Camera cam, string via)
    {
        perf.Start();
        try { BeforeInner(cam, via); }
        finally { perf.Stop(); }
    }

    // v0.49.2 (user: with P1 down, the billboards didn't face P2's camera): in the merged / solo view P1's camera is
    // drawn from the shared spot (behind P2 when P1 is down) while the game aimed its billboards at its own spot (P1's
    // gravestone) - for that drawing they're turned from the game's spot to the drawn one, and back after
    // 1.0 alpha [perf] (user: the Steam Deck dropped frames; the billboards cost ~1.2 ms a frame on the PC, in LateTick
    // alone): the scene-wide FindObjectsOfType for LookAtCamera sprites / 3D texts ran 3-4 times every frame. One shared
    // list per kind now, refreshed every 3rd frame (~20 times a second at 60 fps: a new damage number waits at most
    // ~50 ms to be turned); the per-frame work only walks the lists
    private const int RefreshFrames = 3;
    private static readonly List<LookAtCamera> looks = new();
    private static readonly List<TMPro.TextMeshPro> texts = new();
    private static int looksFrame = -100, textsFrame = -100;
    private static List<LookAtCamera> Looks()
    {
        if (Time.frameCount - looksFrame >= RefreshFrames)
        {
            looksFrame = Time.frameCount;
            looks.Clear();
            foreach (var l in Object.FindObjectsOfType<LookAtCamera>()) if (l != null) looks.Add(l);
        }
        return looks;
    }
    private static List<TMPro.TextMeshPro> Texts()
    {
        if (Time.frameCount - textsFrame >= RefreshFrames)
        {
            textsFrame = Time.frameCount;
            texts.Clear();
            foreach (var tx in Texts()) if (tx != null) texts.Add(tx);
        }
        return texts;
    }

    private static IntPtr turnedFor;
    private static void TurnForSharedView(Camera p1Cam)
    {
        Vector3 c1 = P2Camera.GamePos, c2 = p1Cam.transform.position;
        if ((c2 - c1).sqrMagnitude < 0.25f) return;
        foreach (var l in Looks())
        {
            if (l == null || !l.isActiveAndEnabled || OnCanvas(l)) continue;
            var t = l.transform;
            Vector3 d1 = c1 - t.position, d2 = c2 - t.position;
            if (l.xzOnly) { d1.y = 0f; d2.y = 0f; }
            if (d1.sqrMagnitude < 1e-6f || d2.sqrMagnitude < 1e-6f) continue;
            saved.Add((t, t.rotation));
            t.rotation = Quaternion.LookRotation(d2, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(d1, Vector3.up)) * t.rotation;
        }
        if (saved.Count > 0) turnedFor = p1Cam.Pointer;
        if (saved.Count > 0 && sharedLogs++ < 3) ModLog.Info($"Billboards: {saved.Count} turned for the shared view (drawn {Vector3.Distance(c1, c2):F0} m from the game's camera spot)");
    }
    private static int sharedLogs;

    private static void BeforeInner(Camera cam, string via)
    {
        try
        {
            var p2Cam = P2Camera.Cam;
            var p1Cam = PlayerCamera.Instance?.camera;
            if (cam == null || p1Cam == null || saved.Count > 0) return;
            if (cam.Pointer == p1Cam.Pointer && P2Camera.Overridden) { TurnForSharedView(p1Cam); return; }
            if (p2Cam == null || cam.Pointer != p2Cam.Pointer) return;
            turnedFor = p2Cam.Pointer;
            if (firedBy == null) { firedBy = via; ModLog.Info($"Billboards: turned for P2's camera via {via}"); }
            // v0.44.16 (user: P2's damage numbers looked off; screenshot: edge-on slivers in P2's view): damage numbers
            // are LookAtCamera sprites living under a second - with the list refreshed once a second most were never
            // turned. Looked up every frame now (pickups stay once a second)
            found.Clear();
            found.AddRange(Looks());
            if (Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                // Pickups that don't spin (hearts, clock, shield...) face the game's camera themselves (user: a heart
                // looked off in P2's view); spinning ones look the same from anywhere
                foundTexts.Clear();
                foundPickups.Clear();
                // v0.44.14 (billboard autotest): a pickup whose sprite is a LookAtCamera child isn't turned itself - the
                // pickup object doesn't face the camera in P1's view (~60 deg off), and turning it swung its sprite
                // child (already aimed at P2's camera above) up to 89 deg off
                foreach (var p in Object.FindObjectsOfType<Pickup>())
                    if (p != null && !p.animateRotation && !p.pickedUp && p.GetComponentInChildren(Il2CppType.Of<LookAtCamera>(), true) == null) foundPickups.Add(p);
                if (!pickupsLogged && foundPickups.Count > 0)
                {
                    pickupsLogged = true;
                    ModLog.Info($"Billboards: non-spinning pickups turned for P2 too: {string.Join(", ", foundPickups.Select(p => p.ePickup.ToString()).Distinct())}");
                }
            }
            var full = p2Cam.transform.rotation * Quaternion.Inverse(p1Cam.transform.rotation);
            var yaw = Quaternion.Euler(0f, Mathf.DeltaAngle(p1Cam.transform.eulerAngles.y, p2Cam.transform.eulerAngles.y), 0f);
            // v0.44.14 (billboard autotest: the game's LookAtCamera points each sprite AT the camera's position - 0.0 deg
            // off the line to P1's camera - so one rotation for all (the cameras' difference) left sprites off to the
            // side up to 90 deg off in P2's view): each is turned by the rotation from its line to P1's camera to its
            // line to P2's camera (flat for xzOnly ones), which keeps the game's own up/invert choices
            Vector3 c1 = p1Cam.transform.position, c2 = p2Cam.transform.position;
            foreach (var l in found)
            {
                if (l == null || !l.isActiveAndEnabled || OnCanvas(l)) continue;   // canvas ones: LateTick
                var t = l.transform;
                saved.Add((t, t.rotation));
                Vector3 d1 = c1 - t.position, d2 = c2 - t.position;
                if (l.xzOnly) { d1.y = 0f; d2.y = 0f; }
                if (d1.sqrMagnitude < 1e-6f || d2.sqrMagnitude < 1e-6f) { t.rotation = (l.xzOnly ? yaw : full) * t.rotation; continue; }
                // v0.44.15 (user: P2's hearts looked rotated weird): FromToRotation's shortest turn added a roll around
                // the line. Now the upright look-frame at P1's camera is mapped onto the upright one at P2's (world up
                // kept), so the sprite keeps the game's own tilt relative to its camera, without a roll
                t.rotation = Quaternion.LookRotation(d2, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(d1, Vector3.up)) * t.rotation;
            }
            // 3D texts (damage popups etc.; user: P2's hit numbers faced P1's camera). Short-lived, so looked up
            // every frame, not once a second
            foreach (var tx in Texts())
            {
                if (tx == null || !tx.isActiveAndEnabled) continue;
                // v0.44.17 (user: damage numbers / FLEX looked off; screenshot: mirrored numbers in P2's view): a text
                // that is (or sits under) a LookAtCamera sprite was turned twice - aimed above, then by the cameras'
                // difference again (180 deg apart = seen from behind). The sprite's own turn is enough
                if (tx.GetComponentInParent<LookAtCamera>() != null) continue;
                var t = tx.transform;
                saved.Add((t, t.rotation));
                t.rotation = full * t.rotation;
                if (!textsLogged) { textsLogged = true; ModLog.Info($"Billboards: 3D texts turned for P2 too (e.g. '{tx.name}')"); }
            }
            foreach (var p in foundPickups)
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                var t = p.transform;
                saved.Add((t, t.rotation));
                t.rotation = full * t.rotation;
            }
            if (checkLabel != null) Measure(p1Cam, p2Cam);
        }
        catch (Exception e) { if (errors++ < 3) ModLog.Warn($"Billboards: {e.Message}"); }
    }

    // ---------- Autotest check (user: make sure P2's billboards are angled right, also with P2's camera turned) ----------
    // On P2's next drawn frame, after the turning: for every turned object, how far its facing is off the line to the
    // camera - in P1's view (the game's own facing, the reference) and in P2's view. Facing either way along the
    // object's forward axis counts (some face away by design, LookAtCamera.invert); xzOnly ones are measured flat.
    private static int treeLogs;
    private static bool checkCanvasOff;

    // ---------- Sprites on a canvas (damage numbers, FLEX / blocked popups) ----------
    // v0.44.18 ([bbtree] log: damage numbers are TextMeshProUGUI on the world-space 'DamageNumbersCanvas'; user: still
    // mirrored in P2's view; screenshot): a canvas builds its geometry once a frame, before any camera draws, so turning
    // its sprites just for P2's camera changes nothing on screen (the forced rebuild didn't either). Instead, every
    // frame after the game's LookAtCamera (Update) aimed them at P1's camera, the ones nearer P2 are aimed at P2's
    // camera for real (same mapping as above). P1 then sees P2's numbers aimed at P2's camera - small and far off in
    // P1's half. Merged view (one camera): left as the game made them.
    private static readonly Dictionary<IntPtr, bool> onCanvas = new();
    private static bool OnCanvas(LookAtCamera l)
    {
        if (onCanvas.TryGetValue(l.Pointer, out var v)) return v;
        v = l.GetComponentInParent<Canvas>() != null;
        if (onCanvas.Count > 2000) onCanvas.Clear();
        onCanvas[l.Pointer] = v;
        return v;
    }

    private static int canvasLogs;
    public static void LateTick()
    {
        perf.Start();
        try { LateTickInner(); }
        finally { perf.Stop(); PerfTick(); }
    }

    private static void LateTickInner()
    {
        var p2Cam = P2Camera.Cam;
        var p1Cam = PlayerCamera.Instance?.camera;
        var body = P2Weapons.Body;
        var p1 = Assets.Scripts.Actors.Player.MyPlayer.Instance;
        if (p1Cam == null || body == null || p1 == null) return;
        // Merged / solo view (only P1's camera, drawn from the shared spot): canvas sprites (damage numbers) aimed from
        // the game's camera spot at the drawn one, for real (a canvas is built once a frame)
        if ((p2Cam == null || !p2Cam.enabled) && P2Camera.Overridden)
        {
            Vector3 g = P2Camera.GamePos, s = p1Cam.transform.position;
            if ((s - g).sqrMagnitude < 0.25f) return;
            foreach (var l in Looks())
            {
                if (l == null || !l.isActiveAndEnabled || !OnCanvas(l)) continue;
                var t = l.transform;
                Vector3 d1 = g - t.position, d2 = s - t.position;
                if (l.xzOnly) { d1.y = 0f; d2.y = 0f; }
                if (d1.sqrMagnitude < 1e-6f || d2.sqrMagnitude < 1e-6f) continue;
                t.rotation = Quaternion.LookRotation(d2, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(d1, Vector3.up)) * t.rotation;
            }
            return;
        }
        if (p2Cam == null || !p2Cam.enabled) return;
        Vector3 c1 = p1Cam.transform.position, c2 = p2Cam.transform.position, b1 = p1.transform.position, b2 = body.transform.position;
        int n = 0;
        foreach (var l in Looks())
        {
            if (l == null || !l.isActiveAndEnabled || !OnCanvas(l)) continue;
            var t = l.transform;
            Vector3 p = t.position;
            if ((p - b2).sqrMagnitude >= (p - b1).sqrMagnitude) continue;   // P1's
            Vector3 d1 = c1 - p, d2 = c2 - p;
            if (l.xzOnly) { d1.y = 0f; d2.y = 0f; }
            if (d1.sqrMagnitude < 1e-6f || d2.sqrMagnitude < 1e-6f) continue;
            t.rotation = Quaternion.LookRotation(d2, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(d1, Vector3.up)) * t.rotation;
            n++;
        }
        if (n > 0 && canvasLogs++ < 3) ModLog.Info($"Billboards: {n} canvas sprite(s) near P2 (damage numbers...) aimed at P2's camera");
    }
    private static string checkLabel;
    private static Action<string> checkReport;
    public static void RequestCheck(string label, Action<string> report) { checkLabel = label; checkReport = report; }

    private static void Measure(Camera p1Cam, Camera p2Cam)
    {
        string label = checkLabel;
        checkLabel = null;
        // Two ways a sprite can face a camera: pointing at its position ("line") or parallel to its view ("facing");
        // each kind is judged by the one the game's own P1 view follows (the smaller average there)
        var groups = new Dictionary<string, (int n, float line1, float line2, float face1, float face2, float worstLine, float worstFace, string wl, string wf)>();
        var rolls = new Dictionary<string, (float worst, string name)>();
        var names = new Dictionary<string, int>();
        foreach (var (t, _) in saved) if (t != null) { string nm = t.name.Replace("(Clone)", ""); names[nm] = names.TryGetValue(nm, out var c) ? c + 1 : 1; }
        foreach (var (t, orig) in saved)
        {
            if (t == null) continue;
            var look = t.GetComponent<LookAtCamera>();
            bool flat = look != null && look.xzOnly;
            string kind = look != null ? (flat ? "LookAtCamera xz" : "LookAtCamera") : t.GetComponent<Pickup>() != null ? "pickup" : "3D text";
            Vector3 f1 = orig * Vector3.forward, f2 = t.rotation * Vector3.forward;
            float l1 = Off(f1, p1Cam.transform.position - t.position, flat), l2 = Off(f2, p2Cam.transform.position - t.position, flat);
            float v1 = Off(f1, p1Cam.transform.forward, flat), v2 = Off(f2, p2Cam.transform.forward, flat);
            // Roll (v0.44.15, user: hearts rotated weird although they faced P2's camera): the object's up around its
            // line to the camera, against world up - P2's view must match P1's
            float r = Mathf.Abs(Mathf.DeltaAngle(Roll(orig, p1Cam.transform.position - t.position), Roll(t.rotation, p2Cam.transform.position - t.position)));
            rolls.TryGetValue(kind, out var rw);
            if (r > rw.worst || rw.name == null) rolls[kind] = (Mathf.Max(r, rw.worst), r >= rw.worst ? t.name : rw.name);
            groups.TryGetValue(kind, out var g);
            if (g.n == 0 || Mathf.Abs(l2 - l1) > g.worstLine) { g.worstLine = Mathf.Abs(l2 - l1); g.wl = t.name; }
            if (g.n == 0 || Mathf.Abs(v2 - v1) > g.worstFace) { g.worstFace = Mathf.Abs(v2 - v1); g.wf = t.name; }
            g.n++; g.line1 += l1; g.line2 += l2; g.face1 += v1; g.face2 += v2;
            groups[kind] = g;
        }
        // TEMP [bbtree] (user: damage numbers still looked off; screenshot: mirrored in P2's view while the check said
        // OK): one damage number's make-up - every object under it, its components, and its facing in both views
        if (treeLogs < 2)
            foreach (var (t, orig) in saved)
            {
                if (t == null || !t.name.StartsWith("DamageNumbers")) continue;
                treeLogs++;
                var lines = new List<string>();
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                {
                    var comps = c.GetComponents<Component>().Select(x => x.GetIl2CppType().Name).Where(n => n != "Transform");
                    lines.Add($"'{c.name}' (parent '{c.parent?.name}') [{string.Join(", ", comps)}] fwd-to-P2cam {Vector3.Angle(c.forward, p2Cam.transform.position - c.position):F0} deg, local rot {c.localEulerAngles}");
                }
                var la = t.GetComponent<LookAtCamera>();
                ModLog.Info($"[bbtree] {t.name}: LookAtCamera invert {la?.invert}, xzOnly {la?.xzOnly}, target '{la?.target?.name}'; parent '{t.parent?.name}'; saved orig fwd-to-P1cam {Vector3.Angle(orig * Vector3.forward, p1Cam.transform.position - t.position):F0}; " +
                            $"P2 camera '{p2Cam.name}', P1 camera '{p1Cam.name}', Camera.main '{Camera.main?.name}' | {string.Join(" | ", lines)}");
                break;
            }
        // Canvas sprites (damage numbers): not turned per camera - the ones near P2 must face P2's camera the way the
        // ones near P1 face P1's (as drawn: front/back counts)
        var body = P2Weapons.Body;
        var p1 = Assets.Scripts.Actors.Player.MyPlayer.Instance;
        string canvasPart = "";
        if (body != null && p1 != null)
        {
            var near1 = new List<float>();
            var near2 = new List<(float a, float roll)>();
            var rollsP1 = new List<float>();
            foreach (var l in Object.FindObjectsOfType<LookAtCamera>())
            {
                if (l == null || !l.isActiveAndEnabled || !OnCanvas(l)) continue;
                var t = l.transform;
                bool nearP2 = (t.position - body.transform.position).sqrMagnitude < (t.position - p1.transform.position).sqrMagnitude;
                var cam = nearP2 ? p2Cam : p1Cam;
                float a = Off(t.forward, cam.transform.position - t.position, l.xzOnly), r = Roll(t.rotation, cam.transform.position - t.position);
                if (nearP2) near2.Add((a, r)); else { near1.Add(a); rollsP1.Add(r); }
            }
            if (near2.Count > 0)
            {
                float ref1 = near1.Count > 0 ? near1.Average() : 180f, refRoll = rollsP1.Count > 0 ? rollsP1.Average() : 0f;
                float worst = near2.Max(x => Mathf.Abs(x.a - ref1)), worstRoll = near2.Max(x => Mathf.Abs(Mathf.DeltaAngle(x.roll, refRoll)));
                if (worst >= 10f || worstRoll >= 5f) checkCanvasOff = true;
                canvasPart = $"; canvas sprites (damage numbers): near P1 x{near1.Count} face P1's camera at {ref1:F1} deg, near P2 x{near2.Count} face P2's at {near2.Average(x => x.a):F1} deg, worst difference {worst:F1}, worst roll difference {worstRoll:F1}";
            }
            else canvasPart = $"; canvas sprites: none near P2 (near P1 x{near1.Count})";
        }
        float yawDiff = Mathf.DeltaAngle(p1Cam.transform.eulerAngles.y, p2Cam.transform.eulerAngles.y);
        int lookAtPlayer = Object.FindObjectsOfType<LookAtPlayer>().Length;
        bool ok = true;
        var parts = new List<string>();
        foreach (var kv in groups)
        {
            var g = kv.Value;
            bool byLine = kv.Key.StartsWith("LookAtCamera") || g.line1 <= g.face1;   // LookAtCamera aims at the position
            float worst = byLine ? g.worstLine : g.worstFace;
            rolls.TryGetValue(kv.Key, out var rw);
            if (worst >= 10f || rw.worst >= 5f) ok = false;
            parts.Add($"{kv.Key} x{g.n} (game faces the camera's {(byLine ? "position" : "view direction")}): P1 view {(byLine ? g.line1 : g.face1) / g.n:F1} deg off, " +
                      $"P2 view {(byLine ? g.line2 : g.face2) / g.n:F1} deg off, worst difference {worst:F1} ('{(byLine ? g.wl : g.wf)}'), worst roll difference {rw.worst:F1} ('{rw.name}')");
        }
        if (checkCanvasOff) { ok = false; checkCanvasOff = false; }
        checkReport?.Invoke($"billboards {label}: {(groups.Count == 0 ? "NONE FOUND" : ok ? "OK" : "OFF")} - cameras {yawDiff:F0} deg apart; {string.Join("; ", parts)}{canvasPart};LookAtPlayer objects (not turned) {lookAtPlayer}; objects: {string.Join(", ", names.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"))}");
    }

    // Signed angle of the object's up around the line to the camera, measured from world up (both projected onto the
    // plane across the line)
    private static float Roll(Quaternion rot, Vector3 toCam)
    {
        if (toCam.sqrMagnitude < 1e-6f) return 0f;
        Vector3 n = toCam.normalized;
        Vector3 up = Vector3.ProjectOnPlane(rot * Vector3.up, n), world = Vector3.ProjectOnPlane(Vector3.up, n);
        if (up.sqrMagnitude < 1e-6f || world.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.SignedAngle(world, up, n);
    }

    // Angle between the object's forward axis and the line to the camera, 0..180 - front and back count apart
    // (v0.44.17: "either direction" let P2's mirrored damage numbers pass); P2's must match P1's
    private static float Off(Vector3 fwd, Vector3 toCam, bool flat)
    {
        if (flat) { fwd.y = 0f; toCam.y = 0f; }
        if (fwd.sqrMagnitude < 1e-6f || toCam.sqrMagnitude < 1e-6f) return 0f;
        return Vector3.Angle(fwd, toCam);
    }

    private static void After(Camera cam)
    {
        if (saved.Count == 0) return;
        if (cam == null || cam.Pointer != turnedFor) return;   // P2's camera, or P1's in the shared view
        for (int i = saved.Count - 1; i >= 0; i--) { var (t, rot) = saved[i]; if (t != null) t.rotation = rot; }   // reverse: nested ones exactly back
        saved.Clear();
        turnedFor = IntPtr.Zero;
    }

    private static int errors;
}
