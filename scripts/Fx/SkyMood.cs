using Godot;

namespace MechSim;

/// <summary>
/// A time of day (materials/sky/mood_*.tres): how the map's own sun and sky are changed when the player
/// asks for dawn, dusk or night instead of the hour the map was made for.
/// </summary>
[GlobalClass]
public partial class SkyMood : Resource
{
    /// <summary>Height of the sun (or moon) above the horizon, degrees; below zero = as the map has it.</summary>
    [Export] public float SunElevation = -1f;
    /// <summary>Colour of the sunlight; multiplied into the map's own.</summary>
    [Export] public Color SunTint = Colors.White;
    /// <summary>Strength of the sunlight, as a share of the map's own.</summary>
    [Export] public float SunEnergy = 1f;
    /// <summary>Colour cast over the whole sky and the light it gives.</summary>
    [Export] public Color SkyTint = Colors.White;
    /// <summary>Brightness of the sky and of the light it gives, as a share of the map's own.</summary>
    [Export] public float SkyEnergy = 1f;
    /// <summary>How much of the sky's own colour is left (0 = grey).</summary>
    [Export(PropertyHint.Range, "0,1")] public float SkySaturation = 1f;
    /// <summary>Dark enough that machines switch their lamps on.</summary>
    [Export] public bool Dark;
}
