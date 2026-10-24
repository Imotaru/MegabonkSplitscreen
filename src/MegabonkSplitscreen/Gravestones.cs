using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Death piece 2 (user's plan): a gravestone where a player went down, until they're revived. The game's own
// 'Gravestone' prefab ([findassets] log: prefab/asset, 2 mesh renderers, material m_gravestone) - copied with its game
// scripts and colliders taken off (scenery only: nobody bumps into it or interacts with it), standing on the ground
// below the spot, turned the way the player faced.
internal static class Gravestones
{
    private static readonly GameObject[] stones = new GameObject[2];
    private static GameObject prefab;
    private static int logs;
    private const float Scale = 0.6f;

    public static Vector3? Position(int player) => stones[player] != null ? stones[player].transform.position : null;

    public static void Place(int player, Vector3 at, Vector3 facing)
    {
        Remove(player);
        if (prefab == null)
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "Gravestone" && !go.scene.IsValid()) { prefab = go; break; }
        if (prefab == null) { ModLog.Warn("[grave] the game's 'Gravestone' prefab wasn't found"); return; }

        Vector3 ground = at;
        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 30f, GroundMask(), QueryTriggerInteraction.Ignore)) ground = hit.point;
        facing.y = 0f;
        // v0.45.5 (user: the gravestone lay flat on the ground, it should stand): the prefab lies with its lettered face
        // up and its plinth at the far (+z) end - tipped forward 90 deg it stands on the plinth, face looking along
        // `facing` (towards the other player); upright on slopes too, at 60 % (standing it'd be 3.3 m high)
        // v0.45.6 (user: the gravestone is upside down): tipped back the other way - the +z end up; the lettered face
        // (+y) then looks along -forward, so forward is set opposite to `facing`
        var rot = (facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-facing.normalized, Vector3.up) : Quaternion.identity) * Quaternion.Euler(-90f, 0f, 0f);

        var hold = new GameObject("hold");
        hold.SetActive(false);
        var copy = Object.Instantiate(prefab, hold.transform);
        int scripts = 0, colliders = 0;
        // Game scripts off - except MinimapIcon (it only turns the map icon towards the map camera)
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp" && mb.GetIl2CppType().Name != "MinimapIcon") { Object.DestroyImmediate(mb); scripts++; }
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) if (c != null) { Object.DestroyImmediate(c); colliders++; }
        copy.name = $"MegabonkSplitscreen_P{player + 1}Gravestone";
        copy.transform.SetParent(null, false);
        copy.transform.SetPositionAndRotation(ground, rot);
        copy.transform.localScale = Vector3.one * Scale;
        Object.Destroy(hold);
        copy.SetActive(true);
        // Centred on the spot, its base on the ground (a little in, so it doesn't float on uneven ground)
        var stoneRenderer = copy.GetComponent<Renderer>();
        if (stoneRenderer != null)
        {
            var b = stoneRenderer.bounds;
            copy.transform.position += new Vector3(ground.x - b.center.x, ground.y - b.min.y - 0.1f, ground.z - b.center.z);
        }
        stones[player] = copy;
        MapSkull(player, copy);

        if (logs++ < 6)
        {
            var b = new Bounds(ground, Vector3.zero);
            foreach (var r in copy.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            ModLog.Info($"[grave] P{player + 1}'s gravestone at {ground} (went down at {at}): {scripts} game scripts and {colliders} colliders taken off, size {b.size}, scale {copy.transform.localScale}");
            // TEMP: its parts (one of them looked lying down, and P1's didn't show in a screenshot)
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            {
                var r = t.GetComponent<Renderer>();
                ModLog.Info($"[grave]   '{t.name}' (parent '{t.parent?.name}') local pos {t.localPosition} rot {t.localEulerAngles} scale {t.localScale}, active {t.gameObject.activeInHierarchy}, layer {LayerMask.LayerToName(t.gameObject.layer)}" +
                            (r != null ? $", renderer {r.GetIl2CppType().Name} enabled {r.enabled} bounds {r.bounds.center} size {r.bounds.size}, material {r.sharedMaterial?.name}" : ""));
            }
        }
    }

    // ---------- Piece 3: a green skull on the map and minimap ----------
    // The gravestone carries the game's own map icon ('MinimapIcon (1)': a quad on the UiCamera layer the map cameras
    // draw, material m_encounterGravestone, turned by the game's MinimapIcon script). Its material is copied with the
    // game's 'skull' texture ([findassets]: skull 32x32) in green - the shader's texture/colour names are logged first.
    private static Texture2D skullTex;
    private static int skullLogs;
    private static readonly Color SkullGreen = new(0.35f, 1f, 0.35f, 1f);
    private const float SkullSize = 14f;   // world size on the map (the game's gravestone icon: 8; screenshot: 10 was arrow-sized)

    private static void MapSkull(int player, GameObject stone)
    {
        var icon = stone.transform.Find("MinimapIcon (1)");
        var r = icon != null ? icon.GetComponent<Renderer>() : null;
        if (r == null || r.sharedMaterial == null) { ModLog.Warn("[skull] the gravestone has no map icon"); return; }
        if (skullTex == null)
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (t != null && t.name == "skull") { skullTex = t; break; }
        var mat = new Material(r.sharedMaterial) { name = $"MegabonkSplitscreen_P{player + 1}SkullIcon" };
        var sh = mat.shader;
        var texProps = new List<string>();
        var colorProps = new List<string>();
        for (int i = 0; sh != null && i < sh.GetPropertyCount(); i++)
        {
            var type = sh.GetPropertyType(i);
            string n = sh.GetPropertyName(i);
            if (type == UnityEngine.Rendering.ShaderPropertyType.Texture) texProps.Add(n);
            else if (type == UnityEngine.Rendering.ShaderPropertyType.Color) colorProps.Add(n);
        }
        if (skullTex != null) foreach (var n in texProps) if (mat.GetTexture(n) != null) mat.SetTexture(n, skullTex);
        foreach (var n in colorProps) mat.SetColor(n, SkullGreen);
        r.sharedMaterial = mat;
        icon.localScale = Vector3.one * (SkullSize / Scale);
        if (skullLogs++ < 2)
            ModLog.Info($"[skull] P{player + 1}'s map icon: shader '{sh?.name}', textures [{string.Join(", ", texProps)}], colours [{string.Join(", ", colorProps)}]; " +
                        $"skull texture {(skullTex != null ? $"'{skullTex.name}' {skullTex.width}x{skullTex.height}" : "NOT FOUND")}; icon layer {LayerMask.LayerToName(icon.gameObject.layer)}, MinimapIcon script kept {icon.GetComponent<MinimapIcon>() != null}");
    }

    public static void Remove(int player) => Remove(player, false);

    // burst: the gravestone's own 'StoneExplosion' particles ([grave] log: Poof, Remains, Sparks - off in the prefab)
    // play where the stone was (a revive)
    public static void Remove(int player, bool burst)
    {
        var s = stones[player];
        stones[player] = null;
        if (s == null) return;
        if (burst)
        {
            var fx = s.transform.Find("StoneExplosion");
            if (fx != null)
            {
                fx.SetParent(null, true);
                fx.gameObject.SetActive(true);
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) { ps.gameObject.SetActive(true); ps.Play(); }
                Object.Destroy(fx.gameObject, 4f);
                ModLog.Info($"[grave] P{player + 1}'s gravestone bursts (StoneExplosion)");
            }
        }
        Object.Destroy(s);
    }

    public static void Reset() { Remove(0); Remove(1); }

    private static int GroundMask()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.whatIsGround.value != 0 ? gm.whatIsGround.value : ~0;
    }
}
