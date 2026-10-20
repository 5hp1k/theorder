using Godot;

namespace MechSim;

/// <summary>
/// The mission editor's camera: flies over the map. Right mouse button held — look round; W A S D (or
/// the arrows) — fly, Q / E — down and up, Shift — faster; the wheel — forward and back along the view.
/// <see cref="Focus"/> brings it round to look at a point.
/// </summary>
public partial class EditorCamera : Camera3D
{
    [Export] public float Speed = 60f;
    [Export] public float FastFactor = 4f;
    [Export] public float LookSpeed = 0.004f;
    [Export] public float WheelStep = 25f;

    float _yaw, _pitch = -0.7f;
    /// <summary>While false (typing in a box, a dialog open) the keys don't fly it.</summary>
    public bool KeysActive = true;

    public override void _Ready()
    {
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }

    public float Yaw => _yaw;
    public float Pitch => _pitch;

    public void Look(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = Mathf.Clamp(pitch, -1.55f, 1.55f);
        Rotation = new Vector3(_pitch, _yaw, 0f);
    }

    /// <summary>Looks at a point from a little way off and above.</summary>
    public void Focus(Vector3 point, float distance = 60f)
    {
        Vector3 back = new Basis(Vector3.Up, _yaw) * new Vector3(0f, 0f, 1f);
        GlobalPosition = point + back * distance * 0.8f + Vector3.Up * distance * 0.6f;
        Vector3 to = point - GlobalPosition;
        Look(Mathf.Atan2(-to.X, -to.Z), Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Right) != 0)
        {
            Look(_yaw - motion.Relative.X * LookSpeed, _pitch - motion.Relative.Y * LookSpeed);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true } wheel && !wheel.ShiftPressed && !wheel.CtrlPressed
                 && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            float step = WheelStep * (Input.IsKeyPressed(Key.Shift) ? FastFactor : 1f) * (wheel.ButtonIndex == MouseButton.WheelUp ? 1f : -1f);
            GlobalPosition += -GlobalBasis.Z * step;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!KeysActive || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return;
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1f;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1f;
        float vertical = (Input.IsKeyPressed(Key.E) ? 1f : 0f) - (Input.IsKeyPressed(Key.Q) ? 1f : 0f);
        if (move == Vector3.Zero && vertical == 0f) return;
        float speed = Speed * (Input.IsKeyPressed(Key.Shift) ? FastFactor : 1f) * (float)delta;
        // Flying level: forward is where it looks, along the ground.
        var flat = new Basis(Vector3.Up, _yaw);
        GlobalPosition += flat * move.Normalized() * speed + Vector3.Up * vertical * speed;
    }
}
