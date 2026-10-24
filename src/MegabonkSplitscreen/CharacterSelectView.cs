using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Character select scene once player 2 has joined:
//  - clones player 1's 3D avatar (CharacterDisplay) and places the copy to the screen-right of P1
//  - moves the camera's character-select marker (MenuCamera.characterSelectionTransform) back, and a bit
//    to the right so the view centres between both avatars. The game's own camera motion flies there.
// The clone still listens to the game's selection events, so it mirrors P1's pick until P2 gets a panel.
internal static class CharacterSelectView
{
    private static ConfigEntry<float> spacing;
    private static ConfigEntry<float> pullBack;
    private static ConfigEntry<float> centerShift;

    private static GameObject p2Avatar;
    private static float fixedOffset = -1f;      // P1-P2 distance, measured once per session
    private static Vector3 faceDir;              // towards the character-select camera
    private static GameObject p2Model;        // the character model inside the clone (PlayerRenderer.rendererObject)
    private static GameObject staleModel;     // replaced model to check on the next frame
    private static Transform movedMarker;     // the marker we already moved (scene reloads give a new one)
    private static float logViewAt = -1f;      // when to log where the avatars ended up on screen

    public static void Init(ConfigFile config)
    {
        spacing = config.Bind("CharacterSelect", "P2Spacing", 1.6f,
            "Distance from P1's avatar to P2's, as a multiple of the avatar's width.");
        pullBack = config.Bind("CharacterSelect", "CameraPullBack", 1.2f,
            "How far the camera moves back, as a multiple of the P1-P2 distance.");
        centerShift = config.Bind("CharacterSelect", "CameraCenterShift", 0.5f,
            "How far the camera moves right, as a multiple of the P1-P2 distance (0.5 = centred between them).");
    }

    public static void OnP2Joined() => Setup();

    // P2 left the lobby: P2's avatar goes and the camera marker goes back where the game had it
    private static Vector3 markerOriginal;
    public static void OnP2Left()
    {
        if (p2Avatar != null) Object.Destroy(p2Avatar);
        p2Avatar = null;
        p2Model = null;
        if (movedMarker != null) { movedMarker.position = markerOriginal; ModLog.Info("Camera marker back where the game had it (P2 left)"); }
        movedMarker = null;
    }

    public static void OnMenuOpened()
    {
        if (PlayerJoin.HasP2) Setup();
    }

    // Cut the clone loose from P1's selection:
    //  - remove its CharacterDisplay, which listens to the game's (static) character/skin selection events
    //    (destroyed before its Start runs, so it never subscribes)
    //  - give every renderer its own material copies; the clone shared P1's runtime materials, so re-skinning
    //    P1 re-textured P2 (texture changed, model didn't)
    private static void Detach(GameObject clone)
    {
        foreach (var d in clone.GetComponentsInChildren<CharacterDisplay>(true))
            Object.Destroy(d);

        int count = 0;
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
        {
            r.materials = r.materials;   // the getter returns fresh per-renderer copies
            count++;
        }
        ModLog.Info($"P2 avatar detached from P1's selection ({count} renderers given own materials)");
    }

    // P2 picked a character: swap the clone's model the same way the game builds a player
    // (PlayerRenderer.SetCharacter, no inventory in the menu), then give the new materials to the clone alone.
    public static void SetP2Character(CharacterData data)
    {
        if (p2Avatar == null) return;
        var renderers = p2Avatar.GetComponentsInChildren<PlayerRenderer>(true);
        if (renderers.Length == 0)
        {
            ModLog.Warn("CharacterSelectView: P2 avatar has no PlayerRenderer");
            return;
        }
        var pr = renderers[0];

        // SetCharacter replaces the model it remembers in rendererObject. That field is set at runtime, so
        // Instantiate may not carry it over to the clone: then the old model would be left standing.
        var oldModel = p2Model;
        ModLog.Info($"P2 avatar before swap: rendererObject={(pr.rendererObject != null ? pr.rendererObject.name : "null")}, our model={(oldModel != null ? oldModel.name : "null")}");
        if (pr.rendererObject == null && oldModel != null) pr.rendererObject = oldModel;
        try
        {
            pr.SetCharacter(data, null, faceDir.sqrMagnitude > 0.0001f ? faceDir : pr.transform.forward);
        }
        catch (Exception e)
        {
            ModLog.Error($"P2 avatar SetCharacter failed: {e}");
            return;
        }
        p2Model = pr.rendererObject;
        // Destroy happens at the end of the frame, so check next frame whether the game removed the old model
        if (oldModel != null && oldModel != p2Model) staleModel = oldModel;
        foreach (var r in p2Avatar.GetComponentsInChildren<Renderer>(true))
            r.materials = r.materials;
        ModLog.Info($"P2 avatar set to {data.eCharacter}");
    }

    // P2's skin on the avatar: the game's own PlayerRenderer.SetSkin, then own material copies again. Returns the name
    // of the skinned renderer it went on (P2Body puts the skin on the same renderer of P2's body in the run).
    private static int skinLogs;
    public static string SetP2Skin(SkinData skin)
    {
        if (p2Avatar == null || skin == null) return null;
        var renderers = p2Avatar.GetComponentsInChildren<PlayerRenderer>(true);
        if (renderers.Length == 0) return null;
        var pr = renderers[0];
        try { pr.SetSkin(skin); }
        catch (Exception e)
        {
            ModLog.Error($"P2 avatar SetSkin failed: {e.Message}");
            return null;
        }
        foreach (var r in p2Avatar.GetComponentsInChildren<Renderer>(true))
            r.materials = r.materials;
        var smr = pr.renderer;
        // TEMP [skins]: what SetSkin did - the skin's materials next to the renderer's afterwards
        if (skinLogs++ < 6)
        {
            string Names(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material> m) =>
                m == null ? "none" : string.Join(", ", m.Select(x => x != null ? x.name : "null"));
            ModLog.Info($"[skins] avatar SetSkin({skin.name}): skin materials [{Names(skin.materials)}], renderer '{smr?.name}' now [{(smr != null ? Names(smr.sharedMaterials) : "-")}]");
        }
        return smr != null ? smr.name : null;
    }

    // P1's CharacterDisplay. FindObjectOfType alone can return P2's clone (it has a CharacterDisplay too).
    private static CharacterDisplay FindP1Display()
    {
        foreach (var d in Object.FindObjectsOfType<CharacterDisplay>())
            if (d != null && d.gameObject != p2Avatar) return d;
        return null;
    }

    private static void Setup()
    {
        var display = FindP1Display();
        if (display == null)
        {
            ModLog.Warn("CharacterSelectView: no CharacterDisplay in the scene");
            return;
        }
        var menuCam = Object.FindObjectOfType<MenuCamera>();
        var cam = menuCam != null && menuCam.camera != null ? menuCam.camera : Camera.main;
        if (cam == null)
        {
            ModLog.Warn("CharacterSelectView: no camera found");
            return;
        }

        // Screen-right in the world, kept level so the avatar stays on the same ground height.
        Vector3 right = cam.transform.right;
        right.y = 0f;
        right.Normalize();

        // Distance: measured from P1's model width the first time, then kept (user: coming back to character select
        // with another P1 character put P2 closer, since the width differs per character)
        if (fixedOffset < 0f)
        {
            float width = 1f;
            var smr = display.playerRenderer != null ? display.playerRenderer.renderer : null;
            if (smr != null) width = Mathf.Max(smr.bounds.size.x, smr.bounds.size.z, 0.5f);
            fixedOffset = width * spacing.Value;
        }
        float offset = fixedOffset;

        var p1 = display.transform;
        if (p2Avatar == null)
        {
            p2Avatar = Object.Instantiate(display.gameObject, p1.parent);
            p2Avatar.name = "MegabonkSplitscreen_P2Display";
            p2Avatar.transform.SetPositionAndRotation(p1.position + right * offset, p1.rotation);
            p2Avatar.transform.localScale = p1.localScale;
            Detach(p2Avatar);

            // Face the character-select camera like P1 stands (the copy can carry P1's current spin; user saw P2
            // facing away after coming back from a run)
            faceDir = cam.transform.position - p2Avatar.transform.position;
            faceDir.y = 0f;
            faceDir = faceDir.sqrMagnitude > 0.0001f ? faceDir.normalized : -right;
            foreach (var r in p2Avatar.GetComponentsInChildren<PlayerRenderer>(true))
                if (r != null) r.transform.rotation = Quaternion.LookRotation(faceDir, Vector3.up);

            // The clone's copy of P1's current model, found by the same path under the clone
            var p1Model = display.playerRenderer != null ? display.playerRenderer.rendererObject : null;
            var m = p1Model != null ? P2Select.Mirror(p1, p1Model.transform, p2Avatar.transform) : null;
            p2Model = m != null ? m.gameObject : null;
            ModLog.Info($"P2 avatar model: {(p2Model != null ? p2Model.name : "not found")} (P1's: {(p1Model != null ? p1Model.name : "null")})");
            ModLog.Info($"P2 avatar spawned at {p2Avatar.transform.position} (P1 at {p1.position}, offset {offset:F2}, facing {faceDir})");
        }

        var marker = menuCam != null ? menuCam.characterSelectionTransform : null;
        if (marker == null)
        {
            ModLog.Warn("CharacterSelectView: MenuCamera.characterSelectionTransform not found, camera not moved");
        }
        else if (marker != movedMarker)
        {
            Vector3 before = marker.position;
            markerOriginal = before;
            marker.position = before - marker.forward * (offset * pullBack.Value) + right * (offset * centerShift.Value);
            movedMarker = marker;
            ModLog.Info($"Camera marker moved {before} -> {marker.position} (fov {cam.fieldOfView:F0})");
        }

        CharacterSelectLayout.ApplyP1();
        P2Panel.Ensure(Object.FindObjectOfType<CharacterMenu>());
        logViewAt = Time.unscaledTime + 2f;
    }

    // Called every frame by the Ticker; logs once, after the camera has had time to arrive.
    public static void Update()
    {
        P2Panel.Sync();
        if (staleModel != null)
        {
            ModLog.Warn($"P2 avatar: old model '{staleModel.name}' survived SetCharacter, removing it");
            Object.Destroy(staleModel);
        }
        staleModel = null;
        if (logViewAt < 0f || Time.unscaledTime < logViewAt) return;
        logViewAt = -1f;

        var display = FindP1Display();
        var menuCam = Object.FindObjectOfType<MenuCamera>();
        var cam = menuCam != null && menuCam.camera != null ? menuCam.camera : Camera.main;
        if (display == null || cam == null || p2Avatar == null) return;

        // Viewport: (0,0) = bottom-left of the screen, (1,1) = top-right
        Vector3 v1 = cam.WorldToViewportPoint(display.transform.position);
        Vector3 v2 = cam.WorldToViewportPoint(p2Avatar.transform.position);
        ModLog.Info($"On screen: P1 avatar at x={v1.x:F2} y={v1.y:F2}, P2 avatar at x={v2.x:F2} y={v2.y:F2} (camera at {cam.transform.position})");
    }
}
