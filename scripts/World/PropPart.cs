using Godot;

namespace MechSim;

/// <summary>
/// One breakable piece of a prop: a tree's crown tier, a cactus arm, an oil rig's derrick. It has its
/// own collision and strength, so shooting it breaks off that piece (and whatever it carries) and leaves
/// the rest standing as cover. A vital piece (a trunk) takes the whole prop with it.
/// Lives under a <see cref="Destructible"/> root.
/// </summary>
public partial class PropPart : StaticBody3D, IDamageable
{
    [Export] public float Hp = 12f;
    /// <summary>Other pieces that rest on this one and go when it goes.</summary>
    [Export] public Godot.Collections.Array<NodePath> Carries = new();
    /// <summary>Losing this piece destroys the whole prop (a tree without its trunk falls).</summary>
    [Export] public bool Vital;
    /// <summary>Effect spawned where the piece breaks off.</summary>
    [Export] public PackedScene BreakFx;
    /// <summary>Weight of the piece once it has come off, kg.</summary>
    [Export] public float Mass = 400f;
    /// <summary>Seconds the broken-off piece lies around before it is cleared away.</summary>
    [Export] public float LieTime = 25f;

    public bool Broken { get; private set; }

    public void TakeDamage(float amount, Vector3 point)
    {
        if (Broken) return;
        Hp -= amount;
        if (Hp > 0f) return;

        if (Vital && GetParent() is Destructible whole)
        {
            Vector3 away = GlobalPosition - point;
            away.Y = 0f;
            whole.DestroyWhole(away);
            return;
        }
        BreakOff();
        foreach (NodePath path in Carries)
            (GetNodeOrNull(path) as PropPart)?.BreakOff();
    }

    public void BreakOff()
    {
        if (Broken) return;
        Broken = true;
        CollisionLayer = 0;
        Node scene = GetTree().CurrentScene;
        Vector3 at = Center;
        Burning.Ignite(scene, at, 0.8f);
        if (BreakFx != null)
        {
            var fx = BreakFx.Instantiate<Node3D>();
            scene.AddChild(fx);
            fx.GlobalPosition = at;
        }
        // The piece itself comes away and falls, then lies there a while.
        var shove = new Vector3((float)GD.RandRange(-2.0, 2.0), (float)GD.RandRange(0.5, 2.5), (float)GD.RandRange(-2.0, 2.0));
        Debris.Detach(this, scene, shove, Mass, LieTime);
    }

    /// <summary>Leaves the piece in place but takes it out of the way of shots (the prop is going down whole).</summary>
    public void Disable()
    {
        Broken = true;
        CollisionLayer = 0;
    }

    Vector3 Center
    {
        get
        {
            foreach (Node child in GetChildren())
                if (child is CollisionShape3D shape) return shape.GlobalPosition;
            return GlobalPosition;
        }
    }
}
