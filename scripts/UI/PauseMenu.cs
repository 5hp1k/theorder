using Godot;

namespace MechSim;

/// <summary>In-battle pause panel (Esc), also shown when the clock runs out. The buttons are in game.tscn;
/// "Настройки" swaps them for the settings page (settings_panel.tscn).</summary>
public partial class PauseMenu : Control
{
    Label _title;
    Button _resume;
    Control _buttons, _settings;

    public override void _Ready()
    {
        _title = GetNode<Label>("%PauseTitle");
        _resume = GetNode<Button>("%BtnResume");
        _resume.Pressed += () => Game.Instance.SetPaused(false);
        GetNode<Button>("%BtnRestart").Pressed += () =>
        {
            GetTree().Paused = false;
            GetTree().ReloadCurrentScene();
        };
        GetNode<Button>("%BtnMenu").Pressed += () => Game.Instance.QuitToMenu();
        // Settings: the same page as in the main menu, shown in place of the buttons.
        _buttons = GetNode<Control>("Center/Box");
        _settings = GetNode<Control>("%SettingsBox");
        GetNode<Button>("%BtnSettings").Pressed += () => ShowSettings(true);
        GetNode<Button>("%BtnSettingsBack").Pressed += () => ShowSettings(false);
        VisibilityChanged += Refresh;
    }

    void ShowSettings(bool on)
    {
        _settings.Visible = on;
        _buttons.Visible = !on;
    }

    void Refresh()
    {
        ShowSettings(false);
        bool over = Game.Instance is { TimeUp: true };
        _title.Text = Loc.T(over ? $"ВРЕМЯ ВЫШЛО\nУничтожено: {Game.Kills}" : "ПАУЗА");
        _resume.Visible = !over;
    }
}
