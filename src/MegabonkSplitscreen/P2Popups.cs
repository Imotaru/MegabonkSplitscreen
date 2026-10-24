using Assets.Scripts.Actors.Player;
using HarmonyLib;
using UnityEngine;

namespace MegabonkSplitscreen;

// P2's damage popups (user: P1 gets a "-32 hp"-style popup when hurt, P2 didn't).
// In the game EffectManager.OnDamage (P1's PlayerHealth damage event) shows it through EffectManager.PopupText - a
// number version (damage, colour, position, size) and a text version. Rule (log, don't guess): the first time P1 is
// hit, the call the game makes is recorded (which version, colour, size, text, offset above P1) and logged; P2's hits
// then make the same call above P2. Until P1 has been hit once, P2 gets none.
internal static class P2Popups
{
    private static bool inP1Damage, learned, useText;
    private static Color color;
    private static int size;
    private static string textFormat;
    private static Vector3 offset;

    public static void Apply(Harmony harmony)
    {
        int n = 0;
        var onDamage = AccessTools.DeclaredMethod(typeof(EffectManager), nameof(EffectManager.OnDamage));
        if (onDamage != null && P2Weapons.IsNativeCodeUnique(onDamage) && !P2Weapons.IsNativeCodeHooked(onDamage))
        {
            harmony.Patch(onDamage, prefix: new HarmonyMethod(typeof(P2Popups), nameof(BeforeP1Damage)),
                finalizer: new HarmonyMethod(typeof(P2Popups), nameof(AfterP1Damage)));
            n++;
        }
        foreach (var m in typeof(EffectManager).GetMethods().Where(x => x.Name == nameof(EffectManager.PopupText) && x.DeclaringType == typeof(EffectManager)))
        {
            if (!P2Weapons.IsNativeCodeUnique(m) || P2Weapons.IsNativeCodeHooked(m)) continue;
            var ps = m.GetParameters();
            string patch = ps[0].ParameterType == typeof(string) ? nameof(SeenText) : nameof(SeenNumber);
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(P2Popups), patch));
            n++;
        }
        ModLog.Info($"P2Popups: {n} hooks");
    }

    private static void BeforeP1Damage() => inP1Damage = true;
    private static Exception AfterP1Damage(Exception __exception) { inP1Damage = false; return __exception; }

    private static void SeenNumber(float damage, Color color, ref Vector3 position, int textSize)
    {
        Learn(false, damage.ToString("0"), color, position, textSize);
        MoveToP2(ref position, damage.ToString("0"));
    }
    private static void SeenText(string text, Color color, ref Vector3 position, int textSize)
    {
        Learn(true, text, color, position, textSize);
        MoveToP2(ref position, text);
    }

    // A popup the game makes while it runs P2's health (P2GameHealth: an Aegis block, an evade...) is placed at "the
    // player" (P1): moved to the same spot above P2 (user: P2's Aegis had no "blocked" text)
    private static int movedLogs;
    private static void MoveToP2(ref Vector3 position, string what)
    {
        if (!P2GameHealth.Borrowing || !P2Movement.Active) return;
        var p1 = MyPlayer.Instance;
        if (p1 == null) return;
        Vector3 before = position;
        position = P2Movement.Pm.transform.position + (position - p1.transform.position);
        if (movedLogs++ < 6) ModLog.Info($"[popup] '{what}' made during P2's health code: moved from {before} to {position} (above P2)");
    }

    private static void Learn(bool asText, string text, Color c, Vector3 pos, int textSize)
    {
        if (!inP1Damage || learned) return;
        var p1 = MyPlayer.Instance;
        learned = true;
        useText = asText;
        color = c;
        size = textSize;
        textFormat = text;
        offset = p1 != null ? pos - p1.transform.position : Vector3.up * 2f;
        ModLog.Info($"[popup] P1's damage popup: {(asText ? "text" : "number")} '{text}', colour {c}, size {textSize}, offset {offset}");
    }

    // From P2Health when P2 takes damage
    public static void Show(int amount)
    {
        if (!learned || amount <= 0) return;
        var em = EffectManager.Instance;
        if (em == null || !P2Movement.Active) return;
        Vector3 pos = P2Movement.BodyPosition() + offset + (MyPlayer.Instance != null && MyPlayer.Instance.playerRenderer?.rendererObject != null
            ? MyPlayer.Instance.transform.position - MyPlayer.Instance.playerRenderer.rendererObject.transform.position : Vector3.zero);
        try
        {
            if (useText)
            {
                // P1's text with its number replaced by P2's
                var m = System.Text.RegularExpressions.Regex.Match(textFormat, @"\d+");
                string t = m.Success ? textFormat.Substring(0, m.Index) + amount + textFormat.Substring(m.Index + m.Length) : amount.ToString();
                em.PopupText(t, color, pos, size);
            }
            else em.PopupText((float)amount, color, pos, size);
        }
        catch (Exception e) { if (errors++ < 3) ModLog.Warn($"P2 popup failed: {e.Message}"); }
    }
    private static int errors;
}
