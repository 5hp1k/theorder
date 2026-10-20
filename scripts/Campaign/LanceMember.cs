using Godot;

namespace MechSim;

/// <summary>One of the player's lancemates on a mission: the pilot and the machine they take out.</summary>
[GlobalClass]
public partial class LanceMember : Resource
{
    [Export] public PilotProfile Pilot;
    /// <summary>Catalog id of the machine (see scripts/Game/Catalog.cs), e.g. "hound".</summary>
    [Export] public string UnitId = "hound";
    /// <summary>Its weapons as the hangar writes them ("medium_laser:1|autocannon:2"); empty — as it comes from the factory.</summary>
    [Export] public string Loadout = "";
}
