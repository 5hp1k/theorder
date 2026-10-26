using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The control modes (<see cref="GameConfig.ControlStyle"/>) where they touch the input map. In the
/// simulator the functions that have a switch or a button in the cab lose their keys — they are worked by
/// clicking them (key F frees the cursor in the cab; the clicks press the very same actions) — and the
/// view stays in the cockpit. In the other modes the keys are as the player set them.
/// </summary>
public static class ControlModes
{
    /// <summary>Functions with a control of their own in the cab.</summary>
    public static readonly string[] CockpitActions =
    {
        "toggle_lights", "counter_rotate", "kneel", "cab_light", "glass_heat", "night_vision", "thermal_vision", "engine", "reactor", "coolant",
        "target_nearest", "target_next", "target_friendly", "radar_mode", "full_stop",
    };

    // The keys taken away, to be given back.
    static readonly Dictionary<string, Godot.Collections.Array<InputEvent>> Taken = new();

    public static bool Simulator => GameConfig.Controls == GameConfig.ControlStyle.Simulator;
    public static bool Arcade => GameConfig.Controls == GameConfig.ControlStyle.Arcade;

    /// <summary>Puts the input map in step with the chosen mode.</summary>
    public static void Apply()
    {
        var actions = new List<string>(CockpitActions) { "toggle_view" };
        foreach (string action in actions)
        {
            if (!InputMap.HasAction(action)) continue;
            if (Simulator)
            {
                if (Taken.ContainsKey(action)) continue;
                Taken[action] = InputMap.ActionGetEvents(action);
                InputMap.ActionEraseEvents(action);
            }
            else if (Taken.Remove(action, out var events))
            {
                InputMap.ActionEraseEvents(action);
                foreach (var e in events) InputMap.ActionAddEvent(action, e);
            }
        }
    }
}
