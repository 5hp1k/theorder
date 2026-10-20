using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MechSim;

/// <summary>
/// A mission made in the mission editor, as plain data: what is saved in user://missions/, exported and
/// imported (one JSON file, its characters' portraits inside it). <see cref="MissionBuilder"/> turns it
/// into the same scene a campaign mission is (<see cref="Mission"/> with its machines, objectives,
/// triggers, radio lines, cutscenes and endings), so it plays exactly like one.
///
/// Every element of a mission has a <c>Name</c>, unique within it: that is how objectives, triggers and
/// cutscenes name the steps they set going and the machines they are about. Positions are on the map's
/// ground (X, Z, metres); Yaw in radians, 0 facing north (-Z).
/// </summary>
public sealed class MissionDoc
{
    /// <summary>Of the file format: a later game reads older files.</summary>
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Новая миссия";
    public string Author { get; set; } = "";
    /// <summary>One of <see cref="MissionLibrary.Types"/> (what it is about; the editor fills objectives in for it).</summary>
    public string Type { get; set; } = "free";
    public string Location { get; set; } = "";
    public string Briefing { get; set; } = "";
    public string Debriefing { get; set; } = "";
    /// <summary>Catalog id of the map (Catalog.Maps).</summary>
    public string Map { get; set; } = "desert";
    /// <summary>MapLighting.TimeIds / Weather.Scenes; 0 — the map's own.</summary>
    public int TimeOfDay { get; set; }
    public int Weather { get; set; }
    /// <summary>Minutes to do it in; 0 — no limit.</summary>
    public float TimeLimit { get; set; }
    public bool EnemyNukes { get; set; }
    public bool EnemyEmp { get; set; }

    public PlayerDoc Player { get; set; } = new();
    public List<LanceDoc> Lance { get; set; } = new();
    public List<PilotDoc> Pilots { get; set; } = new();
    public List<UnitDoc> Units { get; set; } = new();
    /// <summary>Things put on the map besides its own: cover, buildings, vehicles, rocks.</summary>
    public List<PropDoc> Props { get; set; } = new();
    public List<ObjectiveDoc> Objectives { get; set; } = new();
    public List<TriggerDoc> Triggers { get; set; } = new();
    public List<RadioDoc> Radio { get; set; } = new();
    public List<CutsceneDoc> Cutscenes { get; set; } = new();
    public List<EndingDoc> Endings { get; set; } = new();
    /// <summary>Steps run when the mission is won.</summary>
    public List<string> OnWon { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>The mission in a file's text; null if it is not one.</summary>
    public static MissionDoc FromJson(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<MissionDoc>(json, Options);
            return doc?.Normalised();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public MissionDoc Copy() => FromJson(ToJson());

    /// <summary>Lists never null, numbers in range: a file from elsewhere may have anything in it.</summary>
    MissionDoc Normalised()
    {
        Player ??= new PlayerDoc();
        Lance ??= new(); Pilots ??= new(); Units ??= new(); Props ??= new(); Objectives ??= new(); Triggers ??= new();
        Radio ??= new(); Cutscenes ??= new(); Endings ??= new(); OnWon ??= new();
        Title ??= ""; Id ??= ""; Type ??= "free"; Map ??= "desert";
        foreach (var u in Units) { u.Loadout ??= System.Array.Empty<string>(); u.Paint ??= new PaintDoc(); }
        foreach (var o in Objectives) { o.Units ??= new(); o.OnDone ??= new(); o.OnFailed ??= new(); }
        foreach (var t in Triggers) { t.Units ??= new(); t.Steps ??= new(); }
        foreach (var r in Radio) r.Lines ??= new();
        foreach (var c in Cutscenes) { c.Shots ??= new(); c.Lines ??= new(); c.OnFinished ??= new(); }
        foreach (var l in Lance) l.Loadout ??= System.Array.Empty<string>();
        Player.Loadout ??= System.Array.Empty<string>();
        return this;
    }

    /// <summary>Every named element, for lists of steps and for keeping names unique.</summary>
    public IEnumerable<(string name, string kind)> Elements()
    {
        foreach (var u in Units) yield return (u.Name, "unit");
        foreach (var o in Objectives) yield return (o.Name, "objective");
        foreach (var t in Triggers) yield return (t.Name, "trigger");
        foreach (var r in Radio) yield return (r.Name, "radio");
        foreach (var c in Cutscenes) yield return (c.Name, "cutscene");
        foreach (var e in Endings) yield return (e.Name, "ending");
    }

    /// <summary>A name not yet taken: the stem with the first free number after it.</summary>
    public string FreeName(string stem)
    {
        var taken = new HashSet<string>();
        foreach (var (name, _) in Elements()) taken.Add(name);
        for (int i = 1; ; i++)
            if (!taken.Contains(stem + i)) return stem + i;
    }

    public PilotDoc Pilot(string id) => string.IsNullOrEmpty(id) ? null : Pilots.Find(p => p.Id == id);
}

/// <summary>Where the player starts, and in what (empty: the machine chosen in the hangar, as fitted there).</summary>
public sealed class PlayerDoc
{
    public string UnitId { get; set; } = "";
    public string[] Loadout { get; set; } = System.Array.Empty<string>();
    public float X { get; set; }
    public float Z { get; set; } = 60f;
    public float Yaw { get; set; }
}

/// <summary>One of the player's lance: a pilot (one of the mission's characters, or none) in a machine.</summary>
public sealed class LanceDoc
{
    public string PilotId { get; set; } = "";
    public string UnitId { get; set; } = "hound";
    public string[] Loadout { get; set; } = System.Array.Empty<string>();
}

/// <summary>A named character: a pilot of a machine, a voice on the radio, a face in a cutscene.</summary>
public sealed class PilotDoc
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string Bio { get; set; } = "";
    /// <summary>Gunnery, piloting, awareness, aggression, 0–100 (see PilotProfile).</summary>
    public int[] Skills { get; set; } = { 50, 50, 50, 50 };
    /// <summary>A PNG picture, base64 (empty: none).</summary>
    public string Portrait { get; set; } = "";
    public string Label => string.IsNullOrEmpty(Callsign) ? Name : Callsign;
}

/// <summary>How a machine is painted (PaintJob's numbers); Preset ≥ 0 — one of PaintJob.Presets, as it is.</summary>
public sealed class PaintDoc
{
    /// <summary>-1: the side's usual paint (the player's from the hangar, the enemy's camouflage).</summary>
    public int Preset { get; set; } = -1;
    /// <summary>PaintJob.Pack(), used when Preset is -2 (own colours).</summary>
    public float[] Custom { get; set; } = System.Array.Empty<float>();
}

public sealed class UnitDoc
{
    public string Name { get; set; } = "";
    public string UnitId { get; set; } = "hound";
    /// <summary>"player" (the player's side, not in the lance) or "enemy".</summary>
    public string Team { get; set; } = "enemy";
    public string DisplayName { get; set; } = "";
    public string PilotId { get; set; } = "";
    /// <summary>Per hardpoint, as the hangar writes them; empty — as it comes from the factory.</summary>
    public string[] Loadout { get; set; } = System.Array.Empty<string>();
    public PaintDoc Paint { get; set; } = new();
    public string Group { get; set; } = "";
    public bool SpawnAtStart { get; set; } = true;
    public float Patrol { get; set; } = 70f;
    public bool HuntPlayer { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
}

/// <summary>A thing from Catalog.Props, standing on the ground where it was put.</summary>
public sealed class PropDoc
{
    public string Kind { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
}

/// <summary>destroy (machines), reach (a place), protect (machines), survive (a time).</summary>
public sealed class ObjectiveDoc
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "destroy";
    public string Text { get; set; } = "";
    public bool Required { get; set; } = true;
    public bool StartActive { get; set; } = true;
    public string Group { get; set; } = "";
    public List<string> Units { get; set; } = new();
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 40f;
    public int MayLose { get; set; }
    public float Seconds { get; set; } = 120f;
    public List<string> OnDone { get; set; } = new();
    public List<string> OnFailed { get; set; } = new();
}

/// <summary>area (someone comes within the radius), timer (seconds after it is armed), destroyed (machines down).</summary>
public sealed class TriggerDoc
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "area";
    public bool StartArmed { get; set; } = true;
    public bool Once { get; set; } = true;
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 60f;
    /// <summary>For an area: "player", "side" (anyone of the player's side), "enemy".</summary>
    public string Who { get; set; } = "player";
    public float Delay { get; set; } = 10f;
    public string Group { get; set; } = "";
    public List<string> Units { get; set; } = new();
    public int Count { get; set; }
    public List<string> Steps { get; set; } = new();
}

public sealed class LineDoc
{
    /// <summary>A character's id, or empty and then SpeakerName.</summary>
    public string Speaker { get; set; } = "";
    public string SpeakerName { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Seconds on screen; 0 — as long as it takes to read.</summary>
    public float Hold { get; set; }
}

public sealed class RadioDoc
{
    public string Name { get; set; } = "";
    public List<LineDoc> Lines { get; set; } = new();
}

/// <summary>One position of a cutscene's camera, reached Time seconds after the one before.</summary>
public sealed class ShotDoc
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    /// <summary>Radians.</summary>
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Fov { get; set; } = 60f;
    public float Time { get; set; } = 3f;
}

public sealed class CutsceneDoc
{
    public string Name { get; set; } = "";
    public List<ShotDoc> Shots { get; set; } = new();
    public List<LineDoc> Lines { get; set; } = new();
    public bool Skippable { get; set; } = true;
    public bool HoldBattle { get; set; } = true;
    /// <summary>Played as the mission starts (as well as when a step names it).</summary>
    public bool AtStart { get; set; }
    public List<string> OnFinished { get; set; } = new();
}

public sealed class EndingDoc
{
    public string Name { get; set; } = "";
    public bool Success { get; set; } = true;
    public string Reason { get; set; } = "";
}
