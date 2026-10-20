using Godot;

namespace MechSim;

/// <summary>
/// What a machine looks like, for the mission editor: its model as the game would put it on the field
/// — the weapons fitted on their hardpoints, painted — but only its meshes, copied into plain nodes, so
/// that nothing of it thinks, moves or counts as a unit on the field.
/// </summary>
public static class UnitPreview
{
    /// <summary>The enemy's usual camouflage (scenes/game.tscn: Game.EnemyCamo, EnemyStripe, EnemyScheme).</summary>
    static PaintJob EnemyPaint() => new()
    {
        Camo = GD.Load<Material>("res://materials/camo_rust.tres"),
        Second = new Color(0.5f, 0.1f, 0.06f),
        Scheme = 1,
    };

    /// <summary>The machine's look; paint null — its side's usual paint.</summary>
    public static Node3D Build(string unitId, string[] loadout, PaintJob paint, bool enemy)
    {
        var entry = Catalog.Find(Catalog.PlayerUnits, unitId);
        var unit = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
        if (loadout is { Length: > 0 }) Hardpoint.Apply(unit, loadout);
        paint ??= enemy ? EnemyPaint() : GameConfig.PlayerPaint();
        if (unit is Mech mech) mech.ApplyPaint(paint);
        else if (unit is Vehicle vehicle) vehicle.ApplyPaint(paint);

        var look = new Node3D { Name = "Look" };
        Copy(unit, unit, Transform3D.Identity, look);
        unit.Free();
        return look;
    }

    static void Copy(Node from, Node unit, Transform3D at, Node3D into)
    {
        foreach (Node child in from.GetChildren())
        {
            if (child is not Node3D n || !n.Visible) continue;
            // (not the cab's inside, the emblem plates or a building's stand-in box)
            string name = n.Name;
            if (name == "Cockpit" || name.StartsWith("Insignia") || name == "Preview") continue;
            Transform3D here = at * n.Transform;
            if (n is MeshInstance3D { Mesh: not null } mesh)
            {
                var copy = new MeshInstance3D { Mesh = mesh.Mesh, MaterialOverride = mesh.MaterialOverride, Transform = here, CastShadow = mesh.CastShadow };
                for (int s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++)
                    if (mesh.GetSurfaceOverrideMaterial(s) is { } m) copy.SetSurfaceOverrideMaterial(s, m);
                into.AddChild(copy);
            }
            Copy(n, unit, here, into);
        }
    }
}
