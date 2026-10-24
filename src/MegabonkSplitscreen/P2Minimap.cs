using Assets.Scripts.Actors.Player;
using Assets.Scripts.Camera;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Minimaps for the splitscreen (user's layout): P1's minimap scaled down at the right edge of P1's (left) half,
// P2's copy at the left edge of P2's (right) half, mirroring it.
// In the game, MinimapCamera.minimapCamera renders a top-down view into a texture that a RawImage in the HUD shows,
// inside the minimap frame (compass letters); MinimapCamera.playerIcon follows P1 in that top-down world, and enemy
// icons live there too (so both maps show them).
//  - P1: the HUD frame holding that RawImage is scaled to MapScale and moved so its right edge is at the screen centre.
//  - P2: an inert copy of the frame (game scripts removed) with its left edge at the screen centre, showing P2's own
//    texture from a copy of the minimap camera that follows P2 with the same height/angle as P1's (turned by the
//    difference between P2's and P1's camera yaw, in case the map rotates with the camera); a red copy of the player
//    icon follows P2.
internal static class P2Minimap
{
    private const float MapScale = 0.75f;
    private const float Gap = 12f;   // canvas units between each map and the screen centre

    private static MinimapCamera mm;
    private static Camera p1MapCam, p2MapCam;
    private static RenderTexture p2Texture;
    private static Transform p1Icon, p2Icon;
    private static GameObject p2Frame, p1Frame;
    public static GameObject P1Frame => p1Frame;   // the game's minimap frame ('Minimap')
    public static GameObject P2Frame => p2Frame;   // P2's copy
    private static readonly HashSet<IntPtr> scaledFrames = new();
    private static Transform body;
    private static float canvasScale = 1f;
    private static Vector3 p1OriginalPos;

    public static void Start(GameObject p2Body)
    {
        body = p2Body.transform;
        if (p2Frame != null && p2MapCam != null) return;   // same stage, already made
        try
        {
            Build();
        }
        catch (Exception e)
        {
            ModLog.Error($"P2 minimap failed: {e}");
        }
    }

    private static void Build()
    {
        mm = Object.FindObjectOfType<MinimapCamera>();
        p1MapCam = mm?.minimapCamera;
        var tex = p1MapCam?.targetTexture;
        if (p1MapCam == null || tex == null)
        {
            ModLog.Warn($"P2 minimap: minimap camera ({p1MapCam != null}) or its texture ({tex != null}) not found");
            return;
        }

        // The HUD image showing the minimap texture, and its frame (the largest ancestor that is still map-sized)
        RawImage image = null;
        foreach (var ri in Object.FindObjectsOfType<RawImage>())
            if (ri != null && ri.texture != null && ri.texture.Pointer == tex.Pointer) { image = ri; break; }
        if (image == null)
        {
            ModLog.Warn("P2 minimap: HUD image showing the minimap not found");
            return;
        }
        var frame = image.rectTransform;
        for (var t = frame.parent?.TryCast<RectTransform>(); t != null; t = t.parent?.TryCast<RectTransform>())
        {
            if (t.rect.width > 700f || t.rect.height > 700f || t.GetComponent<Canvas>() != null) break;
            frame = t;
        }
        UiDump.DumpOnce("minimap_dump.txt", frame, p1MapCam.transform, mm.playerIcon);

        var canvas = frame.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.transform.lossyScale.x : 1f;
        canvasScale = scale;
        float centreX = Screen.width / 2f;

        // P1: scale, right edge at the centre (screen space, so it works whatever the anchors are); once per frame object
        p1Frame = frame.gameObject;
        if (scaledFrames.Add(frame.Pointer))
        {
            p1OriginalPos = frame.position;   // where the game had it (merged camera: P1's map goes back there)
            frame.localScale *= MapScale;
            PlaceEdge(frame, centreX - Gap * scale, rightEdge: true);
        }

        // P2: inert copy, left edge at the centre, same height
        var holder = new GameObject("MegabonkSplitscreen_MinimapHolder");
        holder.SetActive(false);
        holder.transform.SetParent(frame.parent, false);
        var copy = Object.Instantiate(frame.gameObject, holder.transform);
        copy.name = "MegabonkSplitscreen_P2Minimap";
        var copyImage = P2Select.Mirror(frame, image.transform, copy.transform)?.GetComponent<RawImage>();
        foreach (var m in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (m != null && m.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(m);
        copy.transform.SetParent(frame.parent, false);
        copy.transform.SetSiblingIndex(frame.GetSiblingIndex() + 1);
        Object.Destroy(holder);
        copy.SetActive(true);
        var copyRt = copy.GetComponent<RectTransform>();
        copyRt.localScale = frame.localScale;
        copyRt.position = new Vector3(copyRt.position.x, frame.position.y, frame.position.z);
        PlaceEdge(copyRt, centreX + Gap * scale, rightEdge: false);
        p2Frame = copy;

        // P2's top-down camera and texture
        p2Texture = new RenderTexture(tex.width, tex.height, tex.depth, tex.format);
        p2Texture.name = "MegabonkSplitscreen_P2MinimapTexture";
        var camHolder = new GameObject("MegabonkSplitscreen_P2MapCamHolder");
        camHolder.SetActive(false);
        var camCopy = Object.Instantiate(p1MapCam.gameObject, camHolder.transform);
        camCopy.name = "MegabonkSplitscreen_P2MinimapCamera";
        foreach (var m in camCopy.GetComponentsInChildren<MonoBehaviour>(true))
            if (m != null && m.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(m);
        foreach (var al in camCopy.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(al);
        camCopy.transform.SetParent(null, true);
        Object.Destroy(camHolder);
        camCopy.SetActive(true);
        p2MapCam = camCopy.GetComponent<Camera>();
        p2MapCam.targetTexture = p2Texture;
        if (copyImage != null) copyImage.texture = p2Texture;

        // P2's icon: a red copy of P1's
        p1Icon = mm.playerIcon;
        if (p1Icon != null)
        {
            var iconCopy = Object.Instantiate(p1Icon.gameObject);
            iconCopy.name = "MegabonkSplitscreen_P2MinimapIcon";
            foreach (var m in iconCopy.GetComponentsInChildren<MonoBehaviour>(true))
                if (m != null && m.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(m);
            foreach (var r in iconCopy.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.materials;   // own copies
                foreach (var mat in mats)
                    if (mat != null && mat.HasProperty("_Color")) mat.color = new Color(1f, 0.25f, 0.25f, 1f);
            }
            foreach (var sr in iconCopy.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr != null) sr.color = new Color(1f, 0.25f, 0.25f, 1f);
            p2Icon = iconCopy.transform;
        }
        ModLog.Info($"Minimaps split: P1 frame '{frame.name}' scaled {MapScale} at the centre's left, P2 copy at its right; " +
                    $"texture {tex.width}x{tex.height}, P2 icon {(p2Icon != null)}");
    }

    // Puts a rect's left or right edge at a screen x (keeps its y)
    private static void PlaceEdge(RectTransform rt, float screenX, bool rightEdge)
    {
        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(arr);
        float left = arr[0].x, right = arr[2].x;
        float dx = (rightEdge ? screenX - right : screenX - left);
        rt.position += new Vector3(dx, 0f, 0f);
    }

    // Called every frame (from P2Body.Update)
    public static void Update()
    {
        if (p2MapCam == null || body == null || p1MapCam == null) return;
        var p1 = MyPlayer.Instance;
        if (p1 == null) return;
        Vector3 p1Pos = p1.transform.position;
        Vector3 p2Pos = body.position;

        // Same height/offset as P1's map camera, turned by the yaw difference between the two player cameras
        float yawDelta = 0f;
        var p1Cam = PlayerCamera.Instance?.camera;
        if (P2Camera.Cam != null && p1Cam != null)
            yawDelta = Mathf.DeltaAngle(p1Cam.transform.eulerAngles.y, P2Camera.Cam.transform.eulerAngles.y);
        var turn = Quaternion.AngleAxis(yawDelta, Vector3.up);
        Vector3 offset = p1MapCam.transform.position - p1Pos;
        p2MapCam.transform.SetPositionAndRotation(p2Pos + turn * offset, turn * p1MapCam.transform.rotation);
        p2MapCam.orthographicSize = p1MapCam.orthographicSize;
        p2MapCam.fieldOfView = p1MapCam.fieldOfView;

        // Death piece 3 (screenshot: P2's red arrow covered P2's map skull while P2 was down): hidden while P2 is down,
        // like P1's (Downed) - the gravestone's green skull marks the spot
        if (p2Icon != null && p2Icon.gameObject.activeSelf == P2Health.IsDead) p2Icon.gameObject.SetActive(!P2Health.IsDead);
        if (p2Icon != null && p1Icon != null)
        {
            Vector3 iconOffset = p1Icon.position - p1Pos;
            p2Icon.SetPositionAndRotation(p2Pos + iconOffset, Quaternion.AngleAxis(yawDelta, Vector3.up) * p1Icon.rotation);
            p2Icon.localScale = p1Icon.lossyScale;
        }
        // Lego-style merge (shared view opening from the middle): P1's map rides the right edge of P1's own view, P2's the
        // left edge of P2's; merging, P1's map glides back to where the game had it and P2's is hidden once merged
        float s = P2Camera.MergeAmount;
        var p1Rt = p1Frame != null ? p1Frame.GetComponent<RectTransform>() : null;
        if (p1Rt != null)
        {
            PlaceEdge(p1Rt, Screen.width * P2Camera.SideWidth - Gap * canvasScale, rightEdge: true);
            // v0.41.7 (user: in the merged view the minimap should be more in the middle; autotest screenshot: at the
            // game's top-right spot P2's health bars covered it): merged, P1's map sits at the top centre (the game's height)
            if (s > 0f && p1OriginalPos != Vector3.zero)
            {
                float w = p1Rt.rect.width * p1Rt.lossyScale.x;
                var centre = new Vector3(Screen.width * 0.5f + (p1Rt.pivot.x - 0.5f) * w, p1OriginalPos.y, p1OriginalPos.z);
                p1Rt.position = Vector3.Lerp(p1Rt.position, centre, s);
            }
        }
        var p2Rt = p2Frame != null ? p2Frame.GetComponent<RectTransform>() : null;
        if (p2Rt != null) PlaceEdge(p2Rt, Screen.width * P2Camera.Divider + Gap * canvasScale, rightEdge: false);
        UpdateCompass(yawDelta, p1Cam);
        // 1.0 alpha (user: with a player down the minimap was P1's; minimap_down test: P1 down, the one view follows P2,
        // the map stayed on P1's gravestone): the alive player's map. P1 down -> P2's map in the shared spot, P1's hidden
        bool p2Solo = P2Camera.Solo == 2;
        bool show = p1Frame != null && p1Frame.activeSelf && (s < 0.98f || p2Solo);
        if (p2Frame != null && p2Frame.activeSelf != show) p2Frame.SetActive(show);
        if (p2Solo && p2Rt != null && p1Rt != null && p1OriginalPos != Vector3.zero)
        {
            float w = p2Rt.rect.width * p2Rt.lossyScale.x;
            p2Rt.position = Vector3.Lerp(p2Rt.position, new Vector3(Screen.width * 0.5f + (p2Rt.pivot.x - 0.5f) * w, p1OriginalPos.y, p1OriginalPos.z), s);
        }
        HideP1Map(p2Solo);
    }

    // P1's frame made invisible (not switched off: the game owns its active state)
    private static CanvasGroup p1Hide;
    private static void HideP1Map(bool hide)
    {
        if (p1Frame == null) return;
        if (p1Hide == null)
        {
            if (!hide) return;
            p1Hide = p1Frame.GetComponent<CanvasGroup>() ?? p1Frame.AddComponent<CanvasGroup>();
        }
        float want = hide ? 0f : 1f;
        if (p1Hide.alpha != want) p1Hide.alpha = want;
    }

    // ---------- Compass (user: P2's compass was wrong) ----------
    // The frame's 'Border' holds the N/S/E/W letters; the game's MinimapUi (removed from P2's copy) turns them with
    // P1's camera. P2's compass = P1's, turned by the yaw difference between the two cameras (like P2's map image).
    // Which way a camera turn turns the compass is learned from P1 (sign of border z change vs P1 camera yaw change).
    // Letters stay upright if P1's do, else they turn along.
    private static Transform p1Border, p2Border;
    private static readonly string[] Letters = { "N", "S", "E", "W" };
    private static float compassSign = 1f, lastP1Yaw = float.NaN, lastP1BorderZ = float.NaN;
    private static int signVotes, compassLogs;

    private static void UpdateCompass(float yawDelta, Camera p1Cam)
    {
        if (p1Frame == null || p2Frame == null) return;
        if (p1Border == null) p1Border = p1Frame.transform.Find("Border");
        if (p2Border == null) p2Border = p2Frame.transform.Find("Border");
        if (p1Border == null || p2Border == null) return;

        // Learn the direction from P1
        if (p1Cam != null)
        {
            float yaw = p1Cam.transform.eulerAngles.y, z = WorldZ(p1Border, Letter(p1Border, "N"));
            if (!float.IsNaN(lastP1Yaw))
            {
                float dYaw = Mathf.DeltaAngle(lastP1Yaw, yaw), dZ = Mathf.DeltaAngle(lastP1BorderZ, z);
                if (Mathf.Abs(dYaw) > 0.5f && Mathf.Abs(dZ) > 0.25f)
                {
                    signVotes = Mathf.Clamp(signVotes + (Mathf.Sign(dYaw) == Mathf.Sign(dZ) ? 1 : -1), -20, 20);
                    float sign = signVotes >= 0 ? 1f : -1f;
                    if (sign != compassSign || compassLogs == 0)
                    {
                        compassSign = sign;
                        if (compassLogs++ < 5) ModLog.Info($"[compass] P1's compass turns {(sign > 0 ? "with" : "against")} the camera yaw");
                    }
                }
            }
            lastP1Yaw = yaw;
            lastP1BorderZ = z;
        }

        var turn = Quaternion.AngleAxis(compassSign * yawDelta, Vector3.forward);
        p2Border.localRotation = turn * p1Border.localRotation;
        foreach (var name in Letters)
        {
            var a = Letter(p1Border, name);
            var b = Letter(p2Border, name);
            if (a == null || b == null) continue;
            b.localPosition = a.localPosition;
            b.localScale = a.localScale;
            // Upright in P1 (world z ~ 0) -> upright in P2; else turned along with the compass
            bool upright = Mathf.Abs(Mathf.DeltaAngle(0f, a.eulerAngles.z)) < 2f;
            b.rotation = upright ? a.rotation : turn * a.rotation;
        }
    }

    private static Transform Letter(Transform border, string name) => border != null ? border.Find(name) : null;

    // Where the compass points on screen: the angle of the N letter around the border's centre (works whether the game
    // turns the border or moves the letters), else the border's own rotation
    private static float WorldZ(Transform border, Transform n)
    {
        if (n != null)
        {
            Vector3 d = n.position - border.position;
            if (d.sqrMagnitude > 1f) return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }
        return border.eulerAngles.z;
    }

    public static void Stop()
    {
        HideP1Map(false);   // P1's map visible again
        p1Hide = null;
        if (p2MapCam != null) Object.Destroy(p2MapCam.gameObject);
        if (p2Icon != null) Object.Destroy(p2Icon.gameObject);
        if (p2Frame != null) Object.Destroy(p2Frame);
        if (p2Texture != null) p2Texture.Release();
        p2MapCam = null;
        p2Icon = null;
        p2Frame = null;
        p1Border = p2Border = null;
        lastP1Yaw = lastP1BorderZ = float.NaN;
        p2Texture = null;
        body = null;
    }
}
