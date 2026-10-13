using Godot;

namespace MechSim;

/// <summary>
/// The chat of a network game on the HUD: the last few lines (they fade after a while), and a line to
/// type in. Enter opens it and sends what was typed; Esc closes it. While it is open the keys are text,
/// not commands.
/// </summary>
public partial class ChatBox : Control
{
    /// <summary>How many lines are shown, and for how long after the last message.</summary>
    [Export] public int Lines = 6;
    [Export] public float ShowTime = 9f;

    Label _log;
    LineEdit _edit;
    Label _hint;
    float _shown;

    public override void _Ready()
    {
        _log = GetNode<Label>("Log");
        _edit = GetNode<LineEdit>("Edit");
        _edit.Visible = false;
        _hint = GetNodeOrNull<Label>("Hint");
        _edit.TextSubmitted += text =>
        {
            NetLink.I.Chat(text);
            Close();
        };
        NetLink.I.ChatChanged += Refresh;
        TreeExiting += () =>
        {
            NetLink.I.ChatChanged -= Refresh;
            Game.Typing = false;
        };
        Refresh();
        _shown = 0f;
    }

    void Refresh()
    {
        var lines = NetLink.I.ChatLines;
        _log.Text = Loc.T(string.Join("\n", lines.GetRange(Mathf.Max(0, lines.Count - Lines), Mathf.Min(Lines, lines.Count))));
        _shown = ShowTime;
    }

    void Close()
    {
        _edit.Text = "";
        _edit.Visible = false;
        _edit.ReleaseFocus();
        Game.Typing = false;
    }

    public override void _Process(double delta)
    {
        Visible = Net.Online;
        _shown -= (float)delta;
        _log.Modulate = new Color(1f, 1f, 1f, _edit.Visible ? 1f : Mathf.Clamp(_shown, 0f, 1f));
        if (_hint != null) _hint.Visible = !_edit.Visible;
    }

    public override void _Input(InputEvent @event)
    {
        if (!Net.Online || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (!_edit.Visible && key.PhysicalKeycode is Key.Enter or Key.KpEnter && !_edit.Visible)
        {
            _edit.Visible = true;
            _edit.GrabFocus();
            Game.Typing = true;
            GetViewport().SetInputAsHandled();
        }
        else if (_edit.Visible && key.PhysicalKeycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
