using Godot;

namespace MechSim;

/// <summary>Strip showing turret/torso twist: centre = where the guns point, the marker = where the hull points.</summary>
public partial class TorsoIndicator : HudWidget
{
    protected override void Paint()
    {
        PanelBackground();
        float mid = Size.X / 2f, y = 22f;
        float half = mid - 8f;
        DrawLine(new Vector2(mid - half, y), new Vector2(mid + half, y), Dim, 2f);
        for (int i = -4; i <= 4; i++)
        {
            float x = mid + i * half / 4f;
            DrawLine(new Vector2(x, y - 5f), new Vector2(x, y + 5f), Dim, 1f);
        }
        DrawLine(new Vector2(mid, y - 12f), new Vector2(mid, y + 12f), Green, 2f);
        Text($"КУРС {Mathf.RoundToInt(Seat.HeadingDeg):000}°", new Vector2(mid - 60f, y - 8f), 14, Green, HorizontalAlignment.Center, 120f);

        if (Seat.MaxTwist <= 0.01f) return;
        // Turret turned right (yaw < 0) means the hull points to the left of the view.
        var p = new Vector2(mid + Seat.Twist / Seat.MaxTwist * half, y + 8f);
        DrawColoredPolygon(new[] { p, p + new Vector2(-8f, 12f), p + new Vector2(8f, 12f) }, Amber);
        Text("КОРПУС", p + new Vector2(-30f, 28f), 12, Amber, HorizontalAlignment.Center, 60f);
    }
}
