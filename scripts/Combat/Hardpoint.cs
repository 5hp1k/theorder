using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A place on a mech or a vehicle where weapons are fitted, as in MechWarrior 4: it takes weapons of
/// one sort (beam, ballistic, missile; an omni mount takes any of the three) and has room for so many
/// slots — a big gun fills several. The weapons are its children: those in the scene are the stock fit,
/// the hangar swaps them for the player's choice.
/// </summary>
public partial class Hardpoint : Node3D
{
    /// <summary>What the hangar calls it; empty = the name of the section it is in.</summary>
    [Export] public string DisplayName = "";
    [Export] public SlotType Type;
    [Export(PropertyHint.Range, "1,8")] public int Slots = 2;
    /// <summary>Section of the mech its weapons are lost with.</summary>
    [Export] public Section Mount = Section.RightArm;
    /// <summary>How far apart the weapons of a custom fit sit (they are spread about the mount's origin).</summary>
    [Export] public Vector3 Spacing = new(0.45f, 0f, 0f);

    static readonly string[] SectionNames = { "Голова", "Корпус", "Левый торс", "Правый торс", "Левая рука", "Правая рука", "Левая нога", "Правая нога" };

    public string Title => DisplayName != "" ? DisplayName : SectionNames[(int)Mount];

    public static bool Fits(SlotType mount, SlotType weapon)
        => mount == weapon || (mount == SlotType.Omni && weapon is SlotType.Beam or SlotType.Ballistic or SlotType.Missile);

    public List<Weapon> Fitted()
    {
        var list = new List<Weapon>();
        foreach (Node child in GetChildren())
            if (child is Weapon w && !w.IsQueuedForDeletion()) list.Add(w);
        return list;
    }

    /// <summary>Hardpoints of a unit, in scene order.</summary>
    public static List<Hardpoint> Of(Node unit) => Geo.Collect<Hardpoint>(unit);

    // ── Loadouts ────────────────────────────────────────────────
    // A loadout is one string per hardpoint: the weapons fitted there, "id:group" joined with "+"
    // (group 0 = the weapon's own), e.g. "medium_laser:1+medium_laser:0". Empty = nothing fitted.

    public static (string id, int group)[] Parse(string fit)
    {
        var list = new List<(string, int)>();
        foreach (string part in (fit ?? "").Split('+', System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] p = part.Split(':');
            int group = 0;
            if (p.Length > 1) int.TryParse(p[1], out group);
            list.Add((p[0], Mathf.Clamp(group, 0, 3)));
        }
        return list.ToArray();
    }

    public static string Format(IEnumerable<(string id, int group)> weapons)
    {
        var parts = new List<string>();
        foreach (var (id, group) in weapons) parts.Add($"{id}:{group}");
        return string.Join("+", parts);
    }

    /// <summary>What the unit carries as it comes from its scene.</summary>
    public static string[] Stock(Node unit)
    {
        var points = Of(unit);
        var result = new string[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var fit = new List<(string, int)>();
            foreach (var w in points[i].Fitted())
                if (Catalog.WeaponIdByPath(w.SceneFilePath) is { } id) fit.Add((id, 0));
            result[i] = Format(fit);
        }
        return result;
    }

    /// <summary>Refits the unit. Call before it enters the tree. A loadout made for another layout is ignored.</summary>
    public static void Apply(Node unit, string[] loadout)
    {
        var points = Of(unit);
        if (loadout == null || loadout.Length != points.Count) return;
        for (int i = 0; i < points.Count; i++) points[i].Fit(Parse(loadout[i]));
    }

    /// <summary>Replaces what is fitted here; weapons of the wrong sort, or that there is no room for, are left out.</summary>
    public void Fit((string id, int group)[] weapons)
    {
        foreach (var old in Fitted())
        {
            RemoveChild(old);
            old.QueueFree();
        }
        var fitted = new List<Weapon>();
        int used = 0;
        foreach (var (id, group) in weapons)
        {
            var entry = Catalog.Find(Catalog.Weapons, id);
            if (entry.Id != id) continue;
            var weapon = GD.Load<PackedScene>(entry.Path).Instantiate<Weapon>();
            if (!Fits(Type, weapon.Slot) || used + weapon.Slots > Slots)
            {
                weapon.Free();
                continue;
            }
            used += weapon.Slots;
            weapon.Mount = Mount;
            if (group >= 1) weapon.Group = group;
            fitted.Add(weapon);
        }
        for (int i = 0; i < fitted.Count; i++)
        {
            fitted[i].Position = Spacing * (i - (fitted.Count - 1) * 0.5f);
            AddChild(fitted[i]);
        }
    }
}
