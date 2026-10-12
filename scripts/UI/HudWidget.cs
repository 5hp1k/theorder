using Godot;

namespace MechSim;

/// <summary>
/// Base for HUD instruments: a Control that redraws itself every frame inside its own rectangle,
/// so its place and size are whatever the scene says.
/// </summary>
public abstract partial class HudWidget : Control
{
    protected Hud Hud { get; private set; }
    /// <summary>The seat the player is in; never null inside Paint().</summary>
    protected ICockpit Seat => Hud.Seat;

    protected Color Green => Hud.Green;
    protected Color Dim => Hud.Dim;
    protected Color Amber => Hud.Amber;
    protected Color Red => Hud.Red;
    protected Color Panel => Hud.Panel;

    Font _font;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        Node n = GetParent();
        while (n != null && n is not MechSim.Hud) n = n.GetParent();
        Hud = (MechSim.Hud)n;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Hud?.Seat != null) Paint();
    }

    /// <summary>Draw in local coordinates: (0,0) is the control's top-left, Size its extent.</summary>
    protected abstract void Paint();

    protected void Text(string s, Vector2 pos, int size, Color c, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
        => DrawString(_font, pos, s, align, width, size, c);

    protected void PanelBackground() => DrawRect(new Rect2(Vector2.Zero, Size), Panel);
}
