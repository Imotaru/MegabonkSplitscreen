using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// P2's jump / landing dust (user: P1 gets a dust effect when jumping and landing, P2 doesn't). [dust] log: the game's
// PlayerFx (on P1) listens to PlayerMovement.A_Jumped (OnJumped) and A_Landed (OnLanded, which also shakes P1's camera)
// and shows its jumpFx / landFx; P2's movement events reach P2's own listeners only. P2's events (P2Movement) call
// these: a copy of P1's effect at P2's feet, played, removed after a few seconds.
internal static class P2Dust
{
    private static PlayerFx fx;
    private static int logs;

    public static void Jump() => Play(true);
    public static void Land(float fallSpeed) => Play(false);

    private static void Play(bool jump)
    {
        try
        {
            if (fx == null) fx = Object.FindObjectOfType<PlayerFx>();
            var src = jump ? fx?.jumpFx : fx?.landFx;
            if (src == null || !P2Movement.Active) return;
            var at = P2Movement.BodyPosition();
            var copy = Object.Instantiate(src, at, src.transform.rotation);
            copy.SetActive(true);
            foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);
            Object.Destroy(copy, 3f);
            if (logs++ < 4) ModLog.Info($"[dust] P2 {(jump ? "jumped" : "landed")}: '{src.name}' played at P2's feet");
        }
        catch (Exception e) { if (logs++ < 6) ModLog.Warn($"[dust] {e.Message}"); }
    }

    // P2Movement's routed movement events: P2's own listeners plus these two
    public static object With(System.Reflection.PropertyInfo p, object routed)
    {
        Il2CppSystem.Delegate add = p.Name switch
        {
            "A_Jumped" => Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<PlayerMovement>>(new Action<PlayerMovement>(_ => Jump())),
            "A_Landed" => Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(s => Land(s))),
            _ => null,
        };
        if (add == null) return routed;
        var all = routed is Il2CppSystem.Delegate r ? Il2CppSystem.Delegate.Combine(r, add) : add;
        try { return typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetMethod(nameof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase.Cast))!.MakeGenericMethod(p.PropertyType).Invoke(all, null); }
        catch { return routed; }
    }
}
