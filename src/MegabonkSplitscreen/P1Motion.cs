using Assets.Scripts.Actors.Player;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Player.Movement;
using UnityEngine;

namespace MegabonkSplitscreen;

// P2's run speed and jump, measured from P1 in the game (user: "same character, but P2 walks and jumps differently").
// P1 moves by rigidbody physics (forces, friction, drag, extra gravity), so P2's simple mover copies the RESULT instead
// of the inputs:
//  - Run speed: P1's horizontal ground speed, the 90th percentile over the last few seconds of grounded movement
//    (ignores brief spikes such as knockback; P1 standing still doesn't lower it).
//  - Jump: from P1's jumps that land at about the take-off height: height h, rise time and fall time ->
//    rising gravity 2h/rise^2, take-off speed = that gravity * rise time, falling gravity 2h/fall^2.
//  - P2 vs P1 differences use the game's own numbers: PlayerMovementValues.GetMoveSpeedMultiplier(character) and the
//    MoveSpeedMultiplier / JumpHeight stats (P2's from P2Inventory). Same character, no tomes -> same as P1.
// Until P1 has run / jumped, defaults are used.
internal static class P1Motion
{
    private const float DefaultSpeed = 8f, DefaultJumpSpeed = 12f, DefaultGravity = 30f;
    private const float Window = 6f;

    private static readonly List<(float t, float speed)> speeds = new();
    private static float runSpeed = -1f;
    private static float riseGravity = -1f, fallGravity = -1f, takeOffSpeed = -1f;

    private static Vector3 lastPos;
    private static bool hasLast, wasGrounded = true;
    private static bool inJump;
    private static float jumpY0, jumpT0, apexY, apexT;
    private static int jumpLogs, speedLogs, rejectLogs, airLogs;
    private static float nextPercentile;

    // Called every unpaused frame (from P2Control)
    public static void Update()
    {
        var p1 = MyPlayer.Instance;
        var pm = PlayerMovement.Instance;
        float dt = Time.deltaTime;
        if (p1 == null || pm == null || dt <= 0f)
        {
            hasLast = false;
            return;
        }
        Vector3 pos = p1.transform.position;
        Vector3 delta = pos - lastPos;
        bool had = hasLast;
        lastPos = pos;
        hasLast = true;
        if (!had || P2Body.IsRecording) return;   // not during the portal walk (the portal moves P1)

        float now = Time.time;
        bool grounded = pm.grounded;
        delta.y = 0f;
        float speed = delta.magnitude / dt;
        if (grounded && speed > 1f && speed < 60f) speeds.Add((now, speed));
        speeds.RemoveAll(s => now - s.t > Window);
        if (now >= nextPercentile && speeds.Count >= 20)
        {
            nextPercentile = now + 0.5f;
            var sorted = speeds.Select(s => s.speed).OrderBy(v => v).ToList();
            float p90 = sorted[(int)(sorted.Count * 0.9f)];
            // Keep the highest stable value: v0.22.1 followed P1's recent pace and dropped to 2.56 when P1 walked slowly
            if (p90 > runSpeed + 0.2f)
            {
                runSpeed = p90;
                if (speedLogs++ < 8) ModLog.Info($"[motion] P1 run speed measured: {runSpeed:F2}");
            }
        }

        // Jumps
        float y = pos.y;
        if (wasGrounded && !grounded)
        {
            if (airLogs++ < 3) ModLog.Info($"[motion] P1 left the ground at y {y:F2}");   // TEMP: is grounded seen at all?
            inJump = true;
            jumpY0 = y; jumpT0 = now; apexY = y; apexT = now;
        }
        else if (inJump && !grounded && y > apexY)
        {
            apexY = y; apexT = now;
        }
        else if (inJump && grounded)
        {
            inJump = false;
            // PlayerMovement.grounded stays true for a moment after the jump (grace time), so "left the ground" is seen
            // ~2 units up (v0.22.3: every jump "landed -2.05 vs take-off"). So the landing height is the baseline:
            //  h = apex - landing; rising gravity from the seen part of the rise (apex - first airborne y over rise time);
            //  take-off speed = sqrt(2 g h) for the full height; falling gravity = 2h / fall^2.
            float h = apexY - y, seen = apexY - jumpY0, rise = apexT - jumpT0, fall = now - apexT;
            bool ok = h > 0.5f && seen > 0.2f && rise > 0.1f && fall > 0.1f && Mathf.Abs(y - jumpY0) < 3f;
            if (!ok && rejectLogs++ < 10)
                ModLog.Info($"[motion] P1 air time not used as a jump: height {h:F2}, seen rise {seen:F2} in {rise:F2} s, fall {fall:F2} s");
            if (ok)
            {
                riseGravity = 2f * seen / (rise * rise);
                takeOffSpeed = Mathf.Sqrt(2f * riseGravity * h);
                fallGravity = 2f * h / (fall * fall);
                if (jumpLogs++ < 5)
                    ModLog.Info($"[motion] P1 jump measured: height {h:F2}, rise {rise:F2} s, fall {fall:F2} s -> take-off {takeOffSpeed:F1}, gravity up {riseGravity:F1} / down {fallGravity:F1}");
            }
        }
        wasGrounded = grounded;
    }

    // ---------- Values for P2 ----------

    public static float RunSpeed() => (runSpeed > 0f ? runSpeed : DefaultSpeed) * SpeedRatio();
    public static float JumpSpeed() => (takeOffSpeed > 0f ? takeOffSpeed : DefaultJumpSpeed) * Mathf.Sqrt(JumpRatio());
    public static float RiseGravity() => riseGravity > 0f ? riseGravity : DefaultGravity;
    public static float FallGravity() => fallGravity > 0f ? fallGravity : DefaultGravity;

    private static float SpeedRatio()
    {
        float r = 1f;
        try
        {
            var p1Char = MyPlayer.Instance?.inventory?.characterData;
            var p2Char = P2Select.Picked;
            if (p1Char != null && p2Char != null)
            {
                float m1 = PlayerMovementValues.GetMoveSpeedMultiplier(p1Char.eCharacter);
                float m2 = PlayerMovementValues.GetMoveSpeedMultiplier(p2Char.eCharacter);
                if (m1 > 0f && m2 > 0f) r *= m2 / m1;
            }
            r *= StatRatio(EStat.MoveSpeedMultiplier);
        }
        catch { }
        return Mathf.Clamp(r, 0.3f, 3f);
    }

    private static float JumpRatio() => Mathf.Clamp(StatRatio(EStat.JumpHeight), 0.3f, 3f);

    // P2's stat / P1's stat (1 when either is missing or zero)
    private static float StatRatio(EStat s)
    {
        if (P2Inventory.Weapons == null) return 1f;
        var ps = MyPlayer.Instance?.inventory?.playerStats;
        if (ps == null || P2Inventory.InP2Context) return 1f;
        float p1 = ps.stats.ContainsKey(s) ? ps.stats[s] : 0f;
        float p2 = P2Inventory.Get(s);
        return p1 > 0.01f && p2 > 0.01f ? p2 / p1 : 1f;
    }
}
