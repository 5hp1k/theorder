using Godot;

namespace MechSim;

/// <summary>
/// One thing the cockpit computer says (a child of <see cref="CockpitVoice"/> in hud.tscn, named for the
/// event it answers). The recordings are made by tools/audio/voice_lines.py.
/// </summary>
public partial class VoiceLine : AudioStreamPlayer
{
    /// <summary>Seconds before the same words may be said again.</summary>
    [Export] public float Repeat = 8f;
    /// <summary>Which line goes first when several are waiting; higher is more urgent.</summary>
    [Export] public int Priority = 1;

    /// <summary>When it was last said (<see cref="CockpitVoice"/>'s clock).</summary>
    public float SaidAt = -1000f;
}
