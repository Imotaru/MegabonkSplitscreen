using Assets.Scripts.Actors.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// P2's item effects that the game shows through P1's own effect scripts (user: some treasure reward vfx don't get
// applied to p2). fxscan (v0.49): P1's 'Feet/ItemEffects' carries HolyBookEffect + ToxicBarrelEffect (their particles
// HolyBookFx / PoisonFx under it), listening to the static ItemHolyBook.A_OnUse / ItemToxicBarrel.A_OnUse (float radius)
// and ItemBase.A_ItemAdded (Refresh); P1's PlayerEffects.OnMirrorReady(bool) shows mirrorFx ('Orientation/Mirror').
//  - P2 gets a real copy of P1's ItemEffects under P2's 'Feet' (the game's scripts and particles); its scripts subscribe
//    themselves when they wake - ItemEvents files those listeners as P2's and routes the events by owner.
//  - P2 gets a copy of P1's mirror particles; ItemMirror.A_MirrorReady fired for P2 shows/hides P2's copy.
internal static class P2ItemFx
{
    private static GameObject itemEffects, mirror;
    private static readonly HashSet<IntPtr> p1Owned = new(), p2Owned = new();
    private static int logs;

    public static IReadOnlyCollection<IntPtr> P1Owned => p1Owned;
    public static IReadOnlyCollection<IntPtr> P2Owned => p2Owned;

    // Each frame in a run (P2Hud.Update): made once P2's body is there
    public static void Update()
    {
        var body = P2Weapons.Body;
        var p1 = MyPlayer.Instance;
        if (body == null || p1 == null) return;
        if (itemEffects == null) Build(p1, body);
    }

    private static void Build(MyPlayer p1, GameObject body)
    {
        p1Owned.Clear();
        p2Owned.Clear();
        var src = p1.GetComponentsInChildren<HolyBookEffect>(true).FirstOrDefault()?.gameObject;
        if (src == null) return;
        foreach (var mb in src.GetComponents<MonoBehaviour>()) if (mb != null) p1Owned.Add(mb.Pointer);
        var pe = p1.GetComponentsInChildren<PlayerEffects>(true).FirstOrDefault();
        if (pe != null) p1Owned.Add(pe.Pointer);

        var parentName = src.transform.parent != null ? src.transform.parent.name : "";
        var onto = FindNamed(body.transform, parentName) ?? body.transform;
        var holder = new GameObject("MegabonkSplitscreen_ItemFxHolder");
        holder.SetActive(false);
        holder.transform.SetParent(onto, false);
        var copy = Object.Instantiate(src, holder.transform);
        copy.name = src.name;   // same names as P1's (the sweep compares effect names)
        foreach (var mb in copy.GetComponents<MonoBehaviour>()) if (mb != null) p2Owned.Add(mb.Pointer);
        copy.transform.SetParent(onto, false);
        copy.transform.localPosition = src.transform.localPosition;
        copy.transform.localRotation = src.transform.localRotation;
        Object.Destroy(holder);
        copy.SetActive(true);   // the game's scripts wake and subscribe - ItemEvents takes those listeners as P2's
        itemEffects = copy;

        if (pe?.mirrorFx != null)
        {
            var mparent = pe.mirrorFx.transform.parent;
            var monto = mparent != null ? FindNamed(body.transform, mparent.name) ?? body.transform : body.transform;
            var mh = new GameObject("MegabonkSplitscreen_MirrorHolder");
            mh.SetActive(false);
            mh.transform.SetParent(monto, false);
            mirror = Object.Instantiate(pe.mirrorFx, mh.transform);
            mirror.name = pe.mirrorFx.name;
            mirror.transform.SetParent(monto, false);
            mirror.transform.localPosition = pe.mirrorFx.transform.localPosition;
            mirror.transform.localRotation = pe.mirrorFx.transform.localRotation;
            Object.Destroy(mh);
            mirror.SetActive(false);
        }
        ItemEvents.Keep(now: true);
        ModLog.Info($"[itemfx] P2's item effects made: '{src.name}' copied under P2's '{onto.name}' ({string.Join(", ", copy.GetComponents<MonoBehaviour>().Select(m => m.GetIl2CppType().Name))}), mirror {(mirror != null ? "copied" : "not found")}");
    }

    // ItemMirror.A_MirrorReady for P2 (ItemEvents)
    public static void P2MirrorReady(bool ready)
    {
        if (mirror == null) return;
        if (mirror.activeSelf != ready) mirror.SetActive(ready);
        if (logs++ < 6) ModLog.Info($"[itemfx] P2's mirror {(ready ? "ready (shown)" : "used (hidden)")}");
    }

    private static Transform FindNamed(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == name) return t;
        return null;
    }

    public static void Reset()
    {
        if (itemEffects != null) Object.Destroy(itemEffects);   // its OnDestroy unsubscribes
        if (mirror != null) Object.Destroy(mirror);
        itemEffects = mirror = null;
        p1Owned.Clear();
        p2Owned.Clear();
    }
}
