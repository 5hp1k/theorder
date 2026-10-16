using Godot;

namespace MechSim;

/// <summary>
/// The compass over the top of the view: a strip of degrees sliding past a fixed pointer as the torso
/// (the turret) turns, with the cardinal points marked С, В, Ю, З and the bearing read out above it.
/// How far the torso is turned from the legs, and tipped up or down, is shown by the sight (<see cref="Reticle"/>).
/// </summary>
public partial class TorsoIndicator : HudWidget
{
    /// <summary>Degrees of bearing shown either side of the pointer.</summary>
    [Export] public float Span = 60f;
    /// <summary>Height of the strip's baseline from the top of the control, pixels.</summary>
    [Export] public float Baseline = 34f;

    static readonly string[] Cardinals = { "С", "В", "Ю", "З" };

    protected override void Paint()
    {
        float mid = Size.X / 2f, y = Baseline;
        float perDeg = (mid - 12f) / Span;
        // (The compass goes with the rest of the electronics when a pulse hits.)
        bool jammed = Jam > 0f;
        float heading = Seat.HeadingDeg;

        DrawLine(new Vector2(mid - Span * perDeg, y), new Vector2(mid + Span * perDeg, y), Dim, 1.5f);
        if (!jammed)
        {
            int first = Mathf.CeilToInt((heading - Span) / 5f) * 5;
            for (int deg = first; deg <= heading + Span; deg += 5)
            {
                float x = mid + (deg - heading) * perDeg;
                // Fades out towards the ends of the strip.
                float fade = 1f - Mathf.Pow(Mathf.Abs(deg - heading) / Span, 3f);
                int bearing = (int)Mathf.PosMod(deg, 360);
                if (bearing % 90 == 0)
                {
                    DrawLine(new Vector2(x, y), new Vector2(x, y - 12f), new Color(Green, Green.A * fade), 2f);
                    Text(Cardinals[bearing / 90], new Vector2(x - 20f, y - 16f), 18, new Color(bearing == 0 ? Amber : Green, fade), HorizontalAlignment.Center, 40f);
                }
                else if (bearing % 30 == 0)
                {
                    DrawLine(new Vector2(x, y), new Vector2(x, y - 9f), new Color(Green, Green.A * fade), 1.5f);
                    Text($"{bearing}", new Vector2(x - 20f, y - 13f), 12, new Color(Green, Green.A * fade), HorizontalAlignment.Center, 40f);
                }
                else DrawLine(new Vector2(x, y), new Vector2(x, y - (bearing % 10 == 0 ? 6f : 3.5f)), new Color(Dim, Dim.A * fade), 1f);
            }
        }
        else Static(new Rect2(mid - Span * perDeg, y - 16f, Span * perDeg * 2f, 16f), 30);

        // The pointer, and the bearing in a box under it.
        var tip = new Vector2(mid, y + 2f);
        DrawColoredPolygon(new[] { tip, tip + new Vector2(-6f, 9f), tip + new Vector2(6f, 9f) }, jammed ? Red : Green);
        var box = new Rect2(mid - 26f, y + 12f, 52f, 18f);
        DrawRect(box, Panel);
        DrawRect(box, jammed ? Red : Dim, false, 1f);
        Text(jammed ? "---" : $"{Mathf.RoundToInt(heading) % 360:000}", new Vector2(box.Position.X, box.End.Y - 4f), 14, jammed ? Red : Green, HorizontalAlignment.Center, box.Size.X);
    }
}
