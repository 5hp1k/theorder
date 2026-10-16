using Godot;

namespace MechSim;

/// <summary>
/// Whoever drives a mech or a vehicle. Added as a child of the machine; each tick it sets the machine's
/// commands (TurnInput, Throttle, TargetYaw/TargetPitch, Fire). PlayerPilot reads input, AiPilot thinks.
/// </summary>
public abstract partial class MechPilot : Node3D
{
    public IDrivable Unit { get; private set; }

    public virtual void Attach(IDrivable unit) => Unit = unit;

    /// <summary>Set the machine's commands for this tick. Not called while it is destroyed.</summary>
    public abstract void Think(float dt);
    /// <summary>Called every tick after the body has moved, dead or alive (camera goes here).</summary>
    public virtual void UpdateView(float dt) { }
    public virtual bool CanFire => true;
    /// <summary>Override to aim along something other than the machine's own sight.</summary>
    public virtual bool TryGetAimView(out Transform3D view)
    {
        view = default;
        return false;
    }

    public virtual void OnHit() { }
    public virtual void OnKilled() { }
    public virtual void OnRespawn() { }
}
