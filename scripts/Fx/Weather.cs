using Godot;

namespace MechSim;

/// <summary>
/// Wind-blown particles (sand, snow): the emitters ride along with the camera and their density pulses
/// in gusts. Also the single source of wind for anything else that drifts (tumbleweeds, smoke).
/// An optional AudioStreamPlayer child named "Loop" gets louder with the gusts.
/// The weather also has its say on the light and the haze (see <see cref="MapLighting"/>).
/// The kinds the menu offers are scenes/fx/weather_*.tscn, snowfall.tscn and sand_wind.tscn.
/// </summary>
public partial class Weather : Node3D
{
    public static readonly string[] Names = { "Как на карте", "Ясно", "Дождь", "Снег", "Туман", "Пыльная буря" };
    /// <summary>Scene for each of <see cref="Names"/>; the first is whatever the map itself has.</summary>
    public static readonly string[] Scenes =
    {
        "", "res://scenes/fx/weather_clear.tscn", "res://scenes/fx/weather_rain.tscn", "res://scenes/fx/snowfall.tscn",
        "res://scenes/fx/weather_fog.tscn", "res://scenes/fx/sand_wind.tscn",
    };

    [ExportGroup("Light and haze")]
    /// <summary>How many times thicker than the map's own the haze is.</summary>
    [Export] public float Haze = 1f;
    /// <summary>Share of the sky's light that gets through (overcast: well under 1).</summary>
    [Export(PropertyHint.Range, "0,1.5")] public float SkyLight = 1f;
    /// <summary>Share of the direct sunlight that gets through.</summary>
    [Export(PropertyHint.Range, "0,1.5")] public float SunLight = 1f;

    [ExportGroup("Wind")]
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
        MapLighting.Instance?.Relight(Haze, SkyLight, SunLight);
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
        float quality = Mathf.Clamp(GameConfig.WeatherDensity, 0.1f, 1f);
        foreach (Node child in GetChildren())
            if (child is GpuParticles3D p) p.AmountRatio = Mathf.Lerp(CalmAmount, 1f, Gust) * quality;
        if (_loop != null) _loop.VolumeDb = Mathf.Lerp(QuietDb, LoudDb, Gust);
    }
}
