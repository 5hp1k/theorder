using Godot;

namespace MechSim;

/// <summary>Hold out for a time after it is given.</summary>
public partial class SurviveObjective : Objective
{
    [Export] public float Seconds = 120f;

    public override string Progress
    {
        get
        {
            int left = Mathf.CeilToInt(Mathf.Max(0f, Seconds - Age));
            return $"{left / 60}:{left % 60:00}";
        }
    }

    protected override void Check(float dt)
    {
        if (Age >= Seconds) Complete();
    }
}
