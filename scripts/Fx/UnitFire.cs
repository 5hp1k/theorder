using Godot;

namespace MechSim;

/// <summary>
/// A machine set alight by a flamethrower (scenes/fx/unit_fire.tscn): it rides on the part that was hit
/// — a section of a mech, the hull of a vehicle — and burns there for <see cref="Duration"/> seconds after
/// the jet last played on it, eating at the armour and (on a mech) pouring heat into the reactor. Going
/// into water puts it out. Then the flames die and it frees itself.
/// </summary>
public partial class UnitFire : Node3D
{
    [Export] public float Duration = 6f;
    [Export] public float DamagePerSecond = 2f;
    /// <summary>Heat per second for a mech, as a share of its shutdown threshold.</summary>
    [Export] public float HeatPerSecond = 0.035f;

    float _left, _tick;
    bool _out;
    IDamageable _victim;
    Mech _mech;

    /// <summary>Sets the part alight (or keeps it burning, if it already is) at a point on it.</summary>
    public static void Ignite(Node3D part, Vector3 at, float seconds)
    {
        if (part.GetNodeOrNull<UnitFire>("UnitFire") is { _out: false } burning)
        {
            burning._left = Mathf.Max(burning._left, seconds);
            return;
        }
        var fire = GD.Load<PackedScene>("res://scenes/fx/unit_fire.tscn").Instantiate<UnitFire>();
        fire.Name = "UnitFire";
        fire.Duration = seconds;
        part.AddChild(fire);
        fire.GlobalPosition = at;
    }

    public override void _Ready()
    {
        _left = Duration;
        _victim = GetParent() as IDamageable;
        _mech = (GetParent() as HitBox)?.Unit as Mech;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_out) return;
        float dt = (float)delta;
        _left -= dt;
        var terrain = MapTerrain.Instance;
        bool doused = terrain is { HasWater: true } && GlobalPosition.Y < terrain.WaterLevel && terrain.WaterDepthAt(GlobalPosition.X, GlobalPosition.Z) > 0.05f;
        bool dead = _victim is ITarget { IsDestroyed: true } || _mech is { IsDestroyed: true };
        if (_left <= 0f || doused || dead)
        {
            GoOut();
            return;
        }
        // (Dealt a second's worth at a time: every hit shakes the machine and rattles its plating.)
        _tick += dt;
        if (_tick >= 1f)
        {
            _tick -= 1f;
            if (Net.Authority) _victim?.TakeDamage(DamagePerSecond, GlobalPosition);
        }
        _mech?.AddHeat(HeatPerSecond * dt);
    }

    void GoOut()
    {
        _out = true;
        Name = "UnitFireOut";
        float longest = 0f;
        foreach (Node child in GetChildren())
        {
            if (child is GpuParticles3D p)
            {
                p.Emitting = false;
                longest = Mathf.Max(longest, (float)p.Lifetime);
            }
            else if (child is OmniLight3D light) light.Visible = false;
            else if (child is AudioStreamPlayer3D sound) sound.Stop();
        }
        GetTree().CreateTimer(longest + 0.2f, false).Timeout += QueueFree;
    }
}
