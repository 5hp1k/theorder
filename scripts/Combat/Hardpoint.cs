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
    /// <summary>
    /// Whether the weapons fitted here show their own models (set out in front of the mount, see
    /// Layout). Off for mounts whose launcher is part of the machine's own model (pods built into a
    /// wing, a tilted deck launcher): there the weapons keep their places and show nothing of their own.
    /// </summary>
    [Export] public bool ShowModels = true;
    /// <summary>How far apart the weapons fitted here sit (they are spread about the mount's origin).</summary>
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
    // A loadout is one string per hardpoint: the weapons fitted there, "id:group[:rounds]" joined with
    // "+" (group 0 = the weapon's own; rounds left out or 0 = its usual load), e.g.
    // "medium_laser:1+autocannon:0:60". Empty = nothing fitted.

    /// <summary>Most rounds a weapon may be given, as a multiple of its usual load.</summary>
    public const int MaxAmmoTimes = 3;

    public static (string id, int group, int ammo)[] Parse(string fit)
    {
        var list = new List<(string, int, int)>();
        foreach (string part in (fit ?? "").Split('+', System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] p = part.Split(':');
            int group = 0, ammo = 0;
            if (p.Length > 1) int.TryParse(p[1], out group);
            if (p.Length > 2) int.TryParse(p[2], out ammo);
            list.Add((p[0], Mathf.Clamp(group, 0, WeaponSystem.GroupCount), Mathf.Max(ammo, 0)));
        }
        return list.ToArray();
    }

    public static string Format(IEnumerable<(string id, int group, int ammo)> weapons)
    {
        var parts = new List<string>();
        foreach (var (id, group, ammo) in weapons) parts.Add(ammo > 0 ? $"{id}:{group}:{ammo}" : $"{id}:{group}");
        return string.Join("+", parts);
    }

    /// <summary>What the unit carries as it comes from its scene.</summary>
    public static string[] Stock(Node unit)
    {
        var points = Of(unit);
        var result = new string[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var fit = new List<(string, int, int)>();
            foreach (var w in points[i].Fitted())
                if (Catalog.WeaponIdByPath(w.SceneFilePath) is { } id) fit.Add((id, 0, 0));
            result[i] = Format(fit);
        }
        return result;
    }

    /// <summary>What the unit carries right now, with each weapon's group as it is set now.</summary>
    public static string[] Current(Node unit)
    {
        var points = Of(unit);
        var result = new string[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var fit = new List<(string, int, int)>();
            foreach (var w in points[i].Fitted())
                if (Catalog.WeaponIdByPath(w.SceneFilePath) is { } id) fit.Add((id, w.Group, Mathf.Max(w.MaxAmmo, 0)));
            result[i] = Format(fit);
        }
        return result;
    }

    /// <summary>Refits the unit. Call before it enters the tree. A loadout made for another layout is ignored.</summary>
    public static void Apply(Node unit, string[] loadout)
    {
        // (The equipment, if any, rides at the end: see GameConfig.LoadoutFor.)
        if (loadout is { Length: > 0 } && Equipment.IsPacked(loadout[^1]))
        {
            Equipment.Apply(unit, Equipment.Unpack(loadout[^1]));
            loadout = loadout.Length > 1 ? loadout[..^1] : null;
        }
        var points = Of(unit);
        if (loadout == null || loadout.Length != points.Count) return;
        for (int i = 0; i < points.Count; i++) points[i].Fit(Parse(loadout[i]));
    }

    /// <summary>Replaces what is fitted here; weapons of the wrong sort, or that there is no room for, are left out.</summary>
    public void Fit((string id, int group, int ammo)[] weapons)
    {
        foreach (var old in Fitted())
        {
            RemoveChild(old);
            old.QueueFree();
        }
        var fitted = new List<Weapon>();
        int used = 0;
        foreach (var (id, group, ammo) in weapons)
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
            // The load the player chose, for a weapon that has one at all.
            if (ammo > 0 && weapon.MaxAmmo > 0) weapon.MaxAmmo = Mathf.Min(ammo, weapon.MaxAmmo * MaxAmmoTimes);
            fitted.Add(weapon);
        }
        foreach (var weapon in fitted) AddChild(weapon);
        Layout();
    }

    public override void _Ready() => Layout();

    /// <summary>
    /// Sets out what is fitted. The hardpoint is where the guns are fixed to the machine: each weapon with
    /// a model of its own stands in front of it (-Z) by its length, its back on the mount. With the
    /// mount's face measured (Area), they are set out in rows and columns that fit on it — whichever
    /// arrangement leaves them biggest — and, if even the best one does not fit, the models are drawn
    /// smaller (down to MinScale), so that nothing hangs off the machine. Without an Area they go side
    /// by side along Spacing. (A hand weapon or a bomb bay, without a model, keeps its place.)
    /// </summary>
    public void Layout()
    {
        var fitted = Fitted();
        var shown = new List<Weapon>();
        foreach (var w in fitted)
        {
            var model = w.GetNodeOrNull<Node3D>("Model");
            if (model != null) model.Visible = ShowModels && !w.Destroyed;
            if (ShowModels && w.ModelLength > 0f && model != null) shown.Add(w);
            else if (ShowModels && fitted.Count > 1) w.Position = Spacing * (fitted.IndexOf(w) - (fitted.Count - 1) * 0.5f);
        }
        if (shown.Count == 0) return;
        if (Area == Vector2.Zero)
        {
            for (int i = 0; i < shown.Count; i++)
                Place(shown[i], Spacing * (i - (shown.Count - 1) * 0.5f), 1f);
            return;
        }
        // One cell per weapon, as big as the biggest of them across.
        Vector2 cell = Vector2.Zero;
        foreach (var w in shown) cell = cell.Max(Face(w) + new Vector2(Gap, Gap));
        int n = shown.Count, bestCols = 1;
        float bestScale = -1f;
        for (int cols = 1; cols <= n; cols++)
        {
            int rows = (n + cols - 1) / cols;
            // (the roomiest arrangement, even among those that fit at full size)
            float k = Mathf.Min(Area.X / (cols * cell.X), Area.Y / (rows * cell.Y));
            if (k > bestScale + 1e-4f)
            {
                bestScale = k;
                bestCols = cols;
            }
        }
        float scale = Mathf.Clamp(bestScale, MinScale, 1f);
        int c = bestCols, r = (n + c - 1) / c;
        for (int i = 0; i < n; i++)
        {
            int col = i % c, row = i / c;
            // the last row, if short, is centred
            int inRow = row == r - 1 ? n - row * c : c;
            float x = (col - (inRow - 1) * 0.5f) * cell.X * scale;
            float y = ((r - 1) * 0.5f - row) * cell.Y * scale;
            Place(shown[i], new Vector3(x, y, 0f), scale);
        }
    }

    /// <summary>Size of the mount's face, metres (across, up); zero — not measured, see Layout.</summary>
    [Export] public Vector2 Area;
    /// <summary>The least the weapons' models are drawn at to fit on the mount.</summary>
    [Export(PropertyHint.Range, "0.3,1")] public float MinScale = 0.5f;
    /// <summary>Room left between neighbouring weapons, metres.</summary>
    const float Gap = 0.04f;

    static void Place(Weapon w, Vector3 at, float scale)
    {
        w.Transform = new Transform3D(Basis.Identity, at + new Vector3(0f, 0f, -w.ModelLength * scale));
        w.GetNode<Node3D>("Model").Scale = Vector3.One * scale;
    }

    static readonly Dictionary<Mesh, Vector2> Faces = new();

    /// <summary>How wide and tall a weapon's model is, seen from in front.</summary>
    static Vector2 Face(Weapon w)
    {
        if (w.GetNodeOrNull<MeshInstance3D>("Model")?.Mesh is not { } mesh) return Vector2.One * 0.3f;
        if (!Faces.TryGetValue(mesh, out var face))
        {
            var box = mesh.GetAabb();
            Faces[mesh] = face = new Vector2(box.Size.X, box.Size.Y);
        }
        return face;
    }
}
