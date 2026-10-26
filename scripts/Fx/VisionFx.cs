using Godot;

namespace MechSim;

/// <summary>
/// The pilot's sights for the dark: night vision (key N) and thermal (key B) — or the switches in the cab.
/// A picture laid over the world (under the interface, materials/vision.gdshader). Thermal also turns the
/// world down — sky, sun, ambient light — and makes the machines' armour glow with heat (the global
/// shader parameter "thermal_vision", read by materials/armor.gdshader), so what is warm stands out.
/// Put in by Game.
/// </summary>
public partial class VisionFx : CanvasLayer
{
    public enum Sight { Off, Night, Thermal }
    public static Sight Mode { get; private set; }

    ColorRect _rect;
    ShaderMaterial _shader;
    // The world's own light, kept to be given back.
    Environment _env;
    float _sky = 1f, _ambient = 1f, _sun = 1f;
    DirectionalLight3D _sunLight;

    public override void _Ready()
    {
        Layer = -1;
        Mode = Sight.Off;
        _shader = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/vision.gdshader") };
        _rect = new ColorRect { Material = _shader, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        RenderingServer.GlobalShaderParameterSet("thermal_vision", 0f);
    }

    public override void _ExitTree()
    {
        if (Mode == Sight.Thermal) World(false);
        Mode = Sight.Off;
        RenderingServer.GlobalShaderParameterSet("thermal_vision", 0f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Game.Cockpit == null || StoryOverlay.InCutscene) return;
        if (@event.IsActionPressed("night_vision")) Set(Mode == Sight.Night ? Sight.Off : Sight.Night);
        else if (@event.IsActionPressed("thermal_vision")) Set(Mode == Sight.Thermal ? Sight.Off : Sight.Thermal);
    }

    public void Set(Sight sight)
    {
        if (Mode == Sight.Thermal && sight != Sight.Thermal) World(false);
        Mode = sight;
        _rect.Visible = sight != Sight.Off;
        _shader.SetShaderParameter("mode", (int)sight);
        _shader.SetShaderParameter("gain", sight == Sight.Night ? 24f : 1f);
        if (sight == Sight.Thermal) World(true);
        RenderingServer.GlobalShaderParameterSet("thermal_vision", sight == Sight.Thermal ? 1f : 0f);
    }

    /// <summary>The world turned down for the thermal sight (or back up).</summary>
    void World(bool down)
    {
        if (down)
        {
            _env = GetTree().CurrentScene.FindChild("WorldEnvironment", true, false) is WorldEnvironment w ? w.Environment : GetViewport().World3D?.Environment;
            _sunLight = Geo.Collect<DirectionalLight3D>(GetTree().CurrentScene) is { Count: > 0 } suns ? suns[0] : null;
            if (_env != null)
            {
                _sky = _env.BackgroundEnergyMultiplier;
                _ambient = _env.AmbientLightEnergy;
                _env.BackgroundEnergyMultiplier = 0f; // the sky is the coldest thing there is
                _env.AmbientLightEnergy = _ambient * 0.15f;
            }
            if (_sunLight != null)
            {
                _sun = _sunLight.LightEnergy;
                _sunLight.LightEnergy = _sun * 0.12f;
            }
        }
        else
        {
            if (_env != null)
            {
                _env.BackgroundEnergyMultiplier = _sky;
                _env.AmbientLightEnergy = _ambient;
            }
            if (_sunLight != null && IsInstanceValid(_sunLight)) _sunLight.LightEnergy = _sun;
        }
    }
}
