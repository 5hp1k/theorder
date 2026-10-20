using Godot;

namespace MechSim;

/// <summary>
/// A machine of a mission: where it stands (this marker; it is put on the ground) and which way it
/// faces, what it is, whose, who pilots it and how it behaves. It takes the field when the mission
/// starts, or later when a step names it (reinforcements, an ambush). Groups name several machines at
/// once for objectives and triggers.
/// </summary>
[Tool]
public partial class MissionUnit : Marker3D, IStep
{
    /// <summary>Catalog id of the machine (scripts/Game/Catalog.cs).</summary>
    [Export] public string UnitId = "hound";
    [Export] public Team Team = Team.Enemy;
    /// <summary>What the HUD calls it; empty — the pilot's callsign, or the machine's name.</summary>
    [Export] public string DisplayName = "";
    /// <summary>A named pilot and their skills; none — an ordinary one.</summary>
    [Export] public PilotProfile Pilot;
    /// <summary>Its weapons as the hangar writes them ("medium_laser:1|autocannon:2"); empty — as it comes from the factory.</summary>
    [Export] public string Loadout = "";
    [Export] public string Group = "";
    /// <summary>On the field from the start; otherwise it comes when a step names it.</summary>
    [Export] public bool SpawnAtStart = true;
    /// <summary>How far it wanders about its post, metres; 0 — it stays put.</summary>
    [Export] public float PatrolRadius = 70f;
    /// <summary>It knows where the player is when it takes the field and goes for him.</summary>
    [Export] public bool HuntPlayer;
    /// <summary>Its paint (PaintJob.Pack()); empty — its side's usual paint.</summary>
    [Export] public float[] Paint = System.Array.Empty<float>();

    /// <summary>The machine on the field, once it is there.</summary>
    public Node3D Machine { get; private set; }
    public bool Spawned => Machine != null;
    public bool Destroyed => Spawned && (!IsInstanceValid(Machine) || Machine is ITarget { IsDestroyed: true });

    public void Run() => Spawn();

    public void Spawn()
    {
        if (Spawned || Engine.IsEditorHint() || Game.Instance == null) return;
        Machine = Game.Instance.SpawnStoryUnit(this);
    }

    // The machine to choose from a list in the inspector.
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if ((string)property["name"] != nameof(UnitId)) return;
        var ids = new System.Collections.Generic.List<string>();
        foreach (var e in Catalog.PlayerUnits) ids.Add(e.Id);
        property["hint"] = (int)PropertyHint.Enum;
        property["hint_string"] = string.Join(",", ids);
    }
}
