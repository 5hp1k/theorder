using Godot;

namespace MechSim;

/// <summary>
/// A named pilot (campaign/pilots/*.tres): who they are, what the radio and the HUD call them, and how
/// good they are at the four things a computer pilot does. The skills are 0–100, 50 being the pilot the
/// machine's own ai_pilot.tscn makes; <see cref="ApplyTo"/> turns them into that pilot's numbers.
/// </summary>
[GlobalClass]
public partial class PilotProfile : Resource
{
    /// <summary>What the campaign save keeps.</summary>
    [Export] public string Id = "";
    [Export] public string Name = "";
    /// <summary>What the radio, the HUD and the squad list call them; the name when empty.</summary>
    [Export] public string Callsign = "";
    [Export] public Texture2D Portrait;
    [Export(PropertyHint.MultilineText)] public string Bio = "";

    [ExportGroup("Skills (0–100)")]
    /// <summary>Gunnery: how steady the aim is and how fast a missile lock comes.</summary>
    [Export(PropertyHint.Range, "0,100")] public int Gunnery = 50;
    /// <summary>Piloting: how near the heat limit they dare to run the machine.</summary>
    [Export(PropertyHint.Range, "0,100")] public int Piloting = 50;
    /// <summary>Awareness: how far their sensors reach and how long they keep after a target lost from sight.</summary>
    [Export(PropertyHint.Range, "0,100")] public int Awareness = 50;
    /// <summary>Aggression: how close they like to fight, and how seldom they stop to shoot from a knee.</summary>
    [Export(PropertyHint.Range, "0,100")] public int Aggression = 50;

    public string Label => string.IsNullOrEmpty(Callsign) ? Name : Callsign;

    public static readonly string[] SkillNames = { "Стрельба", "Пилотирование", "Внимание", "Напор" };
    public int[] Skills => new[] { Gunnery, Piloting, Awareness, Aggression };

    /// <summary>Puts the pilot's skills on a computer pilot (before it is added to its machine).</summary>
    public void ApplyTo(AiPilot pilot)
    {
        float g = Gunnery / 100f, p = Piloting / 100f, a = Awareness / 100f, r = Aggression / 100f;
        pilot.AimSpread *= Mathf.Lerp(1.7f, 0.4f, g);
        pilot.LockTime *= Mathf.Lerp(1.4f, 0.6f, g);
        pilot.CautiousHeat = Mathf.Lerp(0.45f, 0.82f, p);
        pilot.SensorRange *= Mathf.Lerp(0.75f, 1.35f, a);
        pilot.MemoryTime *= Mathf.Lerp(0.6f, 1.6f, a);
        pilot.PreferredRange *= Mathf.Lerp(1.5f, 0.6f, r);
        // The bold ones keep moving; the careful ones go down on a knee and pick their shots.
        pilot.KneelShare *= Mathf.Lerp(1.4f, 0.5f, r);
    }
}
