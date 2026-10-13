using Godot;

namespace MechSim;

/// <summary>
/// Base for HUD instruments: a Control that redraws itself every frame inside its own rectangle,
/// so its place and size are whatever the scene says. The same instruments also run on the screens
/// of a cockpit (see CockpitDisplays), where there is no Hud above them: then they use its stock colours.
/// </summary>
public abstract partial class HudWidget : Control
{
    protected Hud Hud { get; private set; }
    /// <summary>The seat the player is in; never null inside Paint().</summary>
    protected ICockpit Seat => Game.Cockpit;

    protected Color Green => Hud?.Green ?? new Color(0.45f, 1f, 0.55f, 0.9f);
    protected Color Dim => Hud?.Dim ?? new Color(0.45f, 1f, 0.55f, 0.35f);
    protected Color Amber => Hud?.Amber ?? new Color(1f, 0.75f, 0.2f, 0.95f);
    protected Color Red => Hud?.Red ?? new Color(1f, 0.25f, 0.2f, 0.95f);
    protected Color Panel => Hud?.Panel ?? new Color(0f, 0.05f, 0.02f, 0.45f);
    /// <summary>Seconds since the instrument came on, for blinking.</summary>
    protected float Clock => Godot.Time.GetTicksMsec() * 0.001f;

    /// <summary>Seconds the player's electronics stay dead after an electromagnetic pulse; 0 = working.</summary>
    protected static float Jam => Emp.Left(Game.PlayerUnit);

    /// <summary>What a dead screen shows: snow, and a line saying why.</summary>
    protected void Static(Rect2 area, int grains = 90)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(Clock * 24f) + 1 };
        for (int i = 0; i < grains; i++)
        {
            var at = area.Position + new Vector2(rng.Randf() * area.Size.X, rng.Randf() * area.Size.Y);
            DrawRect(new Rect2(at, new Vector2(rng.RandfRange(2f, 14f), 1.5f)), new Color(Dim, rng.RandfRange(0.15f, 0.7f)));
        }
    }

    Font _font;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        Node n = GetParent();
        while (n != null && n is not MechSim.Hud) n = n.GetParent();
        Hud = n as MechSim.Hud;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Seat != null) Paint();
    }

    /// <summary>Draw in local coordinates: (0,0) is the control's top-left, Size its extent.</summary>
    protected abstract void Paint();

    protected void Text(string s, Vector2 pos, int size, Color c, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
        => DrawString(_font, pos, Loc.T(s), align, width, size, c);

    protected void PanelBackground() => DrawRect(new Rect2(Vector2.Zero, Size), Panel);
}
