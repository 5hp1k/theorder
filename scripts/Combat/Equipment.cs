using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// What a machine carries besides its weapons, switched on or off in the hangar: jump jets (only for a
/// mech built for them — one with a Hips/JumpJets node) and an anti-missile system (<see cref="Ams"/>,
/// scenes/weapons/ams.tscn). Kept in GameConfig.Fitted and sent along at the end of the loadout as
/// "#jump_jets=0,ams=1".
/// </summary>
public static class Equipment
{
    public const string JumpJets = "jump_jets", AntiMissile = "ams";
    const string AmsScene = "res://scenes/weapons/ams.tscn";
    /// <summary>Where a fitted anti-missile gun goes on the torso when the scene has none of its own.</summary>
    static readonly Vector3 AmsMount = new(0f, 2.9f, 0.3f);

    public static bool CanJump(Node unit) => unit is Mech && unit.GetNodeOrNull("Hips/JumpJets") != null;
    public static bool CanCarryAms(Node unit) => unit is Mech;

    /// <summary>Whether the machine, as it is now, has the equipment.</summary>
    public static bool Has(Node unit, string what) => what switch
    {
        JumpJets => unit is Mech { HasJumpJets: true },
        AntiMissile => Ams.Of(unit) != null,
        _ => false,
    };

    /// <summary>Fits or takes off equipment. Call before the machine enters the tree.</summary>
    public static void Apply(Node unit, Dictionary<string, bool> kit)
    {
        if (unit is not Mech mech) return;
        if (kit.TryGetValue(JumpJets, out bool jets) && CanJump(unit)) mech.HasJumpJets = jets;
        if (kit.TryGetValue(AntiMissile, out bool ams))
        {
            var fitted = Ams.Of(unit);
            if (ams && fitted == null && unit.GetNodeOrNull<Node3D>("Hips/Torso") is { } torso)
            {
                var gun = GD.Load<PackedScene>(AmsScene).Instantiate<Node3D>();
                gun.Position = AmsMount;
                torso.AddChild(gun);
            }
            else if (!ams && fitted != null)
            {
                fitted.GetParent().RemoveChild(fitted);
                fitted.QueueFree();
            }
        }
    }

    public static bool IsPacked(string s) => s != null && s.StartsWith('#');

    public static string Pack(Dictionary<string, bool> kit)
    {
        var parts = new List<string>();
        foreach (var (key, on) in kit) parts.Add($"{key}={(on ? 1 : 0)}");
        return "#" + string.Join(",", parts);
    }

    public static Dictionary<string, bool> Unpack(string s)
    {
        var kit = new Dictionary<string, bool>();
        foreach (string part in (s ?? "").TrimStart('#').Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] p = part.Split('=');
            if (p.Length == 2) kit[p[0]] = p[1] == "1";
        }
        return kit;
    }
}
