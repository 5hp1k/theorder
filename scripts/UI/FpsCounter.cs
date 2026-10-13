using Godot;

namespace MechSim;

/// <summary>Frames per second and the time of a frame, in a corner of the screen. Shown by the setting or with F3.</summary>
public partial class FpsCounter : Label
{
    float _wait;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Visible = GameConfig.ShowFps;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("toggle_fps")) return;
        GameConfig.ShowFps = !GameConfig.ShowFps;
        GameConfig.Save();
    }

    public override void _Process(double delta)
    {
        Visible = GameConfig.ShowFps;
        if (!Visible) return;
        _wait -= (float)delta;
        if (_wait > 0f) return;
        _wait = 0.25f;
        double fps = Engine.GetFramesPerSecond();
        Text = $"{fps:0} к/с  ·  {(fps > 0 ? 1000.0 / fps : 0):0.0} мс";
    }
}
