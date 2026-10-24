using System.Reflection;
using System.Text;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;

namespace MegabonkSplitscreen;

// TEMP diagnostic (user's request, crash hunt): who listens to the game's global events.
// The game announces things through static delegate fields ("A_WeaponAdded", "A_StatUpdate", ...). This takes a
// snapshot of every such event in Assembly-CSharp and lists each listener: the method and the object it belongs to,
// labelled P1 / P2 / other. Read-only, no hooks (firing can't be logged without hooking the code that fires).
// Snapshots: when P2 spawns, when a P1 level-up/chest window opens (the crash moment), after P2's first pick.
// Full lists -> events_dump.txt (appended), summary of P2-owned listeners -> the log.
internal static class EventDump
{
    private static List<PropertyInfo> events;
    private static string dir;
    private static int snapshots;

    public static void Init(string pluginDir) => dir = pluginDir;

    private static void FindEvents()
    {
        events = new List<PropertyInfo>();
        Type[] types;
        try { types = typeof(PlayerInventory).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        foreach (var t in types)
        {
            PropertyInfo[] props;
            try { props = t.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly); }
            catch { continue; }
            foreach (var p in props)
            {
                if (!typeof(Il2CppSystem.Delegate).IsAssignableFrom(p.PropertyType) || p.GetGetMethod() == null) continue;
                events.Add(p);
            }
        }
        ModLog.Info($"[events] {events.Count} global events found");
    }

    public static void Snapshot(string when)
    {
        if (!ModLog.Debug) return;   // dump files only with debug logging (1.0 alpha)
        if (snapshots >= 12) return;   // keep the file small
        snapshots++;
        try
        {
            if (events == null) FindEvents();
            var sb = new StringBuilder();
            sb.AppendLine($"===== {DateTime.Now:HH:mm:ss.fff} {when} =====");
            var p2Lines = new List<string>();
            int withListeners = 0, total = 0;
            foreach (var p in events)
            {
                Il2CppSystem.Delegate d;
                try { d = p.GetValue(null) as Il2CppSystem.Delegate; }
                catch { continue; }
                if (d == null) continue;
                var list = d.GetInvocationList();
                if (list == null || list.Length == 0) continue;
                withListeners++;
                string evName = $"{p.DeclaringType?.Name}.{p.Name}";
                sb.AppendLine($"{evName} ({list.Length})");
                foreach (var l in list)
                {
                    total++;
                    string line = Describe(l, out bool p2);
                    sb.AppendLine("    " + line);
                    if (p2) p2Lines.Add($"{evName} -> {line}");
                }
            }
            File.AppendAllText(Path.Combine(dir, "events_dump.txt"), sb.ToString());
            ModLog.Info($"[events] {when}: {withListeners} events with {total} listeners; P2-owned listeners: " +
                        (p2Lines.Count == 0 ? "none" : string.Join(" | ", p2Lines)));
        }
        catch (Exception e)
        {
            ModLog.Error($"[events] snapshot '{when}' failed: {e}");
        }
    }

    private static string Describe(Il2CppSystem.Delegate l, out bool p2)
    {
        p2 = false;
        string method = "?";
        try { method = l.Method != null ? $"{l.Method.DeclaringType?.Name}.{l.Method.Name}" : "?"; }
        catch { /* reflection data may be stripped */ }
        var target = l.Target;
        if (target == null) return $"{method} (static)";
        string owner = Owner(target, out p2);
        string type = "?";
        try { type = target.GetIl2CppType().Name; } catch { }
        return $"{method} on {type} [{owner}]";
    }

    // P1 / P2 / other, for the objects that can belong to a player
    private static string Owner(Il2CppSystem.Object o, out bool p2)
    {
        p2 = false;
        var w = o.TryCast<WeaponBase>();
        if (w != null)
        {
            p2 = P2Weapons.IsP2(w);
            return $"{(p2 ? "P2" : "P1")} weapon {w.weaponData?.eWeapon}";
        }
        var wi = o.TryCast<WeaponInventory>();
        if (wi != null)
        {
            p2 = P2Inventory.Weapons != null && wi.Pointer == P2Inventory.Weapons.Pointer;
            return p2 ? "P2 weapon holder" : "P1 weapon inventory";
        }
        var ti = o.TryCast<TomeInventory>();
        if (ti != null)
        {
            p2 = P2Inventory.TomesInv != null && ti.Pointer == P2Inventory.TomesInv.Pointer;
            return p2 ? "P2 tome holder" : "P1 tome inventory";
        }
        var mb = o.TryCast<UnityEngine.MonoBehaviour>();
        if (mb != null) return $"object '{mb.name}'";
        return "other";
    }
}
