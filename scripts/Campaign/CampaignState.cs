using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// How far the player has got in the campaign, kept in user://campaign.cfg: how many missions are
/// done (the story is linear: the next is the first not done), what the hangar holds — the machines and
/// weapons given so far — the pilots in the roster, the machine the player goes out in and what each
/// machine carries. There is no money: everything comes from the story (<see cref="MissionInfo"/>'s
/// rewards, <see cref="Campaign"/>'s start). Weapons that come fitted to a machine count as the player's
/// too, so stripping one off and putting it back is always possible.
/// </summary>
public static class CampaignState
{
    const string FilePath = "user://campaign.cfg";
    public const string DefaultCampaign = "res://campaign/campaign.tres";

    public static Campaign Campaign { get; private set; }
    public static bool Started { get; private set; }
    /// <summary>Missions done; the next one to play is Missions[Done].</summary>
    public static int Done { get; private set; }
    public static readonly HashSet<string> Units = new(), Weapons = new();
    /// <summary>The roster: resource paths of the pilots' profiles.</summary>
    public static readonly List<string> Pilots = new();
    /// <summary>The player's machine and what each machine carries, as GameConfig keeps them for a battle of its own.</summary>
    public static string UnitId = "";
    public static readonly Dictionary<string, string[]> Loadouts = new();
    public static readonly Dictionary<string, Dictionary<string, bool>> Fitted = new();

    static bool _loaded;
    static readonly Dictionary<string, HashSet<string>> StockWeapons = new();

    public static MissionInfo Next => Campaign != null && Done < Campaign.Missions.Length ? Campaign.Missions[Done] : null;
    public static bool Finished => Campaign != null && Started && Done >= Campaign.Missions.Length;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        string path = DefaultCampaign;
        bool saved = cfg.Load(FilePath) == Error.Ok;
        if (saved) path = (string)cfg.GetValue("campaign", "path", path);
        Campaign = ResourceLoader.Exists(path) ? GD.Load<Campaign>(path) : null;
        if (!saved || Campaign == null)
        {
            Reset();
            return;
        }
        Started = (bool)cfg.GetValue("campaign", "started", false);
        Done = (int)cfg.GetValue("campaign", "done", 0);
        UnitId = (string)cfg.GetValue("campaign", "unit", "");
        Units.Clear();
        Weapons.Clear();
        Pilots.Clear();
        foreach (string id in (string[])cfg.GetValue("hangar", "units", System.Array.Empty<string>())) Units.Add(id);
        foreach (string id in (string[])cfg.GetValue("hangar", "weapons", System.Array.Empty<string>())) Weapons.Add(id);
        Pilots.AddRange((string[])cfg.GetValue("roster", "pilots", System.Array.Empty<string>()));
        Loadouts.Clear();
        Fitted.Clear();
        if (cfg.HasSection("hardpoints"))
            foreach (string unit in cfg.GetSectionKeys("hardpoints"))
                Loadouts[unit] = (string[])cfg.GetValue("hardpoints", unit);
        if (cfg.HasSection("equipment"))
            foreach (string unit in cfg.GetSectionKeys("equipment"))
                Fitted[unit] = Equipment.Unpack((string)cfg.GetValue("equipment", unit));
    }

    /// <summary>Back to the beginning of the story: the starting hangar and roster, nothing done.</summary>
    public static void Reset()
    {
        Done = 0;
        Started = false;
        Units.Clear();
        Weapons.Clear();
        Pilots.Clear();
        Loadouts.Clear();
        Fitted.Clear();
        UnitId = "";
        if (Campaign == null) return;
        foreach (string id in Campaign.StartUnits) Units.Add(id);
        foreach (string id in Campaign.StartWeapons) Weapons.Add(id);
        foreach (var pilot in Campaign.StartPilots)
            if (pilot != null && !Pilots.Contains(pilot.ResourcePath)) Pilots.Add(pilot.ResourcePath);
        UnitId = Campaign.StartUnits.Length > 0 ? Campaign.StartUnits[0] : "";
    }

    /// <summary>Starts the story afresh (the old progress is lost).</summary>
    public static void NewGame()
    {
        EnsureLoaded();
        Reset();
        Started = true;
        Save();
    }

    public static void MarkStarted()
    {
        if (Started) return;
        Started = true;
        Save();
    }

    public static bool IsDone(MissionInfo mission) => Campaign != null && Campaign.IndexOf(mission) is var i && i >= 0 && i < Done;
    public static bool IsOpen(MissionInfo mission) => Campaign != null && Campaign.IndexOf(mission) is var i && i >= 0 && i <= Done;

    /// <summary>
    /// The mission is won. The first time, the story moves on and its rewards go into the hangar and the
    /// roster (replaying one already done gives nothing new). Returns whether this was the first time.
    /// </summary>
    public static bool Complete(MissionInfo mission)
    {
        EnsureLoaded();
        if (Campaign == null || Campaign.IndexOf(mission) != Done) return false;
        Done++;
        foreach (string id in mission.GrantUnits) Units.Add(id);
        foreach (string id in mission.GrantWeapons) Weapons.Add(id);
        foreach (var pilot in mission.GrantPilots)
            if (pilot != null && !Pilots.Contains(pilot.ResourcePath)) Pilots.Add(pilot.ResourcePath);
        Save();
        return true;
    }

    public static bool OwnsUnit(string id) => Units.Contains(id);

    /// <summary>Given by the story, or fitted as standard to one of the player's machines.</summary>
    public static bool OwnsWeapon(string id)
    {
        if (Weapons.Contains(id)) return true;
        foreach (string unit in Units)
            if (StockOf(unit).Contains(id)) return true;
        return false;
    }

    static HashSet<string> StockOf(string unit)
    {
        if (StockWeapons.TryGetValue(unit, out var set)) return set;
        set = new HashSet<string>();
        var entry = Catalog.Find(Catalog.PlayerUnits, unit);
        if (entry.Id == unit && GD.Load<PackedScene>(entry.Path)?.Instantiate() is Node node)
        {
            foreach (string fit in Hardpoint.Stock(node))
                foreach (var (id, _, _) in Hardpoint.Parse(fit))
                    set.Add(id);
            node.Free();
        }
        return StockWeapons[unit] = set;
    }

    public static IEnumerable<PilotProfile> Roster()
    {
        foreach (string path in Pilots)
            if (ResourceLoader.Exists(path) && GD.Load<PilotProfile>(path) is { } pilot)
                yield return pilot;
    }

    public static void Save()
    {
        if (Campaign == null) return;
        // While the menus work on the campaign, the hangar's live machine and loadouts are the campaign's.
        if (GameConfig.CampaignMode) GameConfig.CaptureCampaign();
        var cfg = new ConfigFile();
        cfg.SetValue("campaign", "path", Campaign.ResourcePath);
        cfg.SetValue("campaign", "started", Started);
        cfg.SetValue("campaign", "done", Done);
        cfg.SetValue("campaign", "unit", UnitId);
        cfg.SetValue("hangar", "units", new List<string>(Units).ToArray());
        cfg.SetValue("hangar", "weapons", new List<string>(Weapons).ToArray());
        cfg.SetValue("roster", "pilots", Pilots.ToArray());
        foreach (var (unit, fit) in Loadouts) cfg.SetValue("hardpoints", unit, fit);
        foreach (var (unit, kit) in Fitted) cfg.SetValue("equipment", unit, Equipment.Pack(kit));
        cfg.Save(FilePath);
    }
}
