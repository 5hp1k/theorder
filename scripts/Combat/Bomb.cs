using Godot;

namespace MechSim;

/// <summary>
/// Free-fall bomb: keeps the bomber's speed, drops under gravity and hurts everything within its blast
/// radius, less towards the edge. The looks and numbers are in scenes/fx/bomb.tscn. A howitzer's shell
/// (scenes/fx/shell.tscn) is the same thing thrown from a gun: see <see cref="Weapon.MuzzleSpeed"/>.
/// </summary>
public partial class Bomb : Projectile
{
    [Export] public float Gravity = 20f;
    /// <summary>Air drag, 1/s: a retarded bomb (a parachute) falls slowly and lets the bomber get clear. 0 = none.</summary>
    [Export] public float Drag;
    /// <summary>0 = no blast of its own (the impact scene does the damage: a nuclear bomb).</summary>
    [Export] public float BlastRadius = 14f;
    /// <summary>What it lands on takes this share of the damage on top of the blast (a shell striking home).</summary>
    [Export] public float DirectHit;
    /// <summary>Radius of the crater it leaves on the ground.</summary>
    [Export] public float CraterSize = 5f;
    /// <summary>Everyone is warned of it while it falls (a nuclear bomb): see <see cref="Falling"/> and the HUD's NukeWarning.</summary>
    [Export] public bool Warn;
    /// <summary>What the mark on the ground looks like.</summary>
    [Export] public Mark MarkKind = Mark.Blast;
    /// <summary>How deep the hole is, as a share of its radius.</summary>
    [Export] public float CraterDepth = 0.3f;
    /// <summary>An electromagnetic bomb: knocks out the electronics of everything within this radius (0 = none), for up to this many seconds.</summary>
    [Export] public float EmpRadius;
    [Export] public float EmpTime = 14f;
    /// <summary>How hard it jolts a player's machine at the point of burst (less with distance, nothing beyond ShakeRadius).</summary>
    [Export] public float Shake;
    [Export] public float ShakeRadius = 150f;
    /// <summary>
    /// The "Whistle" sound child starts this many seconds before it lands (a shell, long in the air);
    /// 0 = as it leaves the rack (a bomb, set to play by itself).
    /// </summary>
    [Export] public float WhistleLead;
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
    bool _whistled;
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
        if (WhistleLead > 0f && !_whistled && _velocity.Y < 0f && TimeToGround <= WhistleLead && GetNodeOrNull<AudioStreamPlayer3D>("Whistle") is { } whistle)
        {
            _whistled = true;
            whistle.Play();
        }
        _velocity.Y -= Gravity * dt;
        if (Drag > 0f) _velocity *= Mathf.Exp(-Drag * dt);
        Vector3 from = GlobalPosition;
        Vector3 to = from + _velocity * dt;
        var space = GetWorld3D().DirectSpaceState;
        if (Phys.Ray(space, from, to, _mask, _exclude, out var hit))
        {
            if (BlastRadius > 0f) Phys.Blast(space, hit.Position, BlastRadius, _damage, _mask, _exclude);
            Score.Attacker = Shooter;
            if (DirectHit > 0f) Phys.Damage(hit, DirectHit * _damage);
            Score.Attacker = null;
            Impacts.Ground(GetParent(), hit, CraterSize, MarkKind, CraterDepth);
            Impacts.Unit(hit, CraterSize, MarkKind);
            if (EmpRadius > 0f) Emp.Pulse(hit.Position, EmpRadius, EmpTime);
            if (Shake > 0f && Game.PlayerUnit is IDrivable near)
                near.Shake += Shake * Mathf.Clamp(1f - near.GlobalPosition.DistanceTo(hit.Position) / ShakeRadius, 0f, 1f);
            if (ImpactScene != null)
            {
                var fx = ImpactScene.Instantiate<Node3D>();
                if (fx is NukeBlast nuke)
                {
                    nuke.Damage = _damage;
                    nuke.Shooter = Shooter;
                }
                // Placed before it enters the scene: a nuclear blast works out where ground zero is as it arrives.
                Node parent = GetParent();
                fx.Position = parent is Node3D world ? world.ToLocal(hit.Position) : hit.Position;
                parent.AddChild(fx);
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
