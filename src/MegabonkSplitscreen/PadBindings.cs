using Rewired;

namespace MegabonkSplitscreen;

// Which pad button P2 uses for one of the game's actions: the button P1's binding is on (the game's action names are
// MyInputManager.Jump / Slide / Interact / MapOverlay ...), matched on P2's pad by the button's name.
// User (1.0 alpha play): on P2's controller Y was the interact button although the prompt shows X - so the names each
// pad reports and P1's bindings are logged once ([pads]) to see what the pads really send.
internal static class PadBindings
{
    public static string[] ButtonFor(string action, string what, params string[] fallback)
    {
        LogPadsOnce();
        try
        {
            var maps = ReInput.players.GetPlayer(0).controllers.maps;
            var aem = string.IsNullOrEmpty(action) ? null : maps.GetFirstButtonMapWithAction(ControllerType.Joystick, action, true);
            if (aem != null && !string.IsNullOrEmpty(aem.elementIdentifierName))
            {
                ModLog.Info($"P2 {what} button = '{aem.elementIdentifierName}' (id {aem.elementIdentifierId}; P1's '{action}' binding)");
                return new[] { aem.elementIdentifierName.Trim().ToLowerInvariant() };
            }
        }
        catch (Exception e) { ModLog.Warn($"P2 {what} button lookup failed: {e.Message}"); }
        ModLog.Info($"P2 {what} button: P1's '{action}' binding not found, using {string.Join(" / ", fallback)}");
        return fallback;
    }

    // ---------- P2's own pad map (1.0 alpha: slide; the interact button) ----------
    // The game's map for P2's controller type (Rewired's saved-or-default map instance, as the game would give that pad
    // on player 0): the element an action is on - a button, or one side of an axis (triggers) - by its id. Read by
    // Rewired polling (direct reads missed P2's pad under Steam Input - PlayerJoin), matched by id, not by name.
    public sealed class Binding
    {
        public int Id = -1;
        public bool Axis;
        public Pole Pole = Pole.Positive;
        public string Name = "?";
        public bool Found => Id >= 0;
    }

    private static readonly Dictionary<string, Binding> found = new();

    public static Binding For(Joystick joy, string action)
    {
        if (joy == null || string.IsNullOrEmpty(action)) return new Binding();
        string key = joy.id + ":" + action;
        if (found.TryGetValue(key, out var b)) return b;
        b = new Binding();
        try
        {
            for (int cat = 0; cat < 8 && !b.Found; cat++)
            {
                ControllerMap map = null;
                try { map = ReInput.mapping.GetControllerMapInstanceSavedOrDefault(0, joy, cat, 0); } catch { }
                var aem = map?.GetFirstElementMapWithAction(action, true);
                if (aem == null) continue;
                b.Id = aem.elementIdentifierId;
                b.Axis = aem.elementType == ControllerElementType.Axis;
                b.Pole = aem.axisContribution;
                b.Name = aem.elementIdentifierName;
                ModLog.Info($"[pads] P2's '{action}' = '{b.Name}' (id {b.Id}, {(b.Axis ? "axis " + b.Pole : "button")}; map category {cat}) on '{joy.name}'");
            }
        }
        catch (Exception e) { ModLog.Warn($"[pads] P2's '{action}' lookup failed: {e.Message}"); }
        if (!b.Found) ModLog.Info($"[pads] P2's '{action}': not in the game's maps for '{joy.name}'");
        found[key] = b;
        return b;
    }

    // That element held on P2's pad right now (polled)
    public static bool Held(Joystick joy, Binding b)
    {
        if (joy == null || b == null || !b.Found) return false;
        var polling = ReInput.controllers.polling;
        if (!b.Axis)
        {
            var all = polling.PollControllerForAllButtons(ControllerType.Joystick, joy.id);
            if (all == null) return false;
            var e = all.GetEnumerator();
            var move = e.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (move.MoveNext()) { var bp = e.Current; if (bp.success && bp.elementIdentifierId == b.Id) return true; }
            return false;
        }
        var axes = polling.PollControllerForAllAxes(ControllerType.Joystick, joy.id);
        if (axes == null) return false;
        var ea = axes.GetEnumerator();
        var movea = ea.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (movea.MoveNext())
        {
            var ap = ea.Current;
            if (ap.success && ap.elementIdentifierId == b.Id && ap.axisPole == b.Pole) return true;   // polling only reports axes pushed past its threshold
        }
        return false;
    }

    // Every element the game's map for P2's pad binds to the action (user: slide is on B and the left bumper, jump on A
    // and the right bumper - For() only took the first)
    private static readonly Dictionary<string, List<Binding>> foundAll = new();
    public static List<Binding> AllFor(Joystick joy, string action)
    {
        var list = new List<Binding>();
        if (joy == null || string.IsNullOrEmpty(action)) return list;
        string key = joy.id + ":" + action;
        if (foundAll.TryGetValue(key, out var l)) return l;
        LogPadActionsOnce(joy);
        try
        {
            for (int cat = 0; cat < 8; cat++)
            {
                ControllerMap map = null;
                try { map = ReInput.mapping.GetControllerMapInstanceSavedOrDefault(0, joy, cat, 0); } catch { }
                if (map == null) continue;
                var results = new Il2CppSystem.Collections.Generic.List<ActionElementMap>();
                map.GetElementMapsWithAction(action, true, results);
                for (int i = 0; i < results.Count; i++)
                {
                    var aem = results[i];
                    if (aem == null || list.Any(b => b.Id == aem.elementIdentifierId && b.Axis == (aem.elementType == ControllerElementType.Axis))) continue;
                    list.Add(new Binding { Id = aem.elementIdentifierId, Axis = aem.elementType == ControllerElementType.Axis, Pole = aem.axisContribution, Name = aem.elementIdentifierName });
                }
            }
        }
        catch (Exception e) { ModLog.Warn($"[pads] P2's '{action}' lookup (all) failed: {e.Message}"); }
        if (list.Count == 0) { var first = For(joy, action); if (first.Found) list.Add(first); }
        ModLog.Info($"[pads] P2's '{action}' buttons: {(list.Count > 0 ? string.Join(", ", list.Select(b => $"'{b.Name}' (id {b.Id}{(b.Axis ? ", axis " + b.Pole : "")})")) : "none")} on '{joy.name}'");
        foundAll[key] = list;
        return list;
    }

    // [pads] once per pad: every game action and the elements bound to it on that pad (which button is what)
    private static readonly HashSet<int> actionsLogged = new();
    private static void LogPadActionsOnce(Joystick joy)
    {
        if (!actionsLogged.Add(joy.id)) return;
        var parts = new List<string>();
        foreach (var a in new[] { MyInputManager.Jump, MyInputManager.JumpBhop, MyInputManager.Slide, MyInputManager.Interact, MyInputManager.Aim,
                                  MyInputManager.MapOverlay, MyInputManager.Wallrun, MyInputManager.QuickReset, "Ability" })
        {
            if (string.IsNullOrEmpty(a)) continue;
            var names = new List<string>();
            for (int cat = 0; cat < 8; cat++)
            {
                try
                {
                    var map = ReInput.mapping.GetControllerMapInstanceSavedOrDefault(0, joy, cat, 0);
                    if (map == null) continue;
                    var results = new Il2CppSystem.Collections.Generic.List<ActionElementMap>();
                    map.GetElementMapsWithAction(a, true, results);
                    for (int i = 0; i < results.Count; i++) if (results[i] != null) names.Add($"'{results[i].elementIdentifierName}'{(cat > 0 ? $" (cat {cat})" : "")}");
                }
                catch { }
            }
            parts.Add($"{a} = {(names.Count > 0 ? string.Join(" + ", names.Distinct()) : "-")}");
        }
        ModLog.Info($"[pads] game actions on P2's '{joy.name}': {string.Join("; ", parts)}");
    }

    // Any of them held (one poll of the pad's buttons, one of its axes)
    public static bool HeldAny(Joystick joy, List<Binding> bs)
    {
        if (joy == null || bs == null || bs.Count == 0) return false;
        var polling = ReInput.controllers.polling;
        if (bs.Any(b => !b.Axis))
        {
            var all = polling.PollControllerForAllButtons(ControllerType.Joystick, joy.id);
            if (all != null)
            {
                var e = all.GetEnumerator();
                var move = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (move.MoveNext()) { var bp = e.Current; if (bp.success && bs.Any(b => !b.Axis && b.Id == bp.elementIdentifierId)) return true; }
            }
        }
        if (bs.Any(b => b.Axis))
        {
            var axes = polling.PollControllerForAllAxes(ControllerType.Joystick, joy.id);
            if (axes != null)
            {
                var ea = axes.GetEnumerator();
                var movea = ea.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (movea.MoveNext()) { var ap = ea.Current; if (ap.success && bs.Any(b => b.Axis && b.Id == ap.elementIdentifierId && b.Pole == ap.axisPole)) return true; }
            }
        }
        return false;
    }

    public static void Reset() { found.Clear(); foundAll.Clear(); }

    // TEMP [pads]: every pad's buttons (index, name) and P1's joystick bindings for the game's actions
    private static bool logged;
    public static void LogPadsOnce()
    {
        if (logged || !ReInput.isReady) return;
        logged = true;
        try
        {
            foreach (var joy in ReInput.controllers.GetJoysticks())
            {
                if (joy == null) continue;
                var names = new List<string>();
                for (int i = 0; i < joy.buttonCount; i++)
                {
                    string n = "?";
                    try { n = joy.ButtonElementIdentifiers[i]?.name ?? "?"; } catch { }
                    names.Add($"{i} '{n}'");
                }
                ModLog.Info($"[pads] #{joy.id} '{joy.name}' ({joy.hardwareName}): buttons {string.Join(", ", names)}");
            }
            var maps = ReInput.players.GetPlayer(0).controllers.maps;
            var acts = new List<string>();
            foreach (var a in new[] { MyInputManager.Jump, MyInputManager.Slide, MyInputManager.Interact, MyInputManager.MapOverlay, MyInputManager.Wallrun, MyInputManager.Aim })
            {
                if (string.IsNullOrEmpty(a)) continue;
                var aem = maps.GetFirstButtonMapWithAction(ControllerType.Joystick, a, true);
                acts.Add($"{a} -> {(aem != null ? $"'{aem.elementIdentifierName}' (id {aem.elementIdentifierId}, axis range {aem.axisRange})" : "none")}");
            }
            ModLog.Info($"[pads] P1's pad bindings: {string.Join("; ", acts)}");
        }
        catch (Exception e) { ModLog.Warn($"[pads] listing failed: {e.Message}"); }
    }
}
