using Godot;

namespace MechSim;

/// <summary>
/// Free-fall bomb: keeps the bomber's speed, drops under gravity and hurts everything within its blast
/// radius, less towards the edge. The looks and numbers are in scenes/fx/bomb.tscn.
/// </summary>
public partial class Bomb : Projectile
{
    [Export] public float Gravity = 20f;
    /// <summary>Air drag, 1/s: a retarded bomb (a parachute) falls slowly and lets the bomber get clear. 0 = none.</summary>
    [Export] public float Drag;
    /// <summary>0 = no blast of its own (the impact scene does the damage: a nuclear bomb).</summary>
    [Export] public float BlastRadius = 14f;
    /// <summary>Radius of the crater it leaves on the ground.</summary>
    [Export] public float CraterSize = 5f;
    /// <summary>Everyone is warned of it while it falls (a nuclear bomb): see <see cref="Falling"/> and the HUD's NukeWarning.</summary>
    [Export] public bool Warn;
    /// <summary>Spawned where it lands.</summary>
    [Export] public PackedScene ImpactScene;

    /// <summary>The bombs that everyone is being warned of, while they are in the air.</summary>
    public static readonly System.Collections.Generic.List<Bomb> Falling = new();

    /// <summary>Seconds until it lands, near enough: the height left at the speed it is falling.</summary>
    public float TimeToGround
    {
        get
        {
            Vector3 p = GlobalPosition;
            float ground = MapTerrain.Instance?.HeightAt(p.X, p.Z) ?? 0f;
            // (A retarded bomb is at its steady rate of fall almost at once.)
            float fall = Mathf.Max(-_velocity.Y, Drag > 0f ? Gravity / Drag : 5f);
            return Mathf.Max(0f, p.Y - ground) / fall;
        }
    }

    public override void _EnterTree()
    {
        if (Warn) Falling.Add(this);
    }

    public override void _ExitTree() => Falling.Remove(this);

    Vector3 _velocity;
    float _damage, _life = 20f;
    uint _mask;
    Godot.Collections.Array<Rid> _exclude;

    public override void Launch(Vector3 from, Vector3 aimPoint, ITarget target, float damage, float range, uint mask,
                                Godot.Collections.Array<Rid> exclude, Vector3 carrierVelocity)
    {
        _velocity = carrierVelocity;
        _damage = damage;
        _mask = mask;
        _exclude = exclude;
        GlobalPosition = from;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _velocity.Y -= Gravity * dt;
        if (Drag > 0f) _velocity *= Mathf.Exp(-Drag * dt);
        Vector3 from = GlobalPosition;
        Vector3 to = from + _velocity * dt;
        var space = GetWorld3D().DirectSpaceState;
        if (Phys.Ray(space, from, to, _mask, _exclude, out var hit))
        {
            if (BlastRadius > 0f) Phys.Blast(space, hit.Position, BlastRadius, _damage, _mask, _exclude);
            Impacts.Ground(GetParent(), hit, CraterSize);
            if (ImpactScene != null)
            {
                var fx = ImpactScene.Instantiate<Node3D>();
                if (fx is NukeBlast nuke)
                {
                    nuke.Damage = _damage;
                    nuke.Shooter = Shooter;
                }
                GetParent().AddChild(fx);
                fx.GlobalPosition = hit.Position;
            }
            QueueFree();
            return;
        }
        Impacts.Water(GetParent(), from, to, 3f);
        GlobalPosition = to;
        if (_velocity.LengthSquared() > 1f) LookAt(to + _velocity, Vector3.Up);
        _life -= dt;
        if (_life <= 0f) QueueFree();
    }
}
