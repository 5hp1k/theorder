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
    /// <summary>
    /// How much of the interface is up, switched round with key I: all of it; in a cockpit, only what its
    /// own screens don't show (outside, the same as all of it); nothing. Stays so for the rest of the session.
    /// </summary>
    public enum Mode { Full, Cockpit, Off }
    public static Mode Shown;
    public static bool Off => Shown == Mode.Off;
    public float Time { get; private set; }

    Label _view, _timer, _unit;
    // Instruments that a cockpit shows on its own screens; hidden here while it does, if so switched.
    static readonly string[] PanelInstruments = { "Minimap", "HeatGauge", "ThrottleGauge", "JumpGauge", "WeaponList", "PlayerDoll", "ArmorLabel" };
    readonly System.Collections.Generic.List<Control> _panel = new();
    Control _help, _helpHint;

    public static Hud Instance { get; private set; }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Shown unless switched off, in a cutscene or while the console's free camera flies.</summary>
    public void RefreshShown()
    {
        bool shown = !Off && !StoryOverlay.InCutscene && FreeCamera.Current == null;
        if (Visible != shown) Visible = shown;
    }

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
        // The water on the lens is not part of the interface: it stays when the rest is switched off.
        // (Taken out once the scene is set up: not while its parent is still adding children.)
        Callable.From(() =>
        {
            if (GetNodeOrNull<Control>("WaterOverlay") is not { } water || GetParent() is not { } layer) return;
            water.Reparent(layer, false);
            layer.MoveChild(water, GetIndex());
        }).CallDeferred();
        Visible = !Off;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_hud"))
        {
            Shown = Shown switch
            {
                Mode.Full => CockpitDisplays.Active ? Mode.Cockpit : Mode.Off,
                Mode.Cockpit => Mode.Off,
                _ => Mode.Full,
            };
            Visible = !Off;
            return;
        }
        if (!@event.IsActionPressed("toggle_help")) return;
        _help.Visible = !_help.Visible;
        _helpHint.Visible = !_help.Visible;
    }

    public override void _Process(double delta)
    {
        Time += (float)delta;
        // A cutscene hides the interface (the story's own layer stays: subtitles, letterbox).
        RefreshShown();

        var game = Game.Instance;
        _timer.Visible = game != null && game.TimeLimited;
        if (_timer.Visible)
        {
            int left = Mathf.CeilToInt(game.TimeLeft);
            _timer.Text = Loc.T($"{left / 60}:{left % 60:00}");
        }

        bool overlay = !CockpitDisplays.Active || Shown == Mode.Full;
        foreach (Control widget in _panel) widget.Visible = overlay;

        var seat = Seat;
        if (seat == null) return;
        _unit.Text = Loc.T(seat.UnitName).ToUpper();
        _view.Text = Loc.T(seat.FirstPerson ? "ОТ ПЕРВОГО ЛИЦА" : "ВНЕШНИЙ ВИД");
    }
}
