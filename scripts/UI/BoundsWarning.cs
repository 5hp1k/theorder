using Godot;

namespace MechSim;

/// <summary>The player has left the battlefield: how long until the machine is lost.</summary>
public partial class BoundsWarning : HudWidget
{
    protected override void Paint()
    {
        float left = Boundary.TimeLeft(Game.PlayerUnit);
        if (left < 0f || Seat.IsDestroyed) return;
        float w = Size.X;
        DrawRect(new Rect2(0f, 0f, w, Size.Y), new Color(0.25f, 0f, 0f, 0.35f));
        DrawRect(new Rect2(0f, 0f, w, Size.Y), Red, false, 2f);
        if (Mathf.PosMod(Clock, 0.6f) < 0.4f) Text("ВНЕ ЗОНЫ БОЯ", new Vector2(0f, 30f), 26, Red, HorizontalAlignment.Center, w);
        Text($"Вернитесь: самоликвидация через {Mathf.CeilToInt(left)} с", new Vector2(0f, 58f), 17, Amber, HorizontalAlignment.Center, w);
    }
}
