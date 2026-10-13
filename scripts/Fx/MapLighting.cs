using Godot;

namespace MechSim;

/// <summary>
/// Sits on a map's WorldEnvironment. The environment in the scene is the best-looking version of the
/// map at its own hour and in its own weather; this
///  · switches off the costly parts of it as the graphics settings say, and sets the quality of the
///    sun's shadows;
///  · re-lights the map for the time of day chosen in the menu (a <see cref="SkyMood"/> from
///    materials/sky/mood_*.tres): moves and tints the sun, tints and dims the sky and the haze;
///  · lets the weather (see <see cref="Weather"/>) thicken the haze and dim the light.
/// </summary>
public partial class MapLighting : WorldEnvironment
{
    public static readonly string[] TimeIds = { "", "dawn", "day", "dusk", "night" };
    public static readonly string[] TimeNames = { "Как на карте", "Рассвет", "День", "Закат", "Ночь" };

    public static MapLighting Instance { get; private set; }
    /// <summary>Night, or weather as dark as night: machines light their lamps.</summary>
    public static bool Dark { get; private set; }
    /// <summary>
    /// The light there is now next to the light the map was made with, as a colour: white by day on
    /// the map's own settings, dim and tinted at dusk, at night, under cloud. For things that glow by
    /// their own colour and have to dim with the world (the water).
    /// </summary>
    public static Color Daylight { get; private set; } = Colors.White;

    /// <summary>The map's sun; its shadows are cheapened on the lower settings.</summary>
    [Export] public NodePath SunPath = "../Sun";

    DirectionalLight3D _sun;
    Environment _env;
    ShaderMaterial _sky;
    // The map as authored, to scale from.
    float _sunEnergy, _fogDensity, _fogHeightDensity, _volumeDensity, _skyBack, _skyLight;
    Color _sunColor, _fogColor;
    SkyMood _mood;
    bool _hasSdfgi, _hasVolume, _hasSsil, _hasSsao, _hasGlow, _shadowsOff;
    float _shadowReach, _sunSize;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        Dark = false;
        Daylight = Colors.White;
        var env = Environment;
        if (env == null) return;
        // The scene's resource is shared by every load of the map: change a copy.
        _env = env = (Environment)env.Duplicate();
        Environment = env;
        if (env.Sky?.SkyMaterial is ShaderMaterial sky)
        {
            env.Sky = (Sky)env.Sky.Duplicate();
            env.Sky.SkyMaterial = _sky = (ShaderMaterial)sky.Duplicate();
            _skyBack = (float)_sky.GetShaderParameter("background_energy");
            _skyLight = (float)_sky.GetShaderParameter("light_energy");
        }
        // What the map was made with: the settings can only take away from it.
        _hasSdfgi = env.SdfgiEnabled;
        _hasVolume = env.VolumetricFogEnabled;
        _hasSsil = env.SsilEnabled;
        _hasSsao = env.SsaoEnabled;
        _hasGlow = env.GlowEnabled;

        _sun = GetNodeOrNull(SunPath) as DirectionalLight3D;
        if (_sun != null)
        {
            _sunEnergy = _sun.LightEnergy;
            _sunColor = _sun.LightColor;
            _shadowReach = _sun.DirectionalShadowMaxDistance;
            _sunSize = _sun.LightAngularDistance;
        }
        _fogDensity = env.FogDensity;
        _fogHeightDensity = env.FogHeightDensity;
        _volumeDensity = env.VolumetricFogDensity;
        _fogColor = env.FogLightColor;

        string id = TimeIds[Mathf.Clamp(GameConfig.TimeOfDay, 0, TimeIds.Length - 1)];
        _mood = id == "" ? null : GD.Load<SkyMood>($"res://materials/sky/mood_{id}.tres");
        ApplyGraphics();
        Relight(1f, 1f, 1f);
    }

    /// <summary>
    /// Switches the costly parts of the lighting on or off as the graphics settings say (bounced light,
    /// light shafts in the haze, soft shading in corners, glow) and sets the quality of the sun's shadows.
    /// Can be called again whenever the settings change.
    /// </summary>
    public void ApplyGraphics()
    {
        if (_env == null) return;
        _env.SdfgiEnabled = _hasSdfgi && GameConfig.Sdfgi;
        _env.VolumetricFogEnabled = _hasVolume && GameConfig.VolumetricFog;
        _env.SsilEnabled = _hasSsil && GameConfig.Ssil;
        _env.SsaoEnabled = _hasSsao && GameConfig.Ssao;
        _env.GlowEnabled = _hasGlow && GameConfig.Glow;
        if (_sun == null) return;
        int shadows = GameConfig.Shadows;
        _shadowsOff = shadows <= 0;
        _sun.DirectionalShadowMode = shadows >= 2 ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
        _sun.DirectionalShadowMaxDistance = shadows switch { 1 => Mathf.Min(_shadowReach, 200f), 2 => Mathf.Min(_shadowReach, 320f), _ => _shadowReach };
        _sun.LightAngularDistance = shadows >= 2 ? _sunSize : 0f;
        RenderingServer.DirectionalShadowAtlasSetSize(shadows switch { 1 => 2048, 2 => 4096, _ => 8192 }, true);
        _sun.ShadowEnabled = !_shadowsOff && _sun.LightEnergy > 0.08f;
    }

    /// <summary>
    /// Sets the light for the chosen hour, with the weather's say on top: how many times thicker the
    /// haze is, and what share of the sky's and the sun's light gets through the clouds.
    /// </summary>
    public void Relight(float haze, float skyShare, float sunShare)
    {
        if (_env == null) return;
        Color sunTint = _mood?.SunTint ?? Colors.White, skyTint = _mood?.SkyTint ?? Colors.White;
        float sunEnergy = (_mood?.SunEnergy ?? 1f) * sunShare, skyEnergy = (_mood?.SkyEnergy ?? 1f) * skyShare;
        // Under cloud the sky greys out.
        float saturation = (_mood?.SkySaturation ?? 1f) * Mathf.Lerp(0.35f, 1f, Mathf.Clamp(skyShare, 0f, 1f));

        if (_sun != null)
        {
            if (_mood != null && _mood.SunElevation >= 0f)
                _sun.Rotation = new Vector3(-Mathf.DegToRad(_mood.SunElevation), _sun.Rotation.Y, 0f);
            _sun.LightColor = _sunColor * sunTint;
            _sun.LightEnergy = _sunEnergy * sunEnergy;
            // A sun too weak to see by isn't worth its shadows.
            _sun.ShadowEnabled = !_shadowsOff && _sun.LightEnergy > 0.08f;
        }
        if (_sky != null)
        {
            _sky.SetShaderParameter("background_energy", _skyBack * skyEnergy);
            _sky.SetShaderParameter("light_energy", _skyLight * skyEnergy);
            _sky.SetShaderParameter("tint", new Vector3(skyTint.R, skyTint.G, skyTint.B));
            _sky.SetShaderParameter("saturation", saturation);
        }
        float grey = _fogColor.Luminance;
        Color fog = new Color(grey, grey, grey).Lerp(_fogColor, saturation) * skyTint * Mathf.Max(skyEnergy, 0.02f);
        _env.FogLightColor = new Color(fog.R, fog.G, fog.B);
        _env.FogDensity = _fogDensity * haze;
        _env.FogHeightDensity = _fogHeightDensity * haze;
        _env.VolumetricFogDensity = _volumeDensity * Mathf.Sqrt(haze);
        Dark = (_mood?.Dark ?? false) || skyEnergy < 0.12f;
        // Mostly the sky's light, partly the sun's.
        Color light = skyTint * skyEnergy * 0.7f + sunTint * sunEnergy * 0.3f;
        Daylight = new Color(light.R, light.G, light.B);
    }
}
