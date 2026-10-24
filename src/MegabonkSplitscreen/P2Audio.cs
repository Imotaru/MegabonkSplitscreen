using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// P2 hears what happens around P2 (user: enemy hit sounds didn't play for P2 far from P1).
// Unity has one AudioListener - on P1's camera - and the game's hit sounds are 3D AudioSources at the hit, so around
// a far-away P2 they were out of range. Every frame, a playing 3D sound nearer to P2's camera than to the listener,
// and too far from the listener to be heard well, becomes 2D with its volume set from its distance to P2's camera,
// following the sound's own rolloff (min/max distance, logarithmic or linear). When it stops, its settings go back
// (the game reuses its sound objects). Merged view: nothing to do (the listener is between the players).
internal static class P2Audio
{
    private class Held { public AudioSource s; public float volume, blend; }
    private static readonly Dictionary<IntPtr, Held> held = new();
    private static int remapLogs;

    public static void Update()
    {
        var p2Cam = P2Camera.Cam;
        var listener = PlayerCamera.Instance?.camera;
        bool active = p2Cam != null && p2Cam.enabled && listener != null && P2Movement.Active;

        // Finished (or no longer needed): settings back
        if (held.Count > 0)
        {
            var done = new List<IntPtr>();
            foreach (var kv in held)
            {
                var h = kv.Value;
                if (h.s != null && h.s.isPlaying && active) continue;
                if (h.s != null) { h.s.volume = h.volume; h.s.spatialBlend = h.blend; }
                done.Add(kv.Key);
            }
            foreach (var k in done) held.Remove(k);
        }
        if (!active) return;

        Vector3 lp = listener.transform.position, p2 = p2Cam.transform.position;
        foreach (var s in Object.FindObjectsOfType<AudioSource>())
        {
            if (s == null || !s.isPlaying) continue;
            var key = s.Pointer;
            if (held.TryGetValue(key, out var h))
            {
                // Keep following P2's distance (the source may move)
                s.volume = h.volume * Rolloff(s, Vector3.Distance(s.transform.position, p2));
                continue;
            }
            if (s.spatialBlend < 0.5f) continue;   // already 2D (music, UI)
            Vector3 pos = s.transform.position;
            float d1 = Vector3.Distance(pos, lp), d2 = Vector3.Distance(pos, p2);
            if (d2 >= d1 || d1 < s.maxDistance * 0.5f) continue;   // P1 hears it (well enough) already
            held[key] = new Held { s = s, volume = s.volume, blend = s.spatialBlend };
            s.spatialBlend = 0f;
            s.volume = s.volume * Rolloff(s, d2);
            if (remapLogs++ < 10)
                ModLog.Info($"[audio] '{s.name}' ({s.clip?.name}) plays for P2: {d2:F0} from P2, {d1:F0} from the listener " +
                            $"(rolloff {s.rolloffMode}, min {s.minDistance:F0}, max {s.maxDistance:F0})");
        }
    }

    // How loud a 3D sound would be at distance d, per its own rolloff
    private static float Rolloff(AudioSource s, float d)
    {
        float min = Mathf.Max(0.01f, s.minDistance), max = Mathf.Max(min + 0.01f, s.maxDistance);
        if (d >= max) return 0f;
        if (d <= min) return 1f;
        return s.rolloffMode == AudioRolloffMode.Logarithmic ? min / d : 1f - (d - min) / (max - min);
    }
}
