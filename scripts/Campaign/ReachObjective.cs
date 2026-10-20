using Godot;

namespace MechSim;

/// <summary>Get to a place (this node; the HUD marks it) with the player's machine.</summary>
public partial class ReachObjective : Objective
{
    /// <summary>How near counts as there, metres (across the ground).</summary>
    [Export] public float Radius = 40f;

    public override bool Marked => true;

    protected override void Check(float dt)
    {
        // Any of the players (in a network game, whoever gets there).
        foreach (var unit in Units.All)
        {
            if (unit.IsDestroyed || !Game.IsHuman(unit)) continue;
            Vector3 d = unit.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.Length() <= Radius)
            {
                Complete();
                return;
            }
        }
    }
}
