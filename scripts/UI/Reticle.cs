using Godot;

namespace MechSim;

/// <summary>
/// Full-screen overlay: the crosshair (where the weapons point), what it's on, the brackets around
/// the locked target, prompts, and the dimming when the unit is out of action.
/// </summary>
public partial class Reticle : HudWidget
{
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

        ITarget aimed = Units.FromCollider(Seat.AimCollider);
        bool onTarget = aimed is { IsDestroyed: false } && aimed.Team == Team.Enemy;
        Color col = onTarget ? Red : Green;
        DrawArc(aim, 18f, 0f, Mathf.Tau, 32, col, 1.5f, true);
        DrawLine(aim + new Vector2(-30, 0), aim + new Vector2(-20, 0), col, 2f);
        DrawLine(aim + new Vector2(20, 0), aim + new Vector2(30, 0), col, 2f);
        DrawLine(aim + new Vector2(0, -30), aim + new Vector2(0, -20), col, 2f);
        DrawLine(aim + new Vector2(0, 20), aim + new Vector2(0, 30), col, 2f);
        DrawCircle(aim, 2f, col);

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

    /// <summary>Brackets around the locked target, or an arrow pointing towards it when it's off-screen.</summary>
    void DrawTargetMarker(Camera3D cam, Vector2 sz)
    {
        var t = Seat.CanLock ? Seat.Target : null;
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
