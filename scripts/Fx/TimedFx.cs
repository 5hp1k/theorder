using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Short-lived glowing mesh (muzzle flash, impact spark, tracer) that frees itself.
/// The looks live in scenes/fx/flash.tscn and tracer.tscn; this only sets colour, size and lifetime.
/// There is one for every shot, so finished ones are kept and used again rather than freed.
/// </summary>
public partial class TimedFx : MeshInstance3D
{
    static PackedScene _flash, _tracer;

    float _life, _total, _radius;
    Stack<TimedFx> _shelf;
    static readonly Stack<TimedFx> Flashes = new(), Tracers = new();
    /// <summary>Most finished ones of each kind kept for later.</summary>
    const int ShelfSize = 96;

    /// <summary>Forget the kept ones (a new battle: they went with the old one).</summary>
    public static void Reset()
    {
        Flashes.Clear();
        Tracers.Clear();
    }

    public static void Flash(Node parent, Vector3 pos, Color color, float radius, float life)
    {
        _flash ??= GD.Load<PackedScene>("res://scenes/fx/flash.tscn");
        // (A flash is only for the eye; the gun's own sound is elsewhere.)
        if (!OneShotFx.Seen(pos, radius)) return;
        var fx = Spawn(_flash, Flashes, parent, color, life);
        fx._radius = radius;
        fx.GlobalPosition = pos;
        fx.Scale = Vector3.One * radius;
    }

    public static void Tracer(Node parent, Vector3 from, Vector3 to, Color color, float life)
    {
        _tracer ??= GD.Load<PackedScene>("res://scenes/fx/tracer.tscn");
        var fx = Spawn(_tracer, Tracers, parent, color, life);
        fx._radius = 0f;
        fx.Scale = Vector3.One;
        Geo.PlaceSegment(fx, from, to, Vector3.Up);
    }

    static TimedFx Spawn(PackedScene scene, Stack<TimedFx> shelf, Node parent, Color color, float life)
    {
        TimedFx fx = null;
        while (shelf.Count > 0 && fx == null)
        {
            fx = shelf.Pop();
            if (!IsInstanceValid(fx) || fx.IsQueuedForDeletion()) fx = null;
        }
        if (fx != null)
        {
            if (fx.GetParent() != parent) fx.Reparent(parent, false);
            fx.Visible = true;
            fx.SetProcess(true);
        }
        else
        {
            fx = scene.Instantiate<TimedFx>();
            fx._shelf = shelf;
            parent.AddChild(fx);
        }
        fx._life = fx._total = life;
        // The material is local to each instance (see the scene), so recolouring one doesn't touch the others.
        if (fx.MaterialOverride is StandardMaterial3D mat) mat.Emission = color;
        return fx;
    }

    public override void _Process(double delta)
    {
        _life -= (float)delta;
        if (_life <= 0f)
        {
            if (_shelf == null || _shelf.Count >= ShelfSize)
            {
                QueueFree();
                return;
            }
            Visible = false;
            SetProcess(false);
            _shelf.Push(this);
            return;
        }
        if (_radius > 0f)
            Scale = Vector3.One * (_radius * (0.4f + 0.6f * _life / _total));
    }
}
