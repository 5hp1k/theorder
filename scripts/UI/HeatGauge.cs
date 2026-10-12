using Godot;

namespace MechSim;

/// <summary>
/// Right-hand gauge. For a mech: reactor heat with the warning mark, full = shutdown.
/// For anything without a reactor: how much of the hull is left.
/// </summary>
public partial class HeatGauge : HudWidget
{
    protected override void Paint()
    {
        PanelBackground();
        const float w = 22f;
        var topLeft = new Vector2(Size.X - w - 20f, 30f);
        float height = Size.Y - 70f;
        DrawRect(new Rect2(topLeft.X, topLeft.Y, w, height), Dim, false, 1.5f);

        if (!Seat.HasHeat)
        {
            float hull = Seat.Integrity;
            Color hc = hull > 0.5f ? Green : hull > 0.25f ? Amber : Red;
            DrawRect(new Rect2(topLeft.X + 3f, topLeft.Y + height * (1f - hull), w - 6f, height * hull), hc);
            Text("КОРПУС", new Vector2(topLeft.X - 34f, topLeft.Y - 10f), 14, Green);
            Text($"{Mathf.RoundToInt(hull * 100f)}%", new Vector2(topLeft.X - 34f, topLeft.Y + height + 24f), 16, hc);
            return;
        }

        float h = Mathf.Clamp(Seat.Heat, 0f, 1f);
        Color col = h < 0.6f ? Green : h < 0.85f ? Amber : Red;
        DrawRect(new Rect2(topLeft.X + 3f, topLeft.Y + height * (1f - h), w - 6f, height * h), col);
        float warnY = topLeft.Y + height * 0.15f;
        DrawLine(new Vector2(topLeft.X - 4f, warnY), new Vector2(topLeft.X + w + 4f, warnY), Amber, 1f);

        Text("ТЕПЛО", new Vector2(topLeft.X - 30f, topLeft.Y - 10f), 14, Green);
        Text($"{Mathf.RoundToInt(Seat.Heat * 100f)}%", new Vector2(topLeft.X - 34f, topLeft.Y + height + 24f), 16, col);
    }
}
