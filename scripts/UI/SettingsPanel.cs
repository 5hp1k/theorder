using System;
using Godot;

namespace MechSim;

/// <summary>
/// The settings (scenes/ui/settings_panel.tscn), in four tabs: the game (language, mouse, field of view,
/// frame counter), sound (the volume of each kind of sound, and the cockpit computer's voice), graphics (window, resolution, frame limit, and every costly effect on its own
/// switch, with presets that set them all at once) and controls (a key or mouse button for each action).
/// Used by the main menu and by the pause menu in battle. Changes go straight into
/// <see cref="GameConfig"/> and the engine; they are written to disk when the page is left.
/// </summary>
public partial class SettingsPanel : Control
{
    /// <summary>One line of the controls tab: the action's name and the button showing its key.</summary>
    [Export] public PackedScene BindRowScene;
    /// <summary>The voices the cockpit computer can have (audio/voice/voices.tres).</summary>
    [Export] public VoiceCatalog Voices;
    /// <summary>The line a voice is heard saying when it is chosen or tried.</summary>
    [Export] public string PreviewLine = "Startup";

    Label _sensLabel, _fovLabel, _scaleLabel, _weatherLabel, _drawLabel, _fxRangeLabel, _debrisLabel;
    OptionButton _preset;
    Button[] _bindButtons = Array.Empty<Button>();
    int _rebinding = -1;
    bool _filling;

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        _sensLabel = GetNode<Label>("%SensLabel");
        _fovLabel = GetNode<Label>("%FovLabel");
        _scaleLabel = GetNode<Label>("%RenderScaleLabel");
        _weatherLabel = GetNode<Label>("%WeatherLabel");
        _drawLabel = GetNode<Label>("%DrawDistanceLabel");
        _fxRangeLabel = GetNode<Label>("%FxRangeLabel");
        _debrisLabel = GetNode<Label>("%DebrisLabel");
        _preset = GetNode<OptionButton>("%EffectsOption");

        var window = GetNode<OptionButton>("%WindowSizeOption");
        foreach (Vector2I size in GameConfig.WindowSizes) window.AddItem(Loc.T(size.X == 0 ? "Как есть" : $"{size.X} × {size.Y}"));
        var limit = GetNode<OptionButton>("%MaxFpsOption");
        foreach (int fps in GameConfig.FpsLimits) limit.AddItem(Loc.T(fps == 0 ? "Без ограничения" : $"{fps} к/с"));

        var language = GetNode<OptionButton>("%LanguageOption");
        foreach (string name in Loc.LanguageNames) language.AddItem(name);
        language.Selected = Mathf.Max(0, Array.IndexOf(Loc.Languages, GameConfig.Language));
        language.ItemSelected += index =>
        {
            GameConfig.Language = Loc.Languages[index];
            GameConfig.Save();
            // The menu is redrawn in the new language at once; a battle in progress, from its next start.
            if (GetTree().CurrentScene is MainMenu) GetTree().ReloadCurrentScene();
        };

        // The sound.
        Volume("Volume", () => GameConfig.MasterVolume, v => GameConfig.MasterVolume = v);
        Volume("Effects", () => GameConfig.EffectsVolume, v => GameConfig.EffectsVolume = v);
        Volume("Ambience", () => GameConfig.AmbienceVolume, v => GameConfig.AmbienceVolume = v);
        Volume("Signals", () => GameConfig.SignalsVolume, v => GameConfig.SignalsVolume = v);
        Volume("VoiceVolume", () => GameConfig.VoiceVolume, v => GameConfig.VoiceVolume = v);
        BuildVoices();

        // The game.
        Slide("%SensSlider", () => GameConfig.MouseSensitivity, v => GameConfig.MouseSensitivity = v);
        Slide("%FovSlider", () => GameConfig.Fov, v => GameConfig.Fov = v);
        Check("%ShowFpsCheck", () => GameConfig.ShowFps, v => GameConfig.ShowFps = v);
        Check("%GlassFxCheck", () => GameConfig.CockpitGlassFx, v => GameConfig.CockpitGlassFx = v);
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
        Slide("%DrawDistanceSlider", () => GameConfig.DrawDistance, v => GameConfig.DrawDistance = v, true);
        Pick("%MeshDetailOption", () => GameConfig.MeshDetail >= 3f ? 0 : GameConfig.MeshDetail >= 1.5f ? 1 : 2, i => GameConfig.MeshDetail = i switch { 0 => 4f, 1 => 2f, _ => 1f }, true);
        Slide("%FxRangeSlider", () => GameConfig.FxRange, v => GameConfig.FxRange = v, true);
        Slide("%DebrisSlider", () => GameConfig.DebrisAmount, v => GameConfig.DebrisAmount = v, true);

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

    /// <summary>A volume slider (%{name}Slider) and its percentage (%{name}Label).</summary>
    void Volume(string name, Func<float> get, Action<float> set)
    {
        var label = GetNode<Label>($"%{name}Label");
        Slide($"%{name}Slider", get, v =>
        {
            set(v);
            label.Text = Loc.T($"{Mathf.RoundToInt(v * 100f)}%");
        });
        Refill += () => label.Text = Loc.T($"{Mathf.RoundToInt(get() * 100f)}%");
    }

    // ── The cockpit computer's voice ────────────────────────────

    void BuildVoices()
    {
        var option = GetNode<OptionButton>("%VoiceOption");
        var packs = Voices?.Packs ?? Array.Empty<VoicePack>();
        option.AddItem(Loc.T("Выключен"));
        foreach (var pack in packs) option.AddItem(Loc.T(pack.Title));
        var preview = GetNode<Button>("%VoicePreviewButton");
        Refill += () =>
        {
            option.Selected = Mathf.Max(0, Array.FindIndex(packs, p => p.Id == GameConfig.Voice) + 1);
            preview.Disabled = option.Selected == 0;
        };
        option.ItemSelected += index =>
        {
            if (_filling) return;
            GameConfig.Voice = index == 0 ? GameConfig.VoiceOff : packs[index - 1].Id;
            GameConfig.VoicePicked = true;
            preview.Disabled = index == 0;
            PlayVoice();
        };
        preview.Pressed += PlayVoice;
    }

    /// <summary>The chosen voice says a line, to hear what it is like (and how loud).</summary>
    void PlayVoice()
    {
        var player = GetNode<AudioStreamPlayer>("%VoicePreview");
        player.Stop();
        if (Voices?.Find(GameConfig.Voice) is not { } pack || !pack.Lines.TryGetValue(PreviewLine, out var line)) return;
        player.Stream = line;
        player.Play();
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
        _sensLabel.Text = Loc.T($"×{GameConfig.MouseSensitivity:0.00}");
        _fovLabel.Text = Loc.T($"{Mathf.RoundToInt(GameConfig.Fov)}°");
        _scaleLabel.Text = Loc.T($"{Mathf.RoundToInt(GameConfig.RenderScale * 100f)}%");
        _weatherLabel.Text = Loc.T($"{Mathf.RoundToInt(GameConfig.WeatherDensity * 100f)}%");
        _drawLabel.Text = Loc.T($"{Mathf.RoundToInt(GameConfig.DrawDistance * 100f)}%");
        _fxRangeLabel.Text = Loc.T($"{Mathf.RoundToInt(GameConfig.FxRange)} м");
        _debrisLabel.Text = Loc.T($"{Mathf.RoundToInt(GameConfig.DebrisAmount * 100f)}%");
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
            _bindButtons[i].Text = Loc.T(i == _rebinding ? "нажмите клавишу или кнопку мыши…" : GameConfig.BindingText(GameConfig.BindableActions[i]));
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
