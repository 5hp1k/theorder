using System;
using Godot;

namespace MechSim;

/// <summary>
/// The settings (scenes/ui/settings_panel.tscn), in three tabs: the game (sound, mouse, field of view,
/// frame counter), graphics (window, resolution, frame limit, and every costly effect on its own
/// switch, with presets that set them all at once) and controls (a key or mouse button for each action).
/// Used by the main menu and by the pause menu in battle. Changes go straight into
/// <see cref="GameConfig"/> and the engine; they are written to disk when the page is left.
/// </summary>
public partial class SettingsPanel : Control
{
    /// <summary>One line of the controls tab: the action's name and the button showing its key.</summary>
    [Export] public PackedScene BindRowScene;

    Label _volumeLabel, _sensLabel, _fovLabel, _scaleLabel, _weatherLabel;
    OptionButton _preset;
    Button[] _bindButtons = Array.Empty<Button>();
    int _rebinding = -1;
    bool _filling;

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        _volumeLabel = GetNode<Label>("%VolumeLabel");
        _sensLabel = GetNode<Label>("%SensLabel");
        _fovLabel = GetNode<Label>("%FovLabel");
        _scaleLabel = GetNode<Label>("%RenderScaleLabel");
        _weatherLabel = GetNode<Label>("%WeatherLabel");
        _preset = GetNode<OptionButton>("%EffectsOption");

        var window = GetNode<OptionButton>("%WindowSizeOption");
        foreach (Vector2I size in GameConfig.WindowSizes) window.AddItem(size.X == 0 ? "Как есть" : $"{size.X} × {size.Y}");
        var limit = GetNode<OptionButton>("%MaxFpsOption");
        foreach (int fps in GameConfig.FpsLimits) limit.AddItem(fps == 0 ? "Без ограничения" : $"{fps} к/с");

        // The game.
        Slide("%VolumeSlider", () => GameConfig.MasterVolume, v => GameConfig.MasterVolume = v);
        Slide("%SensSlider", () => GameConfig.MouseSensitivity, v => GameConfig.MouseSensitivity = v);
        Slide("%FovSlider", () => GameConfig.Fov, v => GameConfig.Fov = v);
        Check("%ShowFpsCheck", () => GameConfig.ShowFps, v => GameConfig.ShowFps = v);
        Pick("%SimRateOption", () => GameConfig.SimulationRate >= 120 ? 1 : 0, i => GameConfig.SimulationRate = i == 1 ? 120 : 60);

        // The window.
        Pick("%WindowModeOption", () => GameConfig.WindowMode, i => GameConfig.WindowMode = i);
        Pick("%WindowSizeOption", () => Mathf.Max(0, Array.IndexOf(GameConfig.WindowSizes, GameConfig.WindowSize)), i => GameConfig.WindowSize = GameConfig.WindowSizes[i]);
        Pick("%MaxFpsOption", () => Mathf.Max(0, Array.IndexOf(GameConfig.FpsLimits, GameConfig.MaxFps)), i => GameConfig.MaxFps = GameConfig.FpsLimits[i]);
        Check("%VSyncCheck", () => GameConfig.VSync, v => GameConfig.VSync = v);

        // The picture: any change here makes the preset "the player's own".
        Slide("%RenderScaleSlider", () => GameConfig.RenderScale, v => GameConfig.RenderScale = v, true);
        Pick("%MsaaOption", () => GameConfig.Msaa, i => GameConfig.Msaa = i, true);
        Check("%FxaaCheck", () => GameConfig.Fxaa, v => GameConfig.Fxaa = v, true);
        Check("%TaaCheck", () => GameConfig.Taa, v => GameConfig.Taa = v, true);
        Pick("%ShadowsOption", () => GameConfig.Shadows, i => GameConfig.Shadows = i, true);
        Check("%SsaoCheck", () => GameConfig.Ssao, v => GameConfig.Ssao = v, true);
        Check("%SsilCheck", () => GameConfig.Ssil, v => GameConfig.Ssil = v, true);
        Check("%SdfgiCheck", () => GameConfig.Sdfgi, v => GameConfig.Sdfgi = v, true);
        Check("%VolumetricCheck", () => GameConfig.VolumetricFog, v => GameConfig.VolumetricFog = v, true);
        Check("%GlowCheck", () => GameConfig.Glow, v => GameConfig.Glow = v, true);
        Check("%MirrorCheck", () => GameConfig.WaterMirror, v => GameConfig.WaterMirror = v, true);
        Slide("%WeatherSlider", () => GameConfig.WeatherDensity, v => GameConfig.WeatherDensity = v, true);

        _preset.ItemSelected += index =>
        {
            GameConfig.ApplyPreset((int)index);
            GameConfig.ApplySettings();
            Fill();
        };

        BuildBindings();
        GetNode<Button>("%BtnResetBinds").Pressed += () =>
        {
            GameConfig.ResetBindings();
            RefreshBindings();
        };

        // Leaving the page (or the menu it is in) keeps what was set.
        VisibilityChanged += () =>
        {
            _rebinding = -1;
            if (IsVisibleInTree()) Fill();
            else GameConfig.Save();
        };
        Fill();
    }

    // ── Controls bound to settings ──────────────────────────────

    event Action Refill;

    void Changed(bool custom)
    {
        if (_filling) return;
        if (custom) GameConfig.EffectsQuality = 3;
        GameConfig.ApplySettings();
        Labels();
    }

    void Slide(string name, Func<float> get, Action<float> set, bool custom = false)
    {
        var slider = GetNode<HSlider>(name);
        Refill += () => slider.Value = get();
        slider.ValueChanged += value =>
        {
            if (_filling) return;
            set((float)value);
            Changed(custom);
        };
    }

    void Check(string name, Func<bool> get, Action<bool> set, bool custom = false)
    {
        var check = GetNode<CheckButton>(name);
        Refill += () => check.ButtonPressed = get();
        check.Toggled += on =>
        {
            if (_filling) return;
            set(on);
            Changed(custom);
        };
    }

    void Pick(string name, Func<int> get, Action<int> set, bool custom = false)
    {
        var option = GetNode<OptionButton>(name);
        Refill += () => option.Selected = Mathf.Clamp(get(), 0, option.ItemCount - 1);
        option.ItemSelected += index =>
        {
            if (_filling) return;
            set((int)index);
            Changed(custom);
        };
    }

    /// <summary>Shows what the settings are now.</summary>
    void Fill()
    {
        _filling = true;
        Refill?.Invoke();
        _filling = false;
        Labels();
        RefreshBindings();
    }

    void Labels()
    {
        _volumeLabel.Text = $"{Mathf.RoundToInt(GameConfig.MasterVolume * 100f)}%";
        _sensLabel.Text = $"×{GameConfig.MouseSensitivity:0.00}";
        _fovLabel.Text = $"{Mathf.RoundToInt(GameConfig.Fov)}°";
        _scaleLabel.Text = $"{Mathf.RoundToInt(GameConfig.RenderScale * 100f)}%";
        _weatherLabel.Text = $"{Mathf.RoundToInt(GameConfig.WeatherDensity * 100f)}%";
        _preset.Selected = Mathf.Clamp(GameConfig.EffectsQuality, 0, 3);
    }

    // ── Keys ────────────────────────────────────────────────────

    void BuildBindings()
    {
        var list = GetNode<VBoxContainer>("%BindList");
        string[] actions = GameConfig.BindableActions;
        _bindButtons = new Button[actions.Length];
        for (int i = 0; i < actions.Length; i++)
        {
            int index = i;
            var row = BindRowScene.Instantiate<Control>();
            list.AddChild(row);
            row.GetNode<Label>("Name").Text = GameConfig.ActionNames[i];
            _bindButtons[i] = row.GetNode<Button>("Key");
            _bindButtons[i].Pressed += () =>
            {
                _rebinding = index;
                RefreshBindings();
            };
        }
    }

    void RefreshBindings()
    {
        for (int i = 0; i < _bindButtons.Length; i++)
            _bindButtons[i].Text = i == _rebinding ? "нажмите клавишу или кнопку мыши…" : GameConfig.BindingText(GameConfig.BindableActions[i]);
    }

    public override void _Input(InputEvent @event)
    {
        // Waiting for the new key of an action: the next key or mouse button press is it.
        if (_rebinding < 0 || !IsVisibleInTree()) return;
        bool key = @event is InputEventKey { Pressed: true, Echo: false };
        bool mouse = @event is InputEventMouseButton { Pressed: true };
        if (!key && !mouse) return;
        if (@event is not InputEventKey { PhysicalKeycode: Key.Escape })
            GameConfig.Rebind(GameConfig.BindableActions[_rebinding], @event);
        _rebinding = -1;
        RefreshBindings();
        GetViewport().SetInputAsHandled();
    }
}
