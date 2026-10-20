using Godot;

namespace MechSim;

/// <summary>
/// "Missile coming": shown, with a beeping alarm (the "Beep" player, looped), while a missile locked on the
/// player's machine is in the air. An arrow round the ring points at the nearest one.
/// </summary>
public partial class MissileWarning : HudWidget
{
    /// <summary>Radius of the ring the arrow runs round.</summary>
    [Export] public float ArrowRing = 34f;

    AudioStreamPlayer _beep;

    public override void _Ready()
    {
        base._Ready();
        _beep = GetNodeOrNull<AudioStreamPlayer>("Beep");
    }

    /// <summary>The nearest missile homing on the player, and how many there are.</summary>
    static Missile Threat(out int count)
    {
        count = 0;
        Missile nearest = null;
        if (Game.PlayerUnit is not Node me || Game.PlayerUnit.IsDestroyed) return null;
        float best = float.MaxValue;
        Vector3 at = Game.PlayerUnit.AimCenter;
        foreach (var m in Missile.Flying)
        {
            if (!IsInstanceValid(m) || !m.Interceptable || m.Target is not Node target || target != me) continue;
            count++;
            float d = m.GlobalPosition.DistanceSquaredTo(at);
            if (d < best)
            {
                best = d;
                nearest = m;
            }
        }
        return nearest;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // (Heard even with the interface switched off.)
        bool on = Threat(out _) != null && !GetTree().Paused;
        if (_beep == null || OnScreen || _beep.Playing == on) return;
        if (on) _beep.Play();
        else _beep.Stop();
    }

    protected override void Paint()
    {
        var missile = Threat(out int count);
        if (missile == null) return;
        float w = Size.X;
        bool blink = Mathf.PosMod(Clock, 0.4f) < 0.26f;
        DrawRect(new Rect2(0f, 0f, w, Size.Y), new Color(0.25f, 0f, 0f, 0.3f));
        DrawRect(new Rect2(0f, 0f, w, Size.Y), Red, false, 2f);
        if (blink) Text(count > 1 ? $"РАКЕТ НА ВАС: {count}" : "РАКЕТА НА ВАС", new Vector2(0f, 30f), 26, Red, HorizontalAlignment.Center, w);

        // The arrow, towards the nearest one as the pilot looks.
        var cam = Seat?.Camera;
        if (cam == null) return;
        Vector3 local = cam.GlobalTransform.AffineInverse() * new Vector3(missile.GlobalPosition.X, cam.GlobalPosition.Y, missile.GlobalPosition.Z);
        var dir = new Vector2(local.X, local.Z);
        dir = dir.LengthSquared() < 1e-4f ? Vector2.Up : dir.Normalized();
        var c = new Vector2(w * 0.5f, Size.Y + ArrowRing + 10f);
        DrawArc(c, ArrowRing, 0f, Mathf.Tau, 36, new Color(Red, 0.5f), 1.5f, true);
        Vector2 tip = c + dir * (ArrowRing + 8f), side = new Vector2(-dir.Y, dir.X) * 9f;
        DrawColoredPolygon(new[] { tip, tip - dir * 20f + side, tip - dir * 20f - side }, Red);
    }
}
