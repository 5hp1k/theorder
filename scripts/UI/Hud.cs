using Godot;

namespace MechSim;

/// <summary>
/// Root of hud.tscn. Each instrument is its own Control in that scene (move and resize them there) and
/// shows whatever seat the player is in (<see cref="Game.Cockpit"/>): a mech, a vehicle, or on foot.
/// This node holds the shared palette and the few plain labels.
/// </summary>
public partial class Hud : Control
{
    [ExportGroup("Palette")]
    [Export] public Color Green = new(0.45f, 1f, 0.55f, 0.9f);
    [Export] public Color Dim = new(0.45f, 1f, 0.55f, 0.35f);
    [Export] public Color Amber = new(1f, 0.75f, 0.2f, 0.95f);
    [Export] public Color Red = new(1f, 0.25f, 0.2f, 0.95f);
    [Export] public Color Panel = new(0f, 0.05f, 0.02f, 0.45f);

    public ICockpit Seat => Game.Cockpit;
    public float Time { get; private set; }

    Label _view, _timer, _unit;
    // Instruments that a cockpit shows on its own screens; hidden here while it does.
    static readonly string[] PanelInstruments = { "Minimap", "HeatGauge", "ThrottleGauge", "JumpGauge", "WeaponList", "PlayerDoll", "ArmorLabel" };
    readonly System.Collections.Generic.List<Control> _panel = new();
    Control _help, _helpHint;

    public override void _Ready()
    {
        _view = GetNode<Label>("ViewLabel");
        _timer = GetNode<Label>("TimerLabel");
        _unit = GetNode<Label>("ArmorLabel");
        _help = GetNode<Control>("HelpPanel");
        _helpHint = GetNode<Control>("HelpHint");
        _helpHint.Visible = !_help.Visible;
        foreach (string name in PanelInstruments)
            if (GetNodeOrNull<Control>(name) is { } widget) _panel.Add(widget);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("toggle_help")) return;
        _help.Visible = !_help.Visible;
        _helpHint.Visible = !_help.Visible;
    }

    public override void _Process(double delta)
    {
        Time += (float)delta;

        var game = Game.Instance;
        _timer.Visible = game != null && game.TimeLimited;
        if (_timer.Visible)
        {
            int left = Mathf.CeilToInt(game.TimeLeft);
            _timer.Text = Loc.T($"{left / 60}:{left % 60:00}");
        }

        bool overlay = !CockpitDisplays.Active;
        foreach (Control widget in _panel) widget.Visible = overlay;

        var seat = Seat;
        if (seat == null) return;
        _unit.Text = Loc.T(seat.UnitName).ToUpper();
        _view.Text = Loc.T(seat.FirstPerson ? "ОТ ПЕРВОГО ЛИЦА" : "ВНЕШНИЙ ВИД");
    }
}
