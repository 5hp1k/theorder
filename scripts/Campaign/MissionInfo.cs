using Godot;

namespace MechSim;

/// <summary>
/// A mission of the campaign (campaign/missions/*.tres): what the briefing says, the scene that is
/// played (a <see cref="Mission"/> with its map, machines, objectives, triggers and cutscenes), who
/// goes out with the player, and what the player is given for it.
/// </summary>
[GlobalClass]
public partial class MissionInfo : Resource
{
    /// <summary>What the campaign save keeps.</summary>
    [Export] public string Id = "";
    [Export] public string Title = "";
    /// <summary>Where and when, under the title of the briefing.</summary>
    [Export] public string Location = "";
    [Export(PropertyHint.MultilineText)] public string Briefing = "";
    [Export] public Texture2D BriefingImage;
    /// <summary>The objectives as the briefing lists them (the scene's own are what count in battle).</summary>
    [Export] public string[] Objectives = System.Array.Empty<string>();
    [Export(PropertyHint.File, "*.tscn")] public string Scene = "";

    [ExportGroup("Conditions")]
    /// <summary>Hour of the day (see MapLighting.TimeIds; 0 — the map's own) and weather (Weather.Scenes; 0 — the map's own).</summary>
    [Export] public int TimeOfDay;
    [Export] public int Weather;
    /// <summary>Minutes to do it in; 0 — no limit.</summary>
    [Export] public float TimeLimit;
    /// <summary>Whether the enemy's bombers may drop nuclear and pulse bombs.</summary>
    [Export] public bool EnemyNukes, EnemyEmp;

    [ExportGroup("Lance")]
    /// <summary>The machine the story puts the player in; empty — the player's choice from the hangar.</summary>
    [Export] public string PlayerUnit = "";
    [Export] public LanceMember[] Lance = System.Array.Empty<LanceMember>();

    [ExportGroup("Rewards")]
    /// <summary>Machines (catalog ids) and weapons added to the player's hangar, pilots to the roster.</summary>
    [Export] public string[] GrantUnits = System.Array.Empty<string>();
    [Export] public string[] GrantWeapons = System.Array.Empty<string>();
    [Export] public PilotProfile[] GrantPilots = System.Array.Empty<PilotProfile>();
    /// <summary>Said in the debriefing.</summary>
    [Export(PropertyHint.MultilineText)] public string Debriefing = "";
}
