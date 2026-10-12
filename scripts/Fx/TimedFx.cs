using Godot;

namespace MechSim;

/// <summary>
/// Short-lived glowing mesh (muzzle flash, impact spark, tracer) that frees itself.
/// The looks live in scenes/fx/flash.tscn and tracer.tscn; this only sets colour, size and lifetime.
/// </summary>
public partial class TimedFx : MeshInstance3D
{
    static PackedScene _flash, _tracer;

    float _life, _total, _radius;

    public static void Flash(Node parent, Vector3 pos, Color color, float radius, float life)
    {
        _flash ??= GD.Load<PackedScene>("res://scenes/fx/flash.tscn");
        var fx = Spawn(_flash, parent, color, life);
        fx._radius = radius;
        fx.GlobalPosition = pos;
        fx.Scale = Vector3.One * radius;
    }

    public static void Tracer(Node parent, Vector3 from, Vector3 to, Color color, float life)
    {
        _tracer ??= GD.Load<PackedScene>("res://scenes/fx/tracer.tscn");
        var fx = Spawn(_tracer, parent, color, life);
        Geo.PlaceSegment(fx, from, to, Vector3.Up);
    }

    static TimedFx Spawn(PackedScene scene, Node parent, Color color, float life)
    {
        var fx = scene.Instantiate<TimedFx>();
        fx._life = fx._total = life;
        parent.AddChild(fx);
        // The material is local to each instance (see the scene), so recolouring one doesn't touch the others.
        if (fx.MaterialOverride is StandardMaterial3D mat) mat.Emission = color;
        return fx;
    }

    public override void _Process(double delta)
    {
        _life -= (float)delta;
        if (_life <= 0f)
        {
            QueueFree();
            return;
        }
        if (_radius > 0f)
            Scale = Vector3.One * (_radius * (0.4f + 0.6f * _life / _total));
    }
}
