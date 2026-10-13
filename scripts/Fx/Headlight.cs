using Godot;

namespace MechSim;

/// <summary>
/// A lamp on a machine (scenes/fx/headlight.tscn): the "Beam" spotlight and a "Lens" that glows while it
/// is lit. Off until switched on — by the player with L, by computer pilots when it gets dark.
/// </summary>
public partial class Headlight : Node3D
{
    Light3D _beam;
    GeometryInstance3D _lens;

    public bool On { get; private set; }

    public override void _Ready()
    {
        _beam = GetNodeOrNull<Light3D>("Beam");
        _lens = GetNodeOrNull<GeometryInstance3D>("Lens");
        Switch(On);
    }

    public void Switch(bool on)
    {
        On = on;
        if (_beam != null) _beam.Visible = on;
        if (_lens != null) _lens.Visible = on;
    }

    /// <summary>Switches every lamp of a machine.</summary>
    public static void Set(Node unit, bool on)
    {
        foreach (var lamp in Geo.Collect<Headlight>(unit)) lamp.Switch(on);
    }

    /// <summary>Flips the lamps of a machine; returns whether they are now lit.</summary>
    public static bool Toggle(Node unit)
    {
        bool on = false;
        var lamps = Geo.Collect<Headlight>(unit);
        foreach (var lamp in lamps) on |= lamp.On;
        foreach (var lamp in lamps) lamp.Switch(!on);
        return !on && lamps.Count > 0;
    }
}
