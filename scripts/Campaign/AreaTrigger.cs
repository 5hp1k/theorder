using Godot;

namespace MechSim;

/// <summary>When a machine comes within a radius of this node.</summary>
public partial class AreaTrigger : Trigger
{
    public enum Whom { Player, PlayerSide, Enemy }

    [Export] public float Radius = 60f;
    [Export] public Whom Who = Whom.Player;

    protected override bool Test()
    {
        foreach (var unit in Units.All)
        {
            if (unit.IsDestroyed) continue;
            bool counts = Who switch
            {
                Whom.Player => Game.IsHuman(unit),
                Whom.PlayerSide => unit.Team == Team.Player,
                _ => unit.Team == Team.Enemy,
            };
            if (!counts) continue;
            Vector3 d = unit.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.Length() <= Radius) return true;
        }
        return false;
    }
}
