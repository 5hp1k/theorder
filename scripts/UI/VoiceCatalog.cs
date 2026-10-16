using Godot;

namespace MechSim;

/// <summary>
/// Every voice the cockpit computer can speak with (audio/voice/voices.tres), in the order the settings
/// list them; the first is the default. Used by <see cref="CockpitVoice"/> and the settings' sound tab.
/// </summary>
[GlobalClass]
public partial class VoiceCatalog : Resource
{
    [Export] public VoicePack[] Packs = System.Array.Empty<VoicePack>();

    /// <summary>The pack with the id, or null (none, or the voice switched off).</summary>
    public VoicePack Find(string id)
    {
        foreach (var pack in Packs)
            if (pack != null && pack.Id == id) return pack;
        return null;
    }
}
