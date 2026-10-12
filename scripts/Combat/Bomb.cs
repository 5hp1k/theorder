using Godot;

namespace MechSim;

/// <summary>
/// Free-fall bomb: keeps the bomber's speed, drops under gravity and hurts everything within its blast
/// radius, less towards the edge. The looks and numbers are in scenes/fx/bomb.tscn.
/// </summary>
public partial class Bomb : Projectile
{
    [Export] public float Gravity = 20f;
    [Export] public float BlastRadius = 14f;
    /// <summary>Radius of the crater it leaves on the ground.</summary>
    [Export] public float CraterSize = 5f;
    /// <summary>Spawned where it lands.</summary>
    [Export] public PackedScene ImpactScene;

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
        Vector3 from = GlobalPosition;
        Vector3 to = from + _velocity * dt;
        var space = GetWorld3D().DirectSpaceState;
        if (Phys.Ray(space, from, to, _mask, _exclude, out var hit))
        {
            Phys.Blast(space, hit.Position, BlastRadius, _damage, _mask, _exclude);
            Impacts.Ground(GetParent(), hit, CraterSize);
            if (ImpactScene != null)
            {
                var fx = ImpactScene.Instantiate<Node3D>();
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
