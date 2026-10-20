using Godot;

namespace MechSim;

/// <summary>
/// Sets steps going when something happens. Armed from the start, or later when a step names it; it
/// fires once (or, with Once off, every time it happens again after it was armed again).
/// </summary>
public abstract partial class Trigger : Node3D, IStep
{
    [Export] public bool StartArmed = true;
    [Export] public bool Once = true;
    /// <summary>What it sets going (paths of objectives, triggers, machines, cutscenes, radio lines, endings).</summary>
    [Export] public Godot.Collections.Array<NodePath> Steps = new();

    public bool Armed { get; private set; }
    /// <summary>Seconds since it was armed.</summary>
    protected float Age { get; private set; }
    bool _fired;

    public void Run() => Arm();

    public void Arm()
    {
        if (Once && _fired) return;
        Armed = true;
        Age = 0f;
    }

    public void Tick(float dt)
    {
        if (!Armed) return;
        Age += dt;
        if (Test()) Fire();
    }

    protected abstract bool Test();

    public void Fire()
    {
        _fired = true;
        Armed = false;
        Mission.Run(this, Steps);
    }
}
