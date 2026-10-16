using Godot;

namespace MechSim;

/// <summary>
/// Full-screen overlay: the crosshair (where the weapons point), what it's on, the brackets around
/// the locked target, prompts, and the dimming when the unit is out of action.
/// </summary>
public partial class Reticle : HudWidget
{
    // Follows the aim and the target across the screen: drawn every frame.
    public Reticle() => RedrawRate = 0f;

    /// <summary>Colour of the sight over a machine of one's own side.</summary>
    [Export] public Color FriendColor = new(0.35f, 0.65f, 1f, 0.95f);
    [ExportGroup("Torso scales")]
    /// <summary>How far from the sight the twist arc (above it) and the pitch scale (to its left) are drawn, pixels.</summary>
    [Export] public float ScaleRadius = 74f;
    /// <summary>Half the length of each scale: the arc's half-angle in degrees, the pitch scale's half-height in pixels.</summary>
    [Export] public float TwistArc = 32f;
    [Export] public float PitchHalf = 44f;

    protected override void Paint()
    {
        Vector2 sz = Size;
        Vector2 c = sz / 2f;

        if (Seat.Offline)
            DrawRect(new Rect2(Vector2.Zero, sz), new Color(0f, 0f, 0f, 0.45f));

        // Screen centre, unless free look turned the camera away from the guns.
        var cam = Seat.Camera;
        Vector2 aim = c;
        bool aimVisible = true;
        if (Seat.FreeLook)
        {
            aimVisible = !cam.IsPositionBehind(Seat.AimPoint);
            if (aimVisible) aim = cam.UnprojectPosition(Seat.AimPoint);
            Text("СВОБОДНЫЙ ОБЗОР", new Vector2(c.X - 100f, 120f), 14, Amber, HorizontalAlignment.Center, 200f);
        }

        DrawTargetMarker(cam, sz);
        if (Seat.Prompt != null)
            Text(Seat.Prompt, new Vector2(0f, c.Y + 110f), 18, Amber, HorizontalAlignment.Center, sz.X);
        if (!aimVisible || Seat.IsDestroyed) return;

        // Electronics dead (a pulse): a bare sight — it no longer tells friend from foe, nor the range.
        ITarget aimed = Jam > 0f ? null : Units.FromCollider(Seat.AimCollider);
        bool onTarget = aimed is { IsDestroyed: false } && Game.PlayerUnit != null && Units.Hostile(Game.PlayerUnit, aimed);
        // One of our own under the sight: blue, and struck through.
        bool friend = aimed is { IsDestroyed: false } && aimed != Game.PlayerUnit && !onTarget;
        Color col = onTarget ? Red : friend ? FriendColor : Green;
        if (friend)
        {
            DrawLine(aim + new Vector2(-24, -24), aim + new Vector2(24, 24), col, 2.5f);
            DrawLine(aim + new Vector2(-24, 24), aim + new Vector2(24, -24), col, 2.5f);
            Text($"{aimed.DisplayName}   СВОЙ   {Mathf.RoundToInt(aimed.Integrity * 100f)}%", aim + new Vector2(-200f, 70f), 15, col, HorizontalAlignment.Center, 400f);
        }
        // Where the guns are getting to (the arms reaching ahead of the torso, a turret still turning):
        // a small open circle, with a thin line back to the sight.
        if (Seat.LeadAim is { } lead && !cam.IsPositionBehind(lead))
        {
            Vector2 p = cam.UnprojectPosition(lead);
            if (p.DistanceTo(aim) > 10f)
            {
                DrawArc(p, 9f, 0f, Mathf.Tau, 20, new Color(col, 0.85f), 1.5f, true);
                DrawCircle(p, 1.5f, col);
                DrawLine(aim + (p - aim).Normalized() * 20f, p - (p - aim).Normalized() * 10f, new Color(col, 0.3f), 1f);
            }
        }
        DrawArc(aim, 18f, 0f, Mathf.Tau, 32, col, 1.5f, true);
        DrawLine(aim + new Vector2(-30, 0), aim + new Vector2(-20, 0), col, 2f);
        DrawLine(aim + new Vector2(20, 0), aim + new Vector2(30, 0), col, 2f);
        DrawLine(aim + new Vector2(0, -30), aim + new Vector2(0, -20), col, 2f);
        DrawLine(aim + new Vector2(0, 20), aim + new Vector2(0, 30), col, 2f);
        DrawCircle(aim, 2f, col);
        DrawTorsoScales(aim);

        // A howitzer's rangefinder: how far the sight's point is, how long the shell will fly, whether it gets there.
        if (ShellArc.Active && Jam <= 0f)
        {
            Color tone = ShellArc.OnTarget ? Amber : Red;
            string verdict = ShellArc.OnTarget ? $"ПОЛЁТ {ShellArc.FlightTime:0.0} с" : "НЕДОЛЁТ";
            Text($"ДАЛЬНОМЕР {Mathf.RoundToInt(ShellArc.Range)} м   {verdict}", aim + new Vector2(-200f, 96f), 15, tone, HorizontalAlignment.Center, 400f);
            if (!ShellArc.Loaded) Text("ЗАРЯЖАНИЕ", aim + new Vector2(-200f, 116f), 13, Dim, HorizontalAlignment.Center, 400f);
        }

        if (Seat.Magnification > 1.15f)
            Text($"×{Seat.Magnification:0.0}", aim + new Vector2(38f, -24f), 14, Green);

        if (!onTarget) return;
        float dist = Seat.WorldPosition.DistanceTo(aimed.GlobalPosition);
        // For a mech: the section under the crosshair; otherwise the hull.
        string state = Seat.AimCollider is HitBox box && aimed.Damage != null
            ? $"{DamageModel.ShortNames[(int)box.Section]} {Mathf.RoundToInt(aimed.Damage.Fraction(box.Section) * 100f)}%"
            : $"{Mathf.RoundToInt(aimed.Integrity * 100f)}%";
        Text($"{aimed.DisplayName}   {Mathf.RoundToInt(dist)} м   {state}",
             aim + new Vector2(-200f, 70f), 15, Red, HorizontalAlignment.Center, 400f);
    }

    /// <summary>
    /// How the torso stands against the legs, as in MechWarrior: an arc over the sight with a bar running
    /// from its middle to where the legs point (twist), and a scale left of it with a bar from the horizon
    /// mark up or down to where the guns are tipped (pitch).
    /// </summary>
    void DrawTorsoScales(Vector2 aim)
    {
        float r = ScaleRadius;
        var line = new Color(Green, Green.A * 0.6f);
        if (Seat.MaxTwist > 0.01f)
        {
            float half = Mathf.DegToRad(TwistArc), up = -Mathf.Pi / 2f;
            DrawArc(aim, r, up - half, up + half, 24, line, 2f, true);
            foreach (float a in new[] { up - half, up, up + half })
            {
                var dir = Vector2.FromAngle(a);
                DrawLine(aim + dir * (r - 5f), aim + dir * (r + 5f), line, 2f);
            }
            // The torso turned left (twist > 0) leaves the legs pointing right of the sight.
            float legs = up + Mathf.Clamp(Seat.Twist / Seat.MaxTwist, -1f, 1f) * half;
            Color bar = Seat.CounterRotate ? Green : Amber;
            if (Mathf.Abs(legs - up) > 0.01f) DrawArc(aim, r, Mathf.Min(up, legs), Mathf.Max(up, legs), 16, bar, 4f, true);
            var at = aim + Vector2.FromAngle(legs) * (r + 7f);
            var outward = Vector2.FromAngle(legs);
            var side = new Vector2(-outward.Y, outward.X) * 5f;
            DrawColoredPolygon(new[] { at - outward * 5f, at + outward * 4f + side, at + outward * 4f - side }, bar);
        }
        if (Seat.MaxPitch - Seat.MinPitch > 0.01f)
        {
            float x = aim.X - r, h = PitchHalf;
            DrawLine(new Vector2(x, aim.Y - h), new Vector2(x, aim.Y + h), line, 2f);
            foreach (float k in new[] { -1f, -0.5f, 0.5f, 1f })
                DrawLine(new Vector2(x - 3f, aim.Y + k * h), new Vector2(x + 3f, aim.Y + k * h), line, 1.5f);
            DrawLine(new Vector2(x - 6f, aim.Y), new Vector2(x + 6f, aim.Y), line, 2f);
            float p = Seat.Pitch;
            float share = p >= 0f ? p / Mathf.Max(Seat.MaxPitch, 0.01f) : p / Mathf.Max(-Seat.MinPitch, 0.01f);
            float y = aim.Y - Mathf.Clamp(share, -1f, 1f) * h;
            if (Mathf.Abs(y - aim.Y) > 0.5f) DrawLine(new Vector2(x, aim.Y), new Vector2(x, y), Amber, 4f);
            var tip = new Vector2(x + 4f, y);
            DrawColoredPolygon(new[] { tip, tip + new Vector2(9f, -5f), tip + new Vector2(9f, 5f) }, Amber);
        }
    }

    /// <summary>Brackets around the locked target, or an arrow pointing towards it when it's off-screen.</summary>
    void DrawTargetMarker(Camera3D cam, Vector2 sz)
    {
        var t = Seat.CanLock && Jam <= 0f ? Seat.Target : null;
        if (t == null || cam == null) return;
        Vector3 center = t.AimCenter;
        Vector2 c = sz / 2f;
        bool behind = cam.IsPositionBehind(center);
        Vector2 p = behind ? c : cam.UnprojectPosition(center);

        if (behind || p.X < 30f || p.Y < 30f || p.X > sz.X - 30f || p.Y > sz.Y - 30f)
        {
            Vector3 local = cam.GlobalTransform.AffineInverse() * center;
            var dir = new Vector2(local.X, -local.Y);
            dir = dir.LengthSquared() < 1e-6f ? Vector2.Right : dir.Normalized();
            Vector2 tip = c + dir * (Mathf.Min(c.X, c.Y) - 60f);
            Vector2 side = new Vector2(-dir.Y, dir.X) * 10f;
            DrawColoredPolygon(new[] { tip, tip - dir * 22f + side, tip - dir * 22f - side }, Amber);
            return;
        }

        // Half-size from the apparent height of ~5 m at the target's distance.
        Vector2 up = cam.UnprojectPosition(center + cam.GlobalTransform.Basis.Y * 5f);
        float r = Mathf.Max(16f, p.DistanceTo(up) * 1.15f);
        float k = r * 0.35f;
        // Missile lock: a ring closes round the target while the sight is held on it, then it all goes red.
        float progress = Seat.LockProgress;
        bool locked = progress >= 1f;
        Color col = locked ? Red : Amber;
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            Vector2 q = p + corner * r;
            DrawLine(q, q - new Vector2(corner.X * k, 0f), col, 2f);
            DrawLine(q, q - new Vector2(0f, corner.Y * k), col, 2f);
        }
        if (locked) Text("ЗАХВАТ", p + new Vector2(-50f, -r - 8f), 14, Red, HorizontalAlignment.Center, 100f);
        else if (progress > 0f) DrawArc(p, r * 1.25f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * progress, 32, Amber, 2f, true);
    }
}
