using Godot;

namespace MechSim;

/// <summary>Vertical bar: fill = actual speed, arrow = throttle setting, line = zero detent.</summary>
public partial class ThrottleGauge : HudWidget
{
    protected override void Paint()
    {
        PanelBackground();
        const float w = 22f;
        var topLeft = new Vector2(6f, 30f);
        float height = Size.Y - 70f;
        float rev = Seat.ReverseFraction;
        float total = 1f + rev;
        float zeroY = topLeft.Y + height * (1f / total);
        float Y(float t) => zeroY - Mathf.Clamp(t, -rev, 1f) * height / total;

        DrawRect(new Rect2(topLeft.X, topLeft.Y, w, height), Dim, false, 1.5f);

        float s = Seat.SpeedFraction;
        float y0 = Y(0f), y1 = Y(s);
        DrawRect(new Rect2(topLeft.X + 3f, Mathf.Min(y0, y1), w - 6f, Mathf.Abs(y1 - y0)), s >= 0f ? Green : Amber);

        DrawLine(new Vector2(topLeft.X - 4f, zeroY), new Vector2(topLeft.X + w + 4f, zeroY), Green, 2f);
        float ty = Y(Seat.Throttle);
        var tip = new Vector2(topLeft.X + w + 2f, ty);
        DrawColoredPolygon(new[] { tip, tip + new Vector2(12f, -7f), tip + new Vector2(12f, 7f) }, Amber);

        Text("ГАЗ", new Vector2(topLeft.X, topLeft.Y - 10f), 14, Green);
        Text($"{Mathf.RoundToInt(Seat.Throttle * 100f)}%", new Vector2(topLeft.X + w + 16f, ty + 5f), 13, Amber);
        Text($"{Mathf.RoundToInt(Seat.SpeedKmh)} км/ч", new Vector2(topLeft.X, topLeft.Y + height + 24f), 16, Green);
    }
}
