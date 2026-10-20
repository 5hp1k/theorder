using System.Text;
using Godot;

namespace MechSim;

/// <summary>
/// A tool, not part of the game: stands one unit up in a battle as the game puts it together (its
/// weapons on their hardpoints, legs on the ground, and walking if asked) and writes every visible mesh of
/// it to an OBJ file for tools/blender/check_assembled.py, in the unit's own axes, one object per mesh
/// node; before each weapon's mesh a "# weapon" line says where its back end is and which way it points.
///     godot-mono --headless --path . tools/export_unit.tscn -- &lt;unit&gt; &lt;output dir&gt; [walk] [default]
/// "walk": caught in mid-stride; "default": with the catalogue's weapons, not the ones the player fitted.
/// tools/export_units.sh does it for every unit and runs the check.
/// </summary>
public partial class ExportUnit : Node
{
    [Export] public float SettleTime = 3f;

    string _unit = "hound", _dir = ".";
    bool _walk;
    float _t;

    public override void _EnterTree()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Length > 0) _unit = args[0];
        if (args.Length > 1) _dir = args[1];
        _walk = System.Array.IndexOf(args, "walk") >= 0;
        GameConfig.EnsureLoaded();
        GameConfig.MapId = _unit is "ship" or "amphibian" ? "coast" : "desert";
        GameConfig.MechId = _unit;
        GameConfig.EnemyCount = 0;
        GameConfig.EnemyVehicleCount = 0;
        GameConfig.SquadSize = 0;
        GameConfig.WeatherId = 1;
        GameConfig.TimeLimitMinutes = 0;
        if (System.Array.IndexOf(args, "default") >= 0)
        {
            GameConfig.Loadouts.Clear();
            GameConfig.Fitted.Clear();
        }
        AddChild(GD.Load<PackedScene>("res://scenes/game.tscn").Instantiate());
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta;
        if (Game.PlayerUnit is not Node3D unit) return;
        if (_walk && unit is Mech mech) mech.Throttle = 1f;
        if (_t < SettleTime) return;
        Write(unit);
        GetTree().Quit();
    }

    void Write(Node3D unit)
    {
        var sb = new StringBuilder();
        int based = 1;
        Transform3D inv = unit.GlobalTransform.AffineInverse();
        foreach (var mesh in Geo.Collect<MeshInstance3D>(unit))
        {
            // (not the inside of the cab: it is drawn only in first-person view)
            if (mesh.Mesh == null || !mesh.IsVisibleInTree() || unit.GetPathTo(mesh).ToString().Contains("Cockpit")) continue;
            var faces = mesh.Mesh.GetFaces();
            if (faces.Length == 0) continue;
            if (mesh.GetParent() is Weapon weapon)
            {
                Vector3 forward = (inv.Basis * -weapon.GlobalBasis.Z).Normalized();
                Vector3 back = inv * (weapon.GlobalPosition + weapon.GlobalBasis.Z.Normalized() * weapon.ModelLength * mesh.Scale.Z);
                sb.Append(System.FormattableString.Invariant($"# weapon {back.X:0.####} {back.Y:0.####} {back.Z:0.####} {forward.X:0.####} {forward.Y:0.####} {forward.Z:0.####}\n"));
            }
            sb.Append("o ").Append(unit.GetPathTo(mesh).ToString().Replace(' ', '_')).Append('\n');
            Transform3D t = inv * mesh.GlobalTransform;
            foreach (var v in faces)
            {
                Vector3 w = t * v;
                sb.Append(System.FormattableString.Invariant($"v {w.X:0.######} {w.Y:0.######} {w.Z:0.######}\n"));
            }
            for (int i = 0; i < faces.Length; i += 3)
                sb.Append($"f {based + i} {based + i + 1} {based + i + 2}\n");
            based += faces.Length;
        }
        DirAccess.MakeDirRecursiveAbsolute(_dir);
        using var f = FileAccess.Open($"{_dir}/{_unit}.obj", FileAccess.ModeFlags.Write);
        f.StoreString(sb.ToString());
        GD.Print("EXPORTED ", _unit, " ", based - 1);
    }
}
