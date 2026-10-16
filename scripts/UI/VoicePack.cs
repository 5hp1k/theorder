using Godot;

namespace MechSim;

/// <summary>
/// One voice for the cockpit computer (audio/voice/&lt;id&gt;/pack.tres): its recordings, by the name of the
/// <see cref="VoiceLine"/> each one is for. A line it has no recording of is simply not said.
/// </summary>
[GlobalClass]
public partial class VoicePack : Resource
{
    /// <summary>What the settings keep (GameConfig.Voice).</summary>
    [Export] public string Id = "";
    /// <summary>What the settings show.</summary>
    [Export] public string Title = "";
    [Export] public Godot.Collections.Dictionary<string, AudioStream> Lines = new();
}
