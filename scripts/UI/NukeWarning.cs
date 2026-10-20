using Godot;

namespace MechSim;

/// <summary>
/// Warning of a nuclear bomb on its way down (see <see cref="Bomb.Warn"/>): how far off it will land, how
/// long is left, and an arrow towards the place — so the pilot knows which way not to go. The child
/// "Siren" (an AudioStreamPlayer with a looping stream) wails for as long as a bomb is in the air.
/// </summary>
public partial class NukeWarning : HudWidget
{
    /// <summary>Beyond this the blast does nothing: the warning says so instead of telling the pilot to run.</summary>
    [Export] public float DangerRadius = 330f;
    /// <summary>Bombs landing further off than this are nobody's business.</summary>
    [Export] public float WarnRadius = 1500f;
    /// <summary>Radius of the ring the arrow runs round.</summary>
    [Export] public float ArrowRing = 46f;

    AudioStreamPlayer _siren;

    public override void _Ready()
    {
        base._Ready();
        _siren = GetNodeOrNull<AudioStreamPlayer>("Siren");
    }

    /// <summary>The bomb that will land nearest the player, if any is falling.</summary>
    Bomb Threat(out float distance)
    {
        Bomb nearest = null;
        distance = WarnRadius;
        if (Seat == null) return null;
        Vector3 me = Seat.WorldPosition;
        foreach (var bomb in Bomb.Falling)
        {
            if (!IsInstanceValid(bomb) || !bomb.IsInsideTree()) continue;
            Vector3 to = bomb.GlobalPosition - me;
            to.Y = 0f;
            if (to.Length() >= distance) continue;
            distance = to.Length();
            nearest = bomb;
        }
        return nearest;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        bool on = Threat(out _) != null && !GetTree().Paused;
        if (_siren == null || OnScreen || _siren.Playing == on) return;
        if (on) _siren.Play();
        else _siren.Stop();
    }

    protected override void Paint()
    {
        var bomb = Threat(out float distance);
        if (bomb == null) return;
        float w = Size.X;
        bool danger = distance < DangerRadius;
        bool blink = Mathf.PosMod(Clock, 0.5f) < 0.32f;
        DrawRect(new Rect2(0f, 0f, w, Size.Y), new Color(0.25f, 0f, 0f, 0.35f));
        DrawRect(new Rect2(0f, 0f, w, Size.Y), Red, false, 2f);
        if (blink) Text("ЯДЕРНЫЙ УДАР", new Vector2(0f, 30f), 28, Red, HorizontalAlignment.Center, w);
        Text($"До эпицентра {Mathf.RoundToInt(distance)} м · взрыв через {Mathf.CeilToInt(bomb.TimeToGround)} с", new Vector2(0f, 58f), 18, Amber, HorizontalAlignment.Center, w);
        Text(danger ? "В ЗОНЕ ПОРАЖЕНИЯ — УХОДИТЕ ОТ СТРЕЛКИ" : "Вне зоны поражения. Отвернитесь от вспышки",
             new Vector2(0f, 82f), 16, danger ? Red : Green, HorizontalAlignment.Center, w);

        // The arrow: round a ring under the text, towards where the bomb will land, as the pilot looks.
        var cam = Seat.Camera;
        if (cam == null) return;
        Vector3 local = cam.GlobalTransform.AffineInverse() * new Vector3(bomb.GlobalPosition.X, cam.GlobalPosition.Y, bomb.GlobalPosition.Z);
        var dir = new Vector2(local.X, local.Z);
        dir = dir.LengthSquared() < 1e-4f ? Vector2.Up : dir.Normalized();
        var c = new Vector2(w * 0.5f, Size.Y + ArrowRing + 14f);
        DrawArc(c, ArrowRing, 0f, Mathf.Tau, 40, new Color(Red, 0.5f), 1.5f, true);
        Vector2 tip = c + dir * (ArrowRing + 10f), side = new Vector2(-dir.Y, dir.X) * 11f;
        DrawColoredPolygon(new[] { tip, tip - dir * 26f + side, tip - dir * 26f - side }, Red);
        Text("☢", c + new Vector2(-30f, 9f), 26, blink ? Red : Amber, HorizontalAlignment.Center, 60f);
    }
}
