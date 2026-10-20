using Godot;

namespace MechSim;

/// <summary>
/// The mission's objectives on the HUD: those given so far, each with its state and progress; and a
/// marker over every place the player is sent to, with the distance to it.
/// </summary>
public partial class ObjectiveList : HudWidget
{
    [Export] public int FontSize = 15;
    [Export] public float LineHeight = 20f;
    /// <summary>Size of the marker over a place to go.</summary>
    [Export] public float MarkerSize = 11f;

    protected override void Paint()
    {
        var mission = Mission.Current;
        // (Out of the way of the squad's orders, which open in the same corner.)
        if (mission == null || !mission.Begun || SquadCommand.Listening) return;
        float y = LineHeight;
        Text("ЦЕЛИ", new Vector2(0f, y), FontSize, Dim);
        foreach (var objective in mission.Objectives)
        {
            if (objective.State == Objective.Status.Waiting) continue;
            y += LineHeight;
            var (mark, colour) = objective.State switch
            {
                Objective.Status.Done => ("✓", Green),
                Objective.Status.Failed => ("✗", Red),
                _ => ("•", objective.Required ? Amber : Dim),
            };
            string line = Loc.T(objective.Text);
            if (objective.State == Objective.Status.Active && objective.Shown != "") line += "  " + objective.Shown;
            if (!objective.Required) line += Loc.T("  (дополнительно)");
            Text(mark + " " + line, new Vector2(0f, y), FontSize, colour);
        }
        Markers(mission);
    }

    void Markers(Mission mission)
    {
        var cam = Seat?.Camera;
        if (cam == null || Game.PlayerUnit == null) return;
        foreach (var objective in mission.Objectives)
        {
            if (!objective.Marked || objective.State != Objective.Status.Active) continue;
            Vector3 at = objective.GlobalPosition + Vector3.Up * 6f;
            if (cam.IsPositionBehind(at)) continue;
            // From the camera's screen to this instrument's own corner.
            Vector2 p = cam.UnprojectPosition(at) - GetGlobalRect().Position;
            float s = MarkerSize;
            DrawPolyline(new[] { p + new Vector2(0, -s), p + new Vector2(s, 0), p + new Vector2(0, s), p + new Vector2(-s, 0), p + new Vector2(0, -s) }, Amber, 2f, true);
            float d = Game.PlayerUnit.GlobalPosition.DistanceTo(objective.GlobalPosition);
            Text($"{Mathf.RoundToInt(d)} м", p + new Vector2(-40f, s + 16f), 13, Amber, HorizontalAlignment.Center, 80f);
        }
    }
}
