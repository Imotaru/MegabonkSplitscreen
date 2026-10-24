using Assets.Scripts.Actors.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Death pieces 5 + 6 (user's plan): the alive player stands at the downed player's gravestone for ~10 s - the charge
// shrine's look as the countdown - then a portal in the downed player's colour opens on the ground facing up and the
// downed player jumps out of it, back in action (user: collision off while coming out, through the ground and portal).
//  - Countdown: copies of the game's ChargeShrine parts ([describe] log): the force-field sphere (the shrine's own
//    renderer, shader Kaima/Depth/ForceField, _MainColor) as the zone, its world canvas 'Canvas'/'Circle'/'Progress'
//    (radial fill) as the ring, its AudioStart/Loop/Complete/Abort sounds, and its finish effect
//    (ChargeShrineFinishFx). Leaving the zone aborts (like the shrine).
//  - Portal: a copy of the game's PlayerSpawnPortal (red-tinted for P2, as P2's spawn portal) turned to face up, its
//    'Portal' ring opening, 'PassingParticles' + SfxPassing as the player comes through.
//  - Rise: the player is moved up through the ground and portal (kinematic, colliders off), then released with an
//    upward push, collisions back, at half HP (Downed.Revive).
internal static class Revive
{
    // ExitSpeed / RiseTop: v0.46.1 (user: make the player jump a little higher) 9 -> 15, released higher up;
    // v0.48.0 (user: even higher, they sometimes get swarmed) 15 -> 22
    // Radius: v0.48.1 (user: the green sphere a little larger, maybe 1.5 times) 4.5 -> 6.75 (the zone that counts too)
    private const float Radius = 6.75f, ChargeTime = 10f, RingSize = 2.5f, RingHeight = 2.9f, PortalOpenTime = 0.6f, RiseTime = 0.7f, RiseDepth = 2.2f, RiseTop = 1.2f, ExitSpeed = 22f;
    private static readonly Color ZoneColor = new(0.2f, 1f, 0.35f, 1f);

    private class State
    {
        public float progress;
        public bool charging;
        public GameObject zone, ring;
        public UnityEngine.UI.Image fill;
        public AudioSource aStart, aLoop, aComplete, aAbort;
        public int phase;            // 0 charging, 1 portal opening, 2 rising, 3 done
        public float phaseStart;
        public GameObject portal;
        public Transform portalRing;
        public Vector3 portalRingScale, ground;
        public List<Collider> collidersOff = new();
    }
    private static readonly State[] states = { new State(), new State() };
    private static GameObject shrinePrefab, portalPrefab;
    private static int logs;

    public static bool Rising(int player) => states[player].phase >= 1 && states[player].phase < 3;
    public static float Progress(int player) => states[player].progress / ChargeTime;
    public static bool Charging(int player) => states[player].charging;

    private static bool IsDown(int p) => p == 0 ? Downed.P1Down : P2Health.IsDead;
    private static Vector3? AlivePos(int p)
    {
        if (p == 0) return MyPlayer.Instance != null ? MyPlayer.Instance.transform.position : null;
        var b = P2Weapons.Body;
        return b != null && b.activeInHierarchy ? b.transform.position : null;
    }

    // Each frame (P2Body.Update)
    public static void Update()
    {
        if (Time.deltaTime <= 0f || Assets.Scripts.Utility.MyTime.paused) return;
        for (int p = 0; p < 2; p++)
        {
            var s = states[p];
            if (!IsDown(p))
            {
                if (s.zone != null || s.portal != null || s.phase != 0 || s.progress > 0f) Clear(p);   // fresh for the next time
                continue;
            }
            if (s.phase >= 1) { Sequence(p); continue; }   // portal + rise (the gravestone is gone by then)
            var grave = Gravestones.Position(p);
            if (grave == null) continue;
            int other = 1 - p;
            if (IsDown(other)) { SetCharging(p, false); continue; }   // both down: the run ends
            Charge(p, grave.Value, AlivePos(other));
        }
    }

    // ---------- Piece 5: the countdown ----------
    private static void Charge(int p, Vector3 grave, Vector3? alive)
    {
        var s = states[p];
        if (s.zone == null) MakeZone(p, grave);
        bool inside = alive != null && Flat(alive.Value - grave) <= Radius;
        SetCharging(p, inside);
        // Outside: the progress drains at twice the speed (revive test: an enemy's push out of the zone at 8 of 10 s
        // started it all over - too harsh)
        if (inside) s.progress += Time.deltaTime;
        else if (s.progress > 0f) s.progress = Mathf.Max(0f, s.progress - 2f * Time.deltaTime);
        if (s.fill != null) s.fill.fillAmount = Mathf.Clamp01(s.progress / ChargeTime);
        if (s.aLoop != null) s.aLoop.pitch = Mathf.Lerp(0.9f, 1.4f, s.progress / ChargeTime);
        if (s.progress >= ChargeTime) Complete(p, grave);
    }

    private static void SetCharging(int p, bool on)
    {
        var s = states[p];
        if (s.charging == on) return;
        s.charging = on;
        if (on) { s.aStart?.Play(); s.aLoop?.Play(); }
        else
        {
            s.aLoop?.Stop();
            if (s.progress > 0.2f) s.aAbort?.Play();
        }
        if (s.ring != null) s.ring.SetActive(on);
        if (logs++ < 12) ModLog.Info($"[revive] P{2 - p} {(on ? "at" : "left")} P{p + 1}'s gravestone ({s.progress:F1}/{ChargeTime} s)");
    }

    private static void MakeZone(int p, Vector3 grave)
    {
        var s = states[p];
        if (shrinePrefab == null)
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "ChargeShrine" && !go.scene.IsValid()) { shrinePrefab = go; break; }
        if (shrinePrefab == null) { ModLog.Warn("[revive] the game's ChargeShrine prefab wasn't found"); return; }
        var cs = shrinePrefab.GetComponent<ChargeShrine>();
        var hold = new GameObject("hold");
        hold.SetActive(false);
        var copy = Object.Instantiate(shrinePrefab, hold.transform);
        // What the shrine's script points at, found in the copy by path before the script goes
        Transform Part(Component c) => c != null ? P2Select.Mirror(shrinePrefab.transform, c.transform, copy.transform) : null;
        var fillT = Part(cs?.circleProgress);
        var circleT = Part(cs?.circleParent);
        s.aStart = Part(cs?.audioStart)?.GetComponent<AudioSource>();
        s.aLoop = Part(cs?.audioLoop)?.GetComponent<AudioSource>();
        s.aComplete = Part(cs?.audioComplete)?.GetComponent<AudioSource>();
        s.aAbort = Part(cs?.audioAbort)?.GetComponent<AudioSource>();
        // Only the zone, the ring and the sounds stay: the altar, the icons and the heal zone go
        foreach (var name in new[] { "B_EnergyAltar", "Renderer", "AltarIcon", "MinimapIcon", "HealZone" })
        {
            var t = copy.transform.Find(name);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp" && mb.GetIl2CppType().Name != "LookAtCamera") Object.DestroyImmediate(mb);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) if (c != null) Object.DestroyImmediate(c);
        copy.name = $"MegabonkSplitscreen_P{p + 1}ReviveZone";
        copy.transform.SetParent(null, false);
        // The shrine is scale 25 with its zone sphere at that size: scaled to the revive radius (a unit sphere's
        // radius is 0.5), so the ring (a child) scales with it
        float scale = Radius * 2f;
        copy.transform.SetPositionAndRotation(grave, Quaternion.identity);
        copy.transform.localScale = Vector3.one * scale;
        Object.Destroy(hold);
        // The zone sphere is off in the prefab (the shrine's script turns it on): on, in green ([revive] log: the
        // prefab's _MainColor is a full blue (0, 0.56, 1, 1))
        var zr = copy.GetComponent<Renderer>();
        if (zr != null)
        {
            zr.enabled = true;
            if (zr.material != null && zr.material.HasProperty("_MainColor")) zr.material.SetColor("_MainColor", ZoneColor);
        }
        // The ring's canvas: the shrine shrinks it for its scale-25 body ([revive] log: 0.3 m across here) - sized to
        // RingSize and lifted over the gravestone
        var canvasT = copy.transform.Find("Canvas");
        if (canvasT != null && fillT != null)
        {
            var img = fillT.GetComponent<UnityEngine.UI.Image>();
            float rectW = img != null ? Mathf.Max(0.01f, img.rectTransform.rect.width) : 1.5f;
            canvasT.localScale = Vector3.one * (RingSize / rectW / scale);
            canvasT.localPosition = new Vector3(0f, RingHeight / scale, 0f);
        }
        copy.SetActive(true);
        s.zone = copy;
        s.fill = fillT?.GetComponent<UnityEngine.UI.Image>();
        if (s.fill != null) { s.fill.fillAmount = 0f; s.fill.color = new Color(0.4f, 1f, 0.45f, 1f); }
        s.ring = circleT != null ? circleT.gameObject : null;
        if (s.ring != null) s.ring.SetActive(false);
        s.progress = 0f;
        s.phase = 0;
        ModLog.Info($"[revive] P{p + 1}'s revive zone at {grave}: radius {Radius} m, ring {(s.fill != null ? "found" : "MISSING")}, sounds {(s.aLoop != null ? "found" : "MISSING")}");
        // TEMP (revive test screenshot: neither the zone nor the ring showed): their real sizes and colours
        var origColor = shrinePrefab.GetComponent<Renderer>()?.sharedMaterial;
        ModLog.Info($"[revive]   zone renderer {(zr != null ? $"enabled {zr.enabled}, bounds size {zr.bounds.size}, layer {LayerMask.LayerToName(copy.layer)}" : "none")}; prefab _MainColor {(origColor != null && origColor.HasProperty("_MainColor") ? origColor.GetColor("_MainColor").ToString() : "?")}; " +
                    $"ring {(s.fill != null ? $"world scale {s.fill.transform.lossyScale}, rect {s.fill.rectTransform.rect.size}, canvas '{s.fill.canvas?.name}' mode {s.fill.canvas?.renderMode}" : "none")}");
    }

    private static void Complete(int p, Vector3 grave)
    {
        var s = states[p];
        s.charging = false;
        s.aLoop?.Stop();
        s.aComplete?.Play();
        var cs = shrinePrefab?.GetComponent<ChargeShrine>();
        if (cs?.finishParticles != null)
        {
            var fx = Object.Instantiate(cs.finishParticles, grave + Vector3.up * 0.5f, Quaternion.identity);
            fx.SetActive(true);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true)) ps.Play();
            Object.Destroy(fx, 5f);
        }
        if (s.ring != null) s.ring.SetActive(false);
        ModLog.Info($"[revive] P{p + 1}'s countdown done - the portal opens");
        OpenPortal(p, grave);
        s.phase = 1;
        s.phaseStart = Time.time;
    }

    // ---------- Piece 6: the portal and the rise ----------
    private static void OpenPortal(int p, Vector3 grave)
    {
        var s = states[p];
        if (portalPrefab == null)
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == "PlayerSpawnPortal" && !go.scene.IsValid()) { portalPrefab = go; break; }
        Vector3 ground = grave;
        if (Physics.Raycast(grave + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 30f, GroundMask(), QueryTriggerInteraction.Ignore)) ground = hit.point;
        s.ground = ground;
        // The gravestone bursts as the portal opens in its place
        Gravestones.Remove(p, burst: true);
        if (portalPrefab == null) { ModLog.Warn("[revive] the game's PlayerSpawnPortal prefab wasn't found - no portal"); return; }
        var hold = new GameObject("hold");
        hold.SetActive(false);
        var copy = Object.Instantiate(portalPrefab, hold.transform);
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetIl2CppType().Assembly.GetName().Name == "Assembly-CSharp") Object.DestroyImmediate(mb);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) if (c != null) Object.DestroyImmediate(c);
        copy.name = $"MegabonkSplitscreen_P{p + 1}RevivePortal";
        if (p == 1) P2Body.TintRed(copy);   // P2's colour, as P2's spawn portal
        copy.transform.SetParent(null, false);
        // Facing up: the ring stands in the portal's xy plane (the spawn portal is upright) - tipped back 90 deg,
        // then moved so the ring sits just above the ground
        copy.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        s.portalRing = copy.transform.Find("Portal");
        Vector3 ringPos = s.portalRing != null ? s.portalRing.position : copy.transform.position;
        copy.transform.position += ground + Vector3.up * 0.15f - ringPos;
        s.portalRingScale = s.portalRing != null ? s.portalRing.localScale : Vector3.one;
        if (s.portalRing != null) s.portalRing.localScale = Vector3.zero;
        var passing = copy.transform.Find("PassingParticles");
        if (passing != null) passing.gameObject.SetActive(false);
        Object.Destroy(hold);
        copy.SetActive(true);
        s.portal = copy;
        Play(copy, "SfxStart");
        Play(copy, "SfxLoop");
        ModLog.Info($"[revive] P{p + 1}'s portal at {ground} ({(p == 1 ? "red" : "blue")}, facing up)");
    }

    private static void Sequence(int p)
    {
        var s = states[p];
        float t = Time.time - s.phaseStart;
        if (s.phase == 1)
        {
            if (s.portalRing != null) s.portalRing.localScale = s.portalRingScale * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / PortalOpenTime));
            if (t < PortalOpenTime) return;
            StartRise(p);
            s.phase = 2;
            s.phaseStart = Time.time;
            return;
        }
        if (s.phase == 2)
        {
            float k = Mathf.Clamp01(t / RiseTime);
            MoveTo(p, s.ground + Vector3.up * Mathf.Lerp(-RiseDepth, RiseTop, 1f - (1f - k) * (1f - k)));
            AirAnimation(p, takeOff: false);
            if (k < 1f) return;
            EndRise(p);
            s.phase = 3;
            s.phaseStart = Time.time;
        }
    }

    private static void StartRise(int p)
    {
        var s = states[p];
        s.collidersOff.Clear();
        var passing = s.portal != null ? s.portal.transform.Find("PassingParticles") : null;
        if (passing != null)
        {
            passing.gameObject.SetActive(true);
            foreach (var ps in passing.GetComponentsInChildren<ParticleSystem>(true)) ps.Play();
        }
        if (s.portal != null) Play(s.portal, "SfxPassing");
        if (p == 0)
        {
            var p1 = MyPlayer.Instance;
            var rb = p1?.playerMovement?.rb;
            if (rb != null) { rb.isKinematic = true; rb.velocity = Vector3.zero; }
            if (p1 != null) foreach (var c in p1.GetComponentsInChildren<Collider>()) if (c != null && c.enabled && !c.isTrigger) { c.enabled = false; s.collidersOff.Add(c); }
            Downed.ShowP1ForRise();
        }
        else
        {
            var pm = P2Movement.Pm;
            if (pm?.rb != null) { pm.rb.isKinematic = true; pm.rb.velocity = Vector3.zero; }
            if (pm != null) foreach (var c in pm.GetComponentsInChildren<Collider>()) if (c != null && c.enabled && !c.isTrigger) { c.enabled = false; s.collidersOff.Add(c); }
            P2Health.ShowForRise();
        }
        MoveTo(p, s.ground - Vector3.up * RiseDepth);
        AirAnimation(p, takeOff: true);
        ModLog.Info($"[revive] P{p + 1} rises through the portal ({s.collidersOff.Count} colliders off)");
    }

    // The jump/fall look while coming out (user: make them do their falling animation): the game's animator parameters
    // ("grounded", "moving", "jumping" - P2Control's [params] names) - P2's through P2Control; P1's on P1's model, with
    // P1's movement marked airborne so P1's own renderer doesn't set "grounded" back
    private static readonly int HGrounded = Animator.StringToHash("grounded"), HMoving = Animator.StringToHash("moving"), HJumping = Animator.StringToHash("jumping");
    private static float p1JumpPulseUntil;
    private static void AirAnimation(int p, bool takeOff)
    {
        if (p == 1) { P2Control.AirAnimation(takeOff); return; }
        var p1 = MyPlayer.Instance;
        if (p1?.playerMovement != null) p1.playerMovement.grounded = false;
        var anim = p1?.playerRenderer?.rendererObject?.GetComponentInChildren<Animator>();
        if (anim == null) return;
        if (takeOff) p1JumpPulseUntil = Time.time + 0.1f;
        anim.SetBool(HGrounded, false);
        anim.SetBool(HMoving, false);
        anim.SetBool(HJumping, Time.time < p1JumpPulseUntil);
    }

    private static void MoveTo(int p, Vector3 feet)
    {
        if (p == 0)
        {
            var p1 = MyPlayer.Instance;
            var rb = p1?.playerMovement?.rb;
            var model = p1?.playerRenderer?.rendererObject?.transform;
            // P1's root sits above the model's feet by this much (as P2's BodyPosition uses)
            Vector3 rootOffset = p1 != null && model != null ? p1.transform.position - model.position : Vector3.up * 1.5f;
            Vector3 to = feet + rootOffset;
            if (rb != null) rb.position = to;
            if (p1 != null) p1.transform.position = to;
        }
        else
        {
            var pm = P2Movement.Pm;
            var body = P2Weapons.Body;
            var p1 = MyPlayer.Instance;
            var model = p1?.playerRenderer?.rendererObject?.transform;
            Vector3 rootOffset = p1 != null && model != null ? p1.transform.position - model.position : Vector3.up * 1.5f;
            if (pm != null) { pm.transform.position = feet + rootOffset; if (pm.rb != null) pm.rb.position = feet + rootOffset; }
            if (body != null) body.transform.position = feet;
        }
    }

    private static void EndRise(int p)
    {
        var s = states[p];
        foreach (var c in s.collidersOff) if (c != null) c.enabled = true;
        s.collidersOff.Clear();
        Rigidbody rb = p == 0 ? MyPlayer.Instance?.playerMovement?.rb : P2Movement.Pm?.rb;
        Downed.Revive(p);
        if (rb != null && p == 0) { rb.isKinematic = false; rb.velocity = Vector3.up * ExitSpeed; }
        if (rb != null && p == 1) { rb.isKinematic = false; rb.velocity = Vector3.up * ExitSpeed; }   // P2's movement copy runs non-kinematic
        ReviveUi.ShowRevived(p);
        ModLog.Info($"[revive] P{p + 1} is back in action");
        if (s.portal != null) { Play(s.portal, "SfxClose"); ClosePortalLater(s.portal); s.portal = null; }
    }

    private static void ClosePortalLater(GameObject portal)
    {
        closing.Add((portal, Time.time));
    }
    private static readonly List<(GameObject go, float start)> closing = new();
    public static void UpdateClosing()
    {
        for (int i = closing.Count - 1; i >= 0; i--)
        {
            var (go, start) = closing[i];
            float t = Time.time - start;
            if (go == null) { closing.RemoveAt(i); continue; }
            var ring = go.transform.Find("Portal");
            if (ring != null && t > 0.8f) ring.localScale = Vector3.Lerp(ring.localScale, Vector3.zero, 1f - Mathf.Exp(-8f * Time.deltaTime));
            if (t > 2.5f) { Object.Destroy(go); closing.RemoveAt(i); }
        }
    }

    private static void Play(GameObject root, string name)
    {
        foreach (var a in root.GetComponentsInChildren<AudioSource>(true))
            if (a != null && a.name == name) { a.Play(); return; }
    }

    private static void Clear(int p)
    {
        var s = states[p];
        if (s.zone != null) Object.Destroy(s.zone);
        if (s.portal != null) Object.Destroy(s.portal);
        foreach (var c in s.collidersOff) if (c != null) c.enabled = true;
        states[p] = new State();
    }

    public static void Reset() { Clear(0); Clear(1); foreach (var (go, _) in closing) if (go != null) Object.Destroy(go); closing.Clear(); }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static int GroundMask()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.whatIsGround.value != 0 ? gm.whatIsGround.value : ~0;
    }
}
