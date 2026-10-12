using Godot;

namespace MechSim;

/// <summary>In-battle pause panel (Esc), also shown when the clock runs out. The buttons are in game.tscn.</summary>
public partial class PauseMenu : Control
{
    Label _title;
    Button _resume;

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
        VisibilityChanged += Refresh;
    }

    void Refresh()
    {
        bool over = Game.Instance is { TimeUp: true };
        _title.Text = over ? $"ВРЕМЯ ВЫШЛО\nУничтожено: {Game.Kills}" : "ПАУЗА";
        _resume.Visible = !over;
    }
}
