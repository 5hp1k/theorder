using Godot;

namespace MechSim;

/// <summary>
/// The console's free camera (scenes/fx/free_camera.tscn, "freecam"): flies anywhere, apart from the
/// player's machine, which stands meanwhile. W/A/S/D move, Q/E down and up, Shift faster, Alt slower,
/// the wheel sets the speed, the mouse looks round. It keeps flying with time stopped ("timestop").
/// The HUD is hidden while it is on: its sights are drawn for the machine's own camera.
/// </summary>
public partial class FreeCamera : Camera3D
{
    /// <summary>Metres a second.</summary>
    [Export] public float Speed = 20f;
    [Export] public float FastFactor = 4f;
    [Export] public float SlowFactor = 0.25f;
    /// <summary>Radians per pixel of mouse movement.</summary>
    [Export] public float Sensitivity = 0.003f;

    public static FreeCamera Current { get; private set; }

    float _yaw, _pitch;
    ulong _last;

    public override void _Ready()
    {
        Current = this;
        Vector3 e = GlobalBasis.GetEuler();
        _pitch = e.X;
        _yaw = e.Y;
        MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _last = Time.GetTicksUsec();
        // (at once: with time stopped the HUD does not update itself)
        Hud.Instance?.RefreshShown();
    }

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
        Hud.Instance?.RefreshShown();
    }

    /// <summary>Not while the console is open or the pause menu is up.</summary>
    static bool Busy => Game.Typing || Game.Instance is { Paused: true };

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Busy) return;
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * Sensitivity;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * Sensitivity, -1.5f, 1.5f);
        }
        else if (@event is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp) Speed *= 1.2f;
            else if (wheel.ButtonIndex == MouseButton.WheelDown) Speed /= 1.2f;
        }
    }

    public override void _Process(double delta)
    {
        // Real time, not the game's: it flies with time stopped or slowed.
        ulong now = Time.GetTicksUsec();
        float dt = Mathf.Min((now - _last) / 1e6f, 0.1f);
        _last = now;
        Rotation = new Vector3(_pitch, _yaw, 0f);
        if (Busy) return;
        Vector3 move = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) move.Z -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S)) move.Z += 1f;
        if (Input.IsPhysicalKeyPressed(Key.A)) move.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D)) move.X += 1f;
        if (Input.IsPhysicalKeyPressed(Key.E)) move.Y += 1f;
        if (Input.IsPhysicalKeyPressed(Key.Q)) move.Y -= 1f;
        if (move == Vector3.Zero) return;
        float speed = Speed * (Input.IsPhysicalKeyPressed(Key.Shift) ? FastFactor : 1f) * (Input.IsPhysicalKeyPressed(Key.Alt) ? SlowFactor : 1f);
        GlobalPosition += GlobalBasis * move.Normalized() * speed * dt;
    }
}
