using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Main menu: mech choice, customisation (paint and weapons), battle setup (map, enemies) and settings.
/// All the controls are laid out in main_menu.tscn; this fills the lists from <see cref="Catalog"/>,
/// keeps <see cref="GameConfig"/> in sync and shows the chosen mech turning in the preview.
/// </summary>
public partial class MainMenu : Control
{
    [Export(PropertyHint.File, "*.tscn")] public string GameScene = "res://scenes/game.tscn";
    [Export] public PackedScene SlotRowScene;
    [Export] public float PreviewTurnSpeed = 0.5f;

    static readonly string[] SectionNames = { "Голова", "Корпус", "Левый торс", "Правый торс", "Левая рука", "Правая рука", "Левая нога", "Правая нога" };

    Control[] _pages;
    Node3D _pivot;
    Node3D _preview;
    Label _summary, _mechInfo, _mapInfo, _enemyCountLabel, _volumeLabel, _sensLabel, _fovLabel;
    VBoxContainer _slotList;
    AudioStreamPlayer _click;
    Label _vehicleCountLabel;
    TextureRect _insigniaPreview;
    FileDialog _insigniaDialog;
    readonly Button[] _bindButtons = new Button[GameConfig.BindableActions.Length];
    int _rebinding = -1;

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _pivot = GetNode<Node3D>("%PreviewPivot");
        _summary = GetNode<Label>("%Summary");
        _mechInfo = GetNode<Label>("%MechInfo");
        _mapInfo = GetNode<Label>("%MapInfo");
        _enemyCountLabel = GetNode<Label>("%EnemyCountLabel");
        _volumeLabel = GetNode<Label>("%VolumeLabel");
        _sensLabel = GetNode<Label>("%SensLabel");
        _fovLabel = GetNode<Label>("%FovLabel");
        _slotList = GetNode<VBoxContainer>("%SlotList");
        _click = GetNodeOrNull<AudioStreamPlayer>("%Click");
        _vehicleCountLabel = GetNode<Label>("%VehicleCountLabel");
        _insigniaPreview = GetNode<TextureRect>("%InsigniaPreview");
        _insigniaDialog = GetNode<FileDialog>("%InsigniaDialog");

        _pages = new[] { GetNode<Control>("%PageMech"), GetNode<Control>("%PageCustom"), GetNode<Control>("%PageBattle"), GetNode<Control>("%PageSettings") };
        GetNode<Button>("%BtnPlay").Pressed += StartBattle;
        GetNode<Button>("%BtnMech").Pressed += () => ShowPage(0);
        GetNode<Button>("%BtnCustom").Pressed += () => ShowPage(1);
        GetNode<Button>("%BtnBattle").Pressed += () => ShowPage(2);
        GetNode<Button>("%BtnSettings").Pressed += () => ShowPage(3);
        GetNode<Button>("%BtnQuit").Pressed += () =>
        {
            GameConfig.Save();
            GetTree().Quit();
        };

        SetupMechPage();
        SetupCustomPage();
        SetupBattlePage();
        SetupSettingsPage();
        ShowPage(0);
        RebuildPreview();
        RebuildSlots();
        Refresh();
    }

    public override void _Process(double delta)
    {
        _pivot.RotateY(PreviewTurnSpeed * (float)delta);
    }

    void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == index;
        _click?.Play();
    }

    void StartBattle()
    {
        GameConfig.Save();
        GetTree().ChangeSceneToFile(GameScene);
    }

    static void Fill(OptionButton option, Catalog.Entry[] entries, string selectedId)
    {
        option.Clear();
        foreach (var e in entries) option.AddItem(e.Name);
        option.Selected = Catalog.IndexOf(entries, selectedId);
    }

    // ── Pages ───────────────────────────────────────────────────

    void SetupMechPage()
    {
        var list = GetNode<ItemList>("%MechList");
        list.Clear();
        foreach (var e in Catalog.PlayerUnits) list.AddItem(e.Name);
        list.Select(Catalog.IndexOf(Catalog.PlayerUnits, GameConfig.MechId));
        list.ItemSelected += index =>
        {
            GameConfig.MechId = Catalog.PlayerUnits[index].Id;
            _click?.Play();
            RebuildPreview();
            RebuildSlots();
            Refresh();
        };
    }

    void SetupCustomPage()
    {
        var camo = GetNode<OptionButton>("%CamoOption");
        Fill(camo, Catalog.Camos, GameConfig.CamoId);
        camo.ItemSelected += index =>
        {
            GameConfig.CamoId = Catalog.Camos[index].Id;
            RebuildPreview();
        };

        var stripe = GetNode<ColorPickerButton>("%StripePicker");
        stripe.Color = GameConfig.StripeColor;
        stripe.ColorChanged += color =>
        {
            GameConfig.StripeColor = color;
            RebuildPreview();
        };

        GetNode<Button>("%BtnStock").Pressed += () =>
        {
            GameConfig.Loadouts.Remove(GameConfig.MechId);
            GameConfig.Groups.Remove(GameConfig.MechId);
            RebuildPreview();
            RebuildSlots();
        };

        // Emblem: any picture from disk.
        GetNode<Button>("%BtnInsignia").Pressed += () => _insigniaDialog.PopupCentered(new Vector2I(900, 600));
        GetNode<Button>("%BtnInsigniaClear").Pressed += () => SetInsignia("");
        _insigniaDialog.FileSelected += SetInsignia;
        _insigniaPreview.Texture = GameConfig.Insignia;

        // Which key or mouse button fires each weapon group.
        for (int i = 0; i < _bindButtons.Length; i++)
        {
            int index = i;
            _bindButtons[i] = GetNode<Button>($"%BindGroup{i + 1}");
            _bindButtons[i].Pressed += () =>
            {
                _rebinding = index;
                RefreshBindings();
            };
        }
        RefreshBindings();
    }

    void SetInsignia(string path)
    {
        GameConfig.InsigniaPath = path;
        _insigniaPreview.Texture = GameConfig.Insignia;
        RebuildPreview();
    }

    void RefreshBindings()
    {
        for (int i = 0; i < _bindButtons.Length; i++)
            _bindButtons[i].Text = i == _rebinding ? "нажмите клавишу или кнопку мыши…" : GameConfig.BindingText(GameConfig.BindableActions[i]);
    }

    public override void _Input(InputEvent @event)
    {
        // Waiting for the new key of a weapon group: the next key or mouse button press is it.
        if (_rebinding < 0) return;
        bool key = @event is InputEventKey { Pressed: true, Echo: false };
        bool mouse = @event is InputEventMouseButton { Pressed: true };
        if (!key && !mouse) return;
        if (@event is not InputEventKey { PhysicalKeycode: Key.Escape })
            GameConfig.Rebind(GameConfig.BindableActions[_rebinding], @event);
        _rebinding = -1;
        RefreshBindings();
        GetViewport().SetInputAsHandled();
    }

    void SetupBattlePage()
    {
        var map = GetNode<OptionButton>("%MapOption");
        Fill(map, Catalog.Maps, GameConfig.MapId);
        map.ItemSelected += index =>
        {
            GameConfig.MapId = Catalog.Maps[index].Id;
            Refresh();
        };

        var count = GetNode<HSlider>("%EnemyCount");
        count.Value = GameConfig.EnemyCount;
        count.ValueChanged += value =>
        {
            GameConfig.EnemyCount = (int)value;
            Refresh();
        };

        var type = GetNode<OptionButton>("%EnemyTypeOption");
        type.Clear();
        type.AddItem("Случайные");
        foreach (var e in Catalog.Mechs) type.AddItem(e.Name);
        type.Selected = GameConfig.EnemyMechId == "random" ? 0 : Catalog.IndexOf(Catalog.Mechs, GameConfig.EnemyMechId) + 1;
        type.ItemSelected += index => GameConfig.EnemyMechId = index == 0 ? "random" : Catalog.Mechs[index - 1].Id;

        var vehicles = GetNode<HSlider>("%VehicleCount");
        vehicles.Value = GameConfig.EnemyVehicleCount;
        vehicles.ValueChanged += value =>
        {
            GameConfig.EnemyVehicleCount = (int)value;
            Refresh();
        };

        var crush = GetNode<CheckButton>("%CrushCheck");
        crush.ButtonPressed = GameConfig.MechsCrush;
        crush.Toggled += on => GameConfig.MechsCrush = on;

        var botsFight = GetNode<CheckButton>("%BotsFightCheck");
        botsFight.ButtonPressed = GameConfig.BotsFight;
        botsFight.Toggled += on => GameConfig.BotsFight = on;

        // Battle length: the list's items are minutes, 0 = no limit.
        var time = GetNode<OptionButton>("%TimeLimitOption");
        for (int i = 0; i < time.ItemCount; i++)
            if (time.GetItemId(i) == GameConfig.TimeLimitMinutes) time.Selected = i;
        time.ItemSelected += index => GameConfig.TimeLimitMinutes = time.GetItemId((int)index);

        var respawn = GetNode<CheckButton>("%RespawnCheck");
        respawn.ButtonPressed = GameConfig.EnemyRespawn;
        respawn.Toggled += on => GameConfig.EnemyRespawn = on;

        var difficulty = GetNode<OptionButton>("%DifficultyOption");
        difficulty.Selected = GameConfig.Difficulty;
        difficulty.ItemSelected += index => GameConfig.Difficulty = (int)index;
    }

    void SetupSettingsPage()
    {
        var volume = GetNode<HSlider>("%VolumeSlider");
        volume.Value = GameConfig.MasterVolume;
        volume.ValueChanged += value =>
        {
            GameConfig.MasterVolume = (float)value;
            GameConfig.ApplySettings();
            Refresh();
        };

        var sens = GetNode<HSlider>("%SensSlider");
        sens.Value = GameConfig.MouseSensitivity;
        sens.ValueChanged += value =>
        {
            GameConfig.MouseSensitivity = (float)value;
            Refresh();
        };

        var fov = GetNode<HSlider>("%FovSlider");
        fov.Value = GameConfig.Fov;
        fov.ValueChanged += value =>
        {
            GameConfig.Fov = (float)value;
            Refresh();
        };

        var fullscreen = GetNode<CheckButton>("%FullscreenCheck");
        fullscreen.ButtonPressed = GameConfig.Fullscreen;
        fullscreen.Toggled += on =>
        {
            GameConfig.Fullscreen = on;
            GameConfig.ApplySettings();
        };

        var vsync = GetNode<CheckButton>("%VSyncCheck");
        vsync.ButtonPressed = GameConfig.VSync;
        vsync.Toggled += on =>
        {
            GameConfig.VSync = on;
            GameConfig.ApplySettings();
        };

        var rate = GetNode<OptionButton>("%SimRateOption");
        rate.Selected = GameConfig.SimulationRate >= 120 ? 1 : 0;
        rate.ItemSelected += index =>
        {
            GameConfig.SimulationRate = index == 1 ? 120 : 60;
            GameConfig.ApplySettings();
        };

        var effects = GetNode<OptionButton>("%EffectsOption");
        effects.Selected = GameConfig.EffectsQuality;
        effects.ItemSelected += index => GameConfig.EffectsQuality = (int)index;
    }

    void Refresh()
    {
        var mech = Catalog.Find(Catalog.PlayerUnits, GameConfig.MechId);
        var map = Catalog.Find(Catalog.Maps, GameConfig.MapId);
        _mechInfo.Text = mech.Description;
        _mapInfo.Text = map.Description;
        _enemyCountLabel.Text = GameConfig.EnemyCount.ToString();
        _vehicleCountLabel.Text = GameConfig.EnemyVehicleCount.ToString();
        _volumeLabel.Text = $"{Mathf.RoundToInt(GameConfig.MasterVolume * 100f)}%";
        _sensLabel.Text = $"×{GameConfig.MouseSensitivity:0.00}";
        _fovLabel.Text = $"{Mathf.RoundToInt(GameConfig.Fov)}°";
        _summary.Text = $"{mech.Name}  ·  {map.Name}  ·  мехов: {GameConfig.EnemyCount}, техники: {GameConfig.EnemyVehicleCount}";
    }

    // ── Mech preview and weapon slots ───────────────────────────

    Node3D SpawnStock() => GD.Load<PackedScene>(Catalog.Find(Catalog.PlayerUnits, GameConfig.MechId).Path).Instantiate<Node3D>();

    void RebuildPreview()
    {
        if (IsInstanceValid(_preview)) _preview.QueueFree();
        _preview = SpawnStock();
        // A statue: no pilot and no simulation, just the model in its rest pose.
        _preview.ProcessMode = ProcessModeEnum.Disabled;
        if (_preview is Mech mech)
        {
            GameConfig.Loadouts.TryGetValue(GameConfig.MechId, out string[] loadout);
            mech.ApplyLoadout(loadout);
            mech.ApplyPaint(GD.Load<Material>(Catalog.Find(Catalog.Camos, GameConfig.CamoId).Path), GameConfig.StripeColor);
            mech.ApplyInsignia(GameConfig.Insignia);
        }
        else if (_preview is Vehicle vehicle)
        {
            vehicle.ApplyPaint(GD.Load<Material>(Catalog.Find(Catalog.Camos, GameConfig.CamoId).Path));
        }
        _pivot.AddChild(_preview);
        if (_preview is not Mech)
        {
            // Small next to a mech (or far too long, for a ship): bring it to a size that fills the stand.
            float size = Geo.LocalBounds(_preview).Size.Length();
            _preview.Scale = Vector3.One * Mathf.Clamp(13f / Mathf.Max(size, 0.1f), 0.3f, 3.2f);
            // Aircraft have their origin in the middle: stand them on the floor.
            _preview.Position = Vector3.Up * Mathf.Max(0f, -Geo.LocalBounds(_preview).Position.Y * _preview.Scale.Y);
        }
    }

    /// <summary>One row per weapon slot of the chosen mech, each with a list of weapons to fit.</summary>
    void RebuildSlots()
    {
        foreach (Node child in _slotList.GetChildren()) child.QueueFree();

        var stockNode = SpawnStock();
        if (stockNode is not Mech stockMech)
        {
            // Nothing to refit on foot or in a vehicle.
            stockNode.Free();
            return;
        }
        List<Weapon> slots = stockMech.GetWeaponSlots();
        var stock = new string[slots.Count];
        for (int i = 0; i < slots.Count; i++)
            stock[i] = Catalog.WeaponIdByPath(slots[i].SceneFilePath) ?? Catalog.Weapons[0].Id;

        GameConfig.Loadouts.TryGetValue(GameConfig.MechId, out string[] chosen);
        if (chosen == null || chosen.Length != stock.Length) chosen = stock;

        for (int i = 0; i < slots.Count; i++)
        {
            int slot = i;
            var row = SlotRowScene.Instantiate<Control>();
            _slotList.AddChild(row);
            row.GetNode<Label>("Name").Text = $"{i + 1}. {SectionNames[(int)slots[i].Mount]}";
            var option = row.GetNode<OptionButton>("Option");
            Fill(option, Catalog.Weapons, chosen[i]);
            option.ItemSelected += index =>
            {
                GameConfig.Loadouts.TryGetValue(GameConfig.MechId, out string[] current);
                if (current == null || current.Length != stock.Length) current = (string[])stock.Clone();
                current[slot] = Catalog.Weapons[index].Id;
                GameConfig.Loadouts[GameConfig.MechId] = current;
                RebuildPreview();
            };

            // Which trigger this slot is on: its weapon's usual group unless the player chose otherwise.
            var group = row.GetNode<OptionButton>("Group");
            GameConfig.Groups.TryGetValue(GameConfig.MechId, out int[] groups);
            bool custom = groups != null && groups.Length == stock.Length && groups[i] >= 1;
            group.Selected = custom ? groups[i] : 0;
            group.ItemSelected += index =>
            {
                GameConfig.Groups.TryGetValue(GameConfig.MechId, out int[] now);
                if (now == null || now.Length != stock.Length) now = new int[stock.Length];
                now[slot] = (int)index;
                GameConfig.Groups[GameConfig.MechId] = now;
            };
        }
        stockMech.Free();
    }
}
