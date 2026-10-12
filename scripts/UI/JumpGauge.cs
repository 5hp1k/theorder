using Godot;

namespace MechSim;

/// <summary>Slim fuel bar for jump jets or a jetpack; only drawn for units that have them.</summary>
public partial class JumpGauge : HudWidget
{
    protected override void Paint()
    {
        if (!Seat.HasJump) return;
        PanelBackground();
        var bar = new Rect2(6f, 30f, Size.X - 12f, Size.Y - 44f);
        float f = Seat.JumpFuel;
        Text("ПРЫЖ", new Vector2(0f, 20f), 10, Green, HorizontalAlignment.Center, Size.X);
        DrawRect(bar, Dim, false, 1.5f);
        DrawRect(new Rect2(bar.Position.X + 2f, bar.Position.Y + bar.Size.Y * (1f - f), bar.Size.X - 4f, bar.Size.Y * f),
                 Seat.Thrusting ? Amber : Green);
    }
}
