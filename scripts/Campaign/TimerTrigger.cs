using Godot;

namespace MechSim;

/// <summary>So many seconds after it is armed (0 with StartArmed: as the mission starts).</summary>
public partial class TimerTrigger : Trigger
{
    [Export] public float Delay;

    protected override bool Test() => Age >= Delay;
}
