using Godot;

namespace MechSim;

/// <summary>
/// Guided missile. Leaves the rack on a lofted course, then turns towards the locked target (or, with no
/// lock, towards the point that was aimed at). Its seeker only sees ahead and it only turns so fast: a
/// target that gets out of its cone is lost for good, and a missile with nothing to chase blows itself
/// up shortly after. It never flies further than the weapon's range.
/// The looks, trail and flight numbers are in scenes/fx/missile.tscn.
/// </summary>
public partial class Missile : Projectile
{
    [Export] public float Speed = 110f;
    /// <summary>How hard it can turn, rad/s.</summary>
    [Export] public float TurnRate = 1.0f;
    /// <summary>Half-angle of what the seeker can see, radians. A target outside it is lost.</summary>
    [Export] public float SeekerCone = 0.8f;
    /// <summary>Seconds a missile flies on after losing its target before it self-destructs.</summary>
    [Export] public float LostFuse = 1.2f;
    /// <summary>Radius of the mark it leaves on the ground.</summary>
    [Export] public float CraterSize = 1.6f;
    /// <summary>Seconds of straight flight before guidance kicks in.</summary>
    [Export] public float ArmTime = 0.25f;
    /// <summary>Upward kick of the launch direction.</summary>
    [Export] public float Loft = 0.22f;
    /// <summary>Random spread of the launch direction.</summary>
    [Export] public float Scatter = 0.05f;
    /// <summary>What the mark looks like: a blast for warheads, a burn for energy bolts.</summary>
    [Export] public Mark MarkKind = Mark.Blast;
    /// <summary>Sets fire to what it destroys, as a beam does (energy bolts).</summary>
    [Export] public bool Burns;
    /// <summary>Spawned where it hits.</summary>
    [Export] public PackedScene ImpactScene;

    Vector3 _dir, _aimPoint;
    ITarget _target;
    float _damage, _left, _age;
    uint _mask;
    Godot.Collections.Array<Rid> _exclude;

    public override void Launch(Vector3 from, Vector3 aimPoint, ITarget target, float damage, float range, uint mask,
                                Godot.Collections.Array<Rid> exclude, Vector3 carrierVelocity)
    {
        _aimPoint = aimPoint;
        _target = target;
        _damage = damage;
        _left = range;
        _mask = mask;
        _exclude = exclude;

        var jitter = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * Scatter;
        _dir = ((aimPoint - from).Normalized() + Vector3.Up * Loft + jitter).Normalized();
        GlobalPosition = from;
        Face();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;

        if (_age > ArmTime)
        {
            bool locked = _target is Node node && IsInstanceValid(node) && !_target.IsDestroyed;
            if (locked && _dir.AngleTo(_target.AimCenter - GlobalPosition) > SeekerCone)
            {
                // Flew past or was out-turned: no second chances.
                locked = false;
                _target = null;
                _aimPoint = GlobalPosition + _dir * 10000f;
                _left = Mathf.Min(_left, Speed * LostFuse);
            }
            Vector3 goal = locked ? _target.AimCenter : _aimPoint;
            Vector3 want = goal - GlobalPosition;
            // Without a lock, stop steering once the aim point is reached and just fly on.
            if (locked || want.Dot(_dir) > 0f)
            {
                want = want.Normalized();
                float angle = _dir.AngleTo(want);
                if (angle > 1e-3f)
                    _dir = _dir.Slerp(want, Mathf.Min(1f, TurnRate * dt / angle)).Normalized();
            }
        }

        Vector3 from = GlobalPosition;
        Vector3 to = from + _dir * Speed * dt;
        if (Phys.Ray(GetWorld3D().DirectSpaceState, from, to, _mask, _exclude, out var hit))
        {
            Burning.Heat = Burns;
            Phys.Damage(hit, _damage);
            Burning.Heat = false;
            Impacts.Ground(GetParent(), hit, CraterSize, MarkKind);
            Detonate(hit.Position);
            return;
        }
        Impacts.Water(GetParent(), from, to, 1.5f);

        GlobalPosition = to;
        Face();
        _left -= Speed * dt;
        if (_left <= 0f) Detonate(to);
    }

    void Face()
    {
        Vector3 up = Mathf.Abs(_dir.Dot(Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
        LookAt(GlobalPosition + _dir, up);
    }

    void Detonate(Vector3 at)
    {
        if (ImpactScene != null)
        {
            var fx = ImpactScene.Instantiate<Node3D>();
            GetParent().AddChild(fx);
            fx.GlobalPosition = at;
        }
        QueueFree();
    }
}
