using HarmonyLib;
using UnityEngine;

namespace MegabonkSplitscreen;

// TEMP diagnostic: a heartbeat every 2 s from the mod's per-frame update, so after a crash or freeze the log shows
// when the main thread stopped. No hooks (a hook wrapping game code that throws kills the process).
// Watchdog (v0.25.3, the game froze when P2 took control and a freeze leaves no dump): a background thread checks
// that the per-frame update still runs; after 15 s without it, Environment.FailFast ends the process so Windows writes
// a crash dump (WER LocalDumps -> H:\CrashDumps) whose main-thread stack shows where the game was stuck.
internal static class FreezeDiag
{
    private const int StuckMs = 15000;
    private static float nextBeat;
    private static long lastFrameTicks = Environment.TickCount64;
    private static bool watching;
    private static volatile bool sawFrame;

    public static void Apply(Harmony harmony)
    {
        ModLog.Info("FreezeDiag: heartbeat + 15 s freeze watchdog (TEMP)");
        if (watching) return;
        watching = true;
        var t = new System.Threading.Thread(Watch) { IsBackground = true, Name = "MegabonkSplitscreen watchdog" };
        t.Start();
    }

    private static volatile bool stopped;
    public static void Stop() => stopped = true;

    private static void Watch()
    {
        while (!stopped)
        {
            System.Threading.Thread.Sleep(1000);
            if (stopped) return;
            if (!sawFrame) continue;   // the game's start-up can take a while before the first frame
            long since = Environment.TickCount64 - System.Threading.Interlocked.Read(ref lastFrameTicks);
            if (since < StuckMs) continue;
            // Not through ModLog: its file lock could be held by the stuck main thread, and the dump must happen anyway
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(ModLog.Dir, "watchdog.txt"),
                    $"{DateTime.Now:HH:mm:ss} main thread stuck for {since / 1000} s -> ending the game for a crash dump{Environment.NewLine}");
            }
            catch { }
            Environment.FailFast("MegabonkSplitscreen watchdog: main thread stuck");
        }
    }

    // Called every frame by the Ticker
    public static void Update()
    {
        System.Threading.Interlocked.Exchange(ref lastFrameTicks, Environment.TickCount64);
        sawFrame = true;
        if (Time.unscaledTime < nextBeat) return;
        nextBeat = Time.unscaledTime + 2f;
        ModLog.Info($"[diag] heartbeat (timeScale {Time.timeScale})");
    }
}
