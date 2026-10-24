using Rewired;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MegabonkSplitscreen;

// Pausing for both players (user's request).
// In the game, PauseUi (Pause/Resume/Toggle/CanPause/IsPaused) reacts to the pause button of Rewired player 0, the
// game's only player; P2's pad isn't on it, so P2's Start did nothing in a run.
//  - P2 presses Start in a run (pausing allowed, no level-up screen): the mod calls the game's PauseUi.Pause().
//    Once P2 lets go of Start, P2 gets the menu: P1's controllers/keyboard/mouse come off Rewired player 0 and P2's pad
//    goes on (the same handover as P2's level-up screen). P2 navigates and can resume with Start; when the pause ends
//    everything goes back to P1. (Handing over only after the release keeps the same press from resuming at once.)
//  - P1 pauses as before (P1 keeps control).
//  - While paused, a banner at the top centre says "P1 PAUSE" (blue) or "P2 PAUSE" (red), on its own top canvas.
internal static class P2Pause
{
    private enum Who { None, P1, P2 }

    private static readonly Color P1Color = new(0.25f, 0.6f, 1f, 1f);
    private static readonly Color P2Color = new(1f, 0.25f, 0.25f, 1f);

    private static Who pausedBy = Who.None;
    private static bool handedOver, startWasDown, waitRelease;
    private static PauseUi pauseUi;
    private static float nextFind;
    private static GameObject bannerRoot;
    private static TextMeshProUGUI banner;

    // Called every frame (from P2Body.Update), also while paused
    public static void Update()
    {
        if (PlayerJoin.P2Joystick == null) return;
        if (pauseUi == null && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 1f;
            // Via PauseHandler (always active, on the HUD canvas): the pause menu itself is inactive until someone
            // pauses, and FindObjectOfType skips inactive objects - so P2 could only pause after P1 had (v0.22.1)
            var handler = Object.FindObjectOfType<PauseHandler>();
            pauseUi = handler != null ? handler.pauseUi : null;
            if (pauseUi == null) pauseUi = Object.FindObjectOfType<PauseUi>();
        }
        if (pauseUi == null)
        {
            // Quit to the main menu from a pause: the pause menu is gone, so end our pause here (banner off,
            // controls back to P1). User: the "P2 PAUSE" label stayed on the main menu.
            if (pausedBy != Who.None) EndPause();
            return;
        }

        bool startDown = P2StartHeld();
        bool pressed = startDown && !startWasDown;
        startWasDown = startDown;
        bool paused = pauseUi.IsPaused();

        if (!paused)
        {
            if (pausedBy != Who.None) EndPause();
            if (pressed && CanP2Pause())
            {
                pauseUi.Pause();
                if (pauseUi.IsPaused())
                {
                    pausedBy = Who.P2;
                    waitRelease = true;
                    ShowBanner();
                    ModLog.Info("P2 paused the game");
                }
            }
            return;
        }

        // Paused
        if (pausedBy == Who.None)
        {
            pausedBy = Who.P1;   // paused by the game's own input = P1
            ShowBanner();
            ModLog.Info("P1 paused the game");
        }
        if (pausedBy == Who.P2 && waitRelease && !startDown)
        {
            waitRelease = false;
            P2LevelUp.GiveControlsToP2();
            handedOver = true;
        }
    }

    private static bool CanP2Pause()
    {
        if (LevelupScreen.isLevelingUp || P2LevelUp.P2ScreenOpen) return false;
        try { return pauseUi.CanPause(); }
        catch { return false; }
    }

    private static void EndPause()
    {
        if (handedOver) P2LevelUp.RestoreControls();
        ModLog.Info($"{pausedBy} pause ended{(handedOver ? ", controls back to P1" : "")}");
        handedOver = false;
        waitRelease = false;
        pausedBy = Who.None;
        if (bannerRoot != null) bannerRoot.SetActive(false);
    }

    private static bool P2StartHeld()
    {
        var joy = PlayerJoin.P2Joystick;
        var bp = ReInput.controllers.polling.PollControllerForFirstButton(ControllerType.Joystick, joy.id);
        if (!bp.success) return false;
        string n = (bp.elementIdentifierName ?? "").Trim().ToLowerInvariant();
        return n == "start" || n == "menu" || n == "options" || bp.elementIndex == 7;
    }

    // ---------- Banner ----------

    private static void ShowBanner()
    {
        if (bannerRoot == null) MakeBanner();
        if (banner == null) return;
        banner.text = pausedBy == Who.P2 ? "P2 PAUSE" : "P1 PAUSE";
        banner.color = pausedBy == Who.P2 ? P2Color : P1Color;
        bannerRoot.SetActive(true);
    }

    private static void MakeBanner()
    {
        try
        {
            bannerRoot = new GameObject("MegabonkSplitscreen_PauseBanner");
            Object.DontDestroyOnLoad(bannerRoot);
            var canvas = bannerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // over the pause menu
            var scaler = bannerRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(bannerRoot.transform, false);
            banner = textGo.AddComponent<TextMeshProUGUI>();
            var any = Object.FindObjectOfType<TextMeshProUGUI>();   // the game's font
            if (any != null && any.font != null) banner.font = any.font;
            banner.fontSize = 64;
            banner.alignment = TextAlignmentOptions.Center;
            banner.raycastTarget = false;
            var rt = banner.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -120f);
            rt.sizeDelta = new Vector2(800f, 100f);
            bannerRoot.SetActive(false);
        }
        catch (Exception e)
        {
            ModLog.Error($"Pause banner failed: {e}");
        }
    }
}
