using Godot;

namespace MechSim;

/// <summary>
/// Wind-blown particles (sand, snow): the emitters ride along with the camera and their density pulses
/// in gusts. Also the single source of wind for anything else that drifts (tumbleweeds, smoke).
/// An optional AudioStreamPlayer child named "Loop" gets louder with the gusts.
/// </summary>
public partial class Weather : Node3D
{
    /// <summary>Horizontal direction the wind blows towards.</summary>
    [Export] public Vector3 WindDirection = new(1f, 0f, 0.35f);
    [Export] public float WindSpeed = 9f;
    /// <summary>Seconds between gust peaks, roughly.</summary>
    [Export] public float GustPeriod = 11f;
    [Export(PropertyHint.Range, "0,1")] public float CalmAmount = 0.25f;
    [Export] public float QuietDb = -22f;
    [Export] public float LoudDb = -9f;

    public static Vector3 Direction { get; private set; } = Vector3.Right;
    public static float Speed { get; private set; } = 9f;
    /// <summary>0 = lull, 1 = gust peak.</summary>
    public static float Gust { get; private set; }

    float _time;
    AudioStreamPlayer _loop;

    public override void _Ready()
    {
        Vector3 dir = new Vector3(WindDirection.X, 0f, WindDirection.Z);
        Direction = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Right;
        // Emitters shoot along local -Z.
        LookAtFromPosition(GlobalPosition, GlobalPosition + Direction, Vector3.Up);
        _loop = GetNodeOrNull<AudioStreamPlayer>("Loop");
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        float w = Mathf.Tau / Mathf.Max(GustPeriod, 0.1f);
        float g = 0.5f + 0.35f * Mathf.Sin(_time * w) + 0.15f * Mathf.Sin(_time * w * 2.7f + 1.3f);
        Gust = Mathf.Clamp(g, 0f, 1f);
        Speed = WindSpeed * (0.6f + 0.8f * Gust);

        var cam = GetViewport().GetCamera3D();
        if (cam != null) GlobalPosition = cam.GlobalPosition;

        // Fewer particles on lower effects quality.
        float quality = GameConfig.EffectsQuality switch { 0 => 0.3f, 1 => 0.65f, _ => 1f };
        foreach (Node child in GetChildren())
            if (child is GpuParticles3D p) p.AmountRatio = Mathf.Lerp(CalmAmount, 1f, Gust) * quality;
        if (_loop != null) _loop.VolumeDb = Mathf.Lerp(QuietDb, LoudDb, Gust);
    }
}
