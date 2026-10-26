using Godot;

namespace MechSim;

/// <summary>
/// The coolant left for flushing the heat sinks (key J, or the cab's "ОХЛ" key): a cell for each charge,
/// lit while it is still there. Drawn only for machines that carry coolant.
/// </summary>
public partial class CoolantGauge : HudWidget
{
    protected override void Paint()
    {
        if (Seat.CoolantMax <= 0) return;
        PanelBackground();
        Text("ОХЛ", new Vector2(0f, 20f), 10, Green, HorizontalAlignment.Center, Size.X);
        int max = Seat.CoolantMax, left = Seat.Coolant;
        float top = 30f, gap = 4f, h = (Size.Y - 44f - gap * (max - 1)) / max;
        for (int i = 0; i < max; i++)
        {
            // (the cells fill from the bottom: the top one is spent first)
            var cell = new Rect2(6f, top + (max - 1 - i) * (h + gap), Size.X - 12f, h);
            DrawRect(cell, Dim, false, 1.5f);
            if (i < left) DrawRect(cell.Grow(-2f), left == 1 ? Amber : Green);
        }
    }
}
