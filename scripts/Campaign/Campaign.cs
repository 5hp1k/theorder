using Godot;

namespace MechSim;

/// <summary>
/// The story (campaign/campaign.tres): its missions in the order they are played, and what the player
/// starts with — machines and weapons in the hangar, pilots in the roster. Missions give more.
/// </summary>
[GlobalClass]
public partial class Campaign : Resource
{
    [Export] public string Title = "";
    [Export(PropertyHint.MultilineText)] public string Description = "";
    /// <summary>The player's own pilot.</summary>
    [Export] public PilotProfile Hero;
    [Export] public MissionInfo[] Missions = System.Array.Empty<MissionInfo>();

    [ExportGroup("At the start")]
    [Export] public string[] StartUnits = System.Array.Empty<string>();
    [Export] public string[] StartWeapons = System.Array.Empty<string>();
    [Export] public PilotProfile[] StartPilots = System.Array.Empty<PilotProfile>();

    public int IndexOf(MissionInfo mission) => System.Array.IndexOf(Missions, mission);
}
