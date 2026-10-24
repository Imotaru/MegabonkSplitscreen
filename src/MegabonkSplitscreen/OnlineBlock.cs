using System.Reflection;
using Assets.Scripts.Steam;
using HarmonyLib;
using Steamworks;

namespace MegabonkSplitscreen;

// Stops modded runs from reaching Steam: no leaderboard scores, no stats, no achievements.
// Two layers:
//  1. The game's own upload functions (readable log of what the game tried to do).
//  2. Steamworks' SteamUserStats write calls, which every path has to go through in the end.
// Reading (downloading leaderboards, getting stats) is left alone.
// The game's mod check (Sus.CheckMods) is deliberately not touched.
internal static class OnlineBlock
{
    private static readonly (Type type, string[] methods)[] Targets =
    {
        (typeof(SteamLeaderboardsManagerNew), new[] { "QueueLeaderboardUpload", "UploadLeaderboardScore" }),
        (typeof(Leaderboards), new[] { "UploadScore" }),
        (typeof(SteamStatsManager), new[] { "QueueUpload", "TryUploadStats", "SetCachedStats" }),
        (typeof(SteamAchievementsManager), new[] { "TryUnlockAchievement", "QueueUpload", "TryUploadAchievements" }),
        (typeof(SteamUserStats), new[]
        {
            "SetStat", "UpdateAvgRateStat", "SetAchievement", "ClearAchievement", "IndicateAchievementProgress",
            "StoreStats", "ResetAllStats", "UploadLeaderboardScore", "AttachLeaderboardUGC",
        }),
    };

    private static readonly Dictionary<string, int> blockCounts = new();

    public static void Apply(Harmony harmony)
    {
        var blockBool = new HarmonyMethod(typeof(OnlineBlock), nameof(BlockBool));
        var blockAny = new HarmonyMethod(typeof(OnlineBlock), nameof(BlockAny));
        int patched = 0;

        foreach (var (type, names) in Targets)
        {
            foreach (var name in names)
            {
                var methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name).ToList();
                if (methods.Count == 0)
                {
                    ModLog.Warn($"OnlineBlock: {type.Name}.{name} not found");
                    continue;
                }
                foreach (var m in methods)
                {
                    try
                    {
                        harmony.Patch(m, prefix: m.ReturnType == typeof(bool) ? blockBool : blockAny);
                        patched++;
                    }
                    catch (Exception e)
                    {
                        ModLog.Error($"OnlineBlock: failed to patch {Describe(m)}: {e.Message}");
                    }
                }
            }
        }
        ModLog.Info($"OnlineBlock: {patched} methods blocked (leaderboards, stats, achievements)");
    }

    // For bool-returning Steam calls: report success so the game doesn't keep retrying.
    private static bool BlockBool(MethodBase __originalMethod, ref bool __result)
    {
        Count(__originalMethod);
        __result = true;
        return false;
    }

    // Everything else: skip the original; the return value is left at its default.
    private static bool BlockAny(MethodBase __originalMethod)
    {
        Count(__originalMethod);
        return false;
    }

    // Logs the 1st, 10th, 100th, ... block of each method so frequent calls don't flood the log.
    private static void Count(MethodBase m)
    {
        string key = m == null ? "?" : $"{m.DeclaringType?.Name}.{m.Name}";
        int n;
        lock (blockCounts)
        {
            blockCounts.TryGetValue(key, out n);
            blockCounts[key] = ++n;
        }
        if (n == 1 || n == 10 || n == 100 || n == 1000 || n % 10000 == 0)
            ModLog.Info($"Blocked {key} (x{n})");
    }

    private static string Describe(MethodInfo m) =>
        $"{m.DeclaringType?.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})";
}
