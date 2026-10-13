using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Main menu: unit choice, the hangar (paint and weapons), battle setup (map, hour, weather, enemies)
/// and settings (the settings page is its own scene, settings_panel.tscn, shared with the pause menu).
/// All the controls are laid out in main_menu.tscn; this fills the lists from <see cref="Catalog"/>,
/// keeps <see cref="GameConfig"/> in sync and shows the chosen mech turning in the preview.
/// </summary>
public partial class MainMenu : Control
{
    [Export(PropertyHint.File, "*.tscn")] public string GameScene = "res://scenes/game.tscn";
    /// <summary>One hardpoint in the hangar: its name, sort, slots and the list of what is fitted.</summary>
    [Export] public PackedScene HardpointRowScene;
    /// <summary>One fitted weapon inside a hardpoint row.</summary>
    [Export] public PackedScene SlotRowScene;
    [Export] public float PreviewTurnSpeed = 0.5f;

    static readonly string[] SlotNames = { "ЛУЧЕВОЕ", "БАЛЛИСТИКА", "РАКЕТЫ", "ЛЮБОЕ", "РУЧНОЕ", "БОМБЫ" };
    // As in MechWarrior 4: beam mounts red, ballistic yellow, missile green, omni grey.
    static readonly Color[] SlotColors = { new(1f, 0.35f, 0.3f), new(1f, 0.85f, 0.3f), new(0.45f, 0.9f, 0.4f), new(0.8f, 0.82f, 0.85f), new(0.75f, 0.6f, 0.45f), new(0.6f, 0.7f, 1f) };

    record WeaponInfo(string Id, string Name, SlotType Slot, int Slots);
    static WeaponInfo[] _weaponInfo;

    /// <summary>What sort of mount each catalog weapon needs and how many slots: read once from its prefab.</summary>
    static WeaponInfo[] Weapons()
    {
        if (_weaponInfo != null) return _weaponInfo;
        _weaponInfo = new WeaponInfo[Catalog.Weapons.Length];
        for (int i = 0; i < _weaponInfo.Length; i++)
        {
            var w = GD.Load<PackedScene>(Catalog.Weapons[i].Path).Instantiate<Weapon>();
            _weaponInfo[i] = new WeaponInfo(Catalog.Weapons[i].Id, Catalog.Weapons[i].Name, w.Slot, w.Slots);
            w.Free();
        }
        return _weaponInfo;
    }

    static WeaponInfo Info(string id)
    {
        foreach (var w in Weapons())
            if (w.Id == id) return w;
        return null;
    }

    Control[] _pages;
    Node3D _pivot;
    Node3D _preview;
    Label _summary, _mechInfo, _mapInfo, _enemyCountLabel;
    VBoxContainer _slotList;
    HardpointDrop _shelf;
    AudioStreamPlayer _click;
    Label _vehicleCountLabel;
    TextureRect _insigniaPreview;
    FileDialog _insigniaDialog;

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _pivot = GetNode<Node3D>("%PreviewPivot");
        _summary = GetNode<Label>("%Summary");
        _mechInfo = GetNode<Label>("%MechInfo");
        _mapInfo = GetNode<Label>("%MapInfo");
        _enemyCountLabel = GetNode<Label>("%EnemyCountLabel");
        _slotList = GetNode<VBoxContainer>("%SlotList");
        _click = GetNodeOrNull<AudioStreamPlayer>("%Click");
        _vehicleCountLabel = GetNode<Label>("%VehicleCountLabel");
        _insigniaPreview = GetNode<TextureRect>("%InsigniaPreview");
        _insigniaDialog = GetNode<FileDialog>("%InsigniaDialog");

        _pages = new[] { GetNode<Control>("%PageMech"), GetNode<Control>("%PageCustom"), GetNode<Control>("%PageBattle"), GetNode<Control>("%PageSettings"), GetNode<Control>("%PageHangar") };
        GetNode<Button>("%BtnHangar").Pressed += () => ShowPage(4);
        _shelf = GetNode<HardpointDrop>("%Shelf");
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
        // Ready-made schemes: choosing one fills in the colours and the pattern below.
        var preset = GetNode<OptionButton>("%PresetOption");
        var pattern = GetNode<OptionButton>("%PatternOption");
        var basePick = GetNode<ColorPickerButton>("%BasePicker");
        var aPick = GetNode<ColorPickerButton>("%PatternAPicker");
        var bPick = GetNode<ColorPickerButton>("%PatternBPicker");
        preset.Clear();
        preset.AddItem("— свои цвета —");
        foreach (var scheme0 in PaintJob.Presets) preset.AddItem(scheme0.Name);
        preset.ItemSelected += index =>
        {
            if (index < 1) return;
            var chosen = PaintJob.Presets[index - 1];
            GameConfig.PaintBase = basePick.Color = chosen.Base;
            GameConfig.PatternId = pattern.Selected = chosen.Pattern;
            GameConfig.PatternA = aPick.Color = chosen.A;
            GameConfig.PatternB = bPick.Color = chosen.B;
            RebuildPreview();
        };
        pattern.Clear();
        foreach (string name in PaintJob.PatternNames) pattern.AddItem(name);
        pattern.Selected = GameConfig.PatternId;
        pattern.ItemSelected += index =>
        {
            GameConfig.PatternId = (int)index;
            preset.Selected = 0;
            RebuildPreview();
        };
        Picker("%BasePicker", GameConfig.PaintBase, c => GameConfig.PaintBase = c);
        Picker("%PatternAPicker", GameConfig.PatternA, c => GameConfig.PatternA = c);
        Picker("%PatternBPicker", GameConfig.PatternB, c => GameConfig.PatternB = c);

        var stripe = GetNode<ColorPickerButton>("%StripePicker");
        stripe.Color = GameConfig.StripeColor;
        stripe.ColorChanged += color =>
        {
            GameConfig.StripeColor = color;
            RebuildPreview();
        };

        var scheme = GetNode<OptionButton>("%SchemeOption");
        scheme.Clear();
        foreach (string name in PaintJob.SchemeNames) scheme.AddItem(name);
        scheme.Selected = GameConfig.PaintScheme;
        scheme.ItemSelected += index =>
        {
            GameConfig.PaintScheme = (int)index;
            RebuildPreview();
        };

        Picker("%GlassPicker", GameConfig.GlassColor, c => GameConfig.GlassColor = c);
        Slider("%WearSlider", GameConfig.Wear, v => GameConfig.Wear = v);
        Slider("%DirtSlider", GameConfig.Dirt, v => GameConfig.Dirt = v);
        Slider("%GlossSlider", GameConfig.Gloss, v => GameConfig.Gloss = v);
        Slider("%PatternSlider", GameConfig.PatternSize, v => GameConfig.PatternSize = v);
        Slider("%InsigniaSizeSlider", GameConfig.InsigniaSize, v => GameConfig.InsigniaSize = v);

        GetNode<Button>("%BtnStock").Pressed += () =>
        {
            GameConfig.Loadouts.Remove(GameConfig.MechId);
            RebuildPreview();
            RebuildSlots();
        };

        // Emblem: any picture from disk.
        GetNode<Button>("%BtnInsignia").Pressed += () => _insigniaDialog.PopupCentered(new Vector2I(900, 600));
        GetNode<Button>("%BtnInsigniaClear").Pressed += () => SetInsignia("");
        _insigniaDialog.FileSelected += SetInsignia;
        _insigniaPreview.Texture = GameConfig.Insignia;

    }

    void Picker(string name, Color value, System.Action<Color> set)
    {
        var picker = GetNode<ColorPickerButton>(name);
        picker.Color = value;
        picker.ColorChanged += color =>
        {
            set(color);
            RebuildPreview();
        };
    }

    void Slider(string name, float value, System.Action<float> set)
    {
        var slider = GetNode<HSlider>(name);
        slider.Value = value;
        // The model is rebuilt when the slider is let go, not at every step of the drag.
        slider.ValueChanged += v => set((float)v);
        slider.DragEnded += _ => RebuildPreview();
    }

    void SetInsignia(string path)
    {
        GameConfig.InsigniaPath = path;
        _insigniaPreview.Texture = GameConfig.Insignia;
        RebuildPreview();
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

        var day = GetNode<OptionButton>("%TimeOfDayOption");
        day.Clear();
        foreach (string name in MapLighting.TimeNames) day.AddItem(name);
        day.Selected = GameConfig.TimeOfDay;
        day.ItemSelected += index => GameConfig.TimeOfDay = (int)index;

        var weather = GetNode<OptionButton>("%WeatherOption");
        weather.Clear();
        foreach (string name in Weather.Names) weather.AddItem(name);
        weather.Selected = GameConfig.WeatherId;
        weather.ItemSelected += index => GameConfig.WeatherId = (int)index;

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

    void Refresh()
    {
        var mech = Catalog.Find(Catalog.PlayerUnits, GameConfig.MechId);
        var map = Catalog.Find(Catalog.Maps, GameConfig.MapId);
        _mechInfo.Text = mech.Description;
        _mapInfo.Text = map.Description;
        _enemyCountLabel.Text = GameConfig.EnemyCount.ToString();
        _vehicleCountLabel.Text = GameConfig.EnemyVehicleCount.ToString();
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
        GameConfig.Loadouts.TryGetValue(GameConfig.MechId, out string[] loadout);
        Hardpoint.Apply(_preview, loadout);
        if (_preview is Mech mech)
        {
            mech.ApplyPaint(GameConfig.PlayerPaint());
            mech.ApplyInsignia(GameConfig.Insignia);
        }
        else if (_preview is Vehicle vehicle)
        {
            vehicle.ApplyPaint(GameConfig.PlayerPaint());
            vehicle.ApplyInsignia(GameConfig.Insignia);
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

    /// <summary>
    /// The hangar. On the left the shelf: every weapon the chosen machine has a mount for. On the right
    /// one panel per hardpoint, in the colour of the sort of weapon it takes, with its slots and what is
    /// fitted. Weapons are dragged from the shelf into a hardpoint (if it is the right sort and there
    /// is room), from one hardpoint to another, or back to the shelf to take them off.
    /// </summary>
    void RebuildSlots()
    {
        foreach (Node child in _slotList.GetChildren()) child.QueueFree();
        var shelfList = _shelf.GetNode<VBoxContainer>("Column/Weapons");
        foreach (Node child in shelfList.GetChildren()) child.QueueFree();

        var stockNode = SpawnStock();
        var points = Hardpoint.Of(stockNode);
        string[] stock = Hardpoint.Stock(stockNode);
        GameConfig.Loadouts.TryGetValue(GameConfig.MechId, out string[] chosen);
        if (chosen == null || chosen.Length != stock.Length) chosen = stock;
        var types = new SlotType[points.Count];
        var room = new int[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            types[i] = points[i].Type;
            room[i] = points[i].Slots;
        }
        var fits = new List<(string id, int group)>[points.Count];
        for (int i = 0; i < points.Count; i++) fits[i] = new List<(string id, int group)>(Hardpoint.Parse(chosen[i]));

        int Used(int point, int except = -1)
        {
            int used = 0;
            for (int k = 0; k < fits[point].Count; k++)
                if (k != except) used += Info(fits[point][k].id)?.Slots ?? 0;
            return used;
        }

        void Commit(bool rebuild)
        {
            var now = new string[fits.Length];
            for (int i = 0; i < fits.Length; i++) now[i] = Hardpoint.Format(fits[i]);
            GameConfig.Loadouts[GameConfig.MechId] = now;
            if (!rebuild) return;
            _click?.Play();
            RebuildPreview();
            // Not from inside the signal of a control that is about to be freed.
            Callable.From(RebuildSlots).CallDeferred();
        }

        // The shelf: taking a fitted weapon back.
        _shelf.GetNode<Label>("Column/Header/Name").Text = "Полка";
        _shelf.GetNode<Label>("Column/Header/Type").Text = "";
        _shelf.GetNode<Label>("Column/Header/Pips").Text = "";
        _shelf.GetNode<Label>("Column/Empty").Text = points.Count == 0 ? "у этой машины нет точек подвески" : "";
        _shelf.Accepts = (_, from, _) => from >= 0;
        _shelf.Dropped = (_, from, index) =>
        {
            if (from < 0 || index < 0 || index >= fits[from].Count) return;
            fits[from].RemoveAt(index);
            Commit(true);
        };
        foreach (var w in Weapons())
        {
            bool mountable = false;
            for (int i = 0; i < points.Count; i++) mountable |= Hardpoint.Fits(types[i], w.Slot) && w.Slots <= room[i];
            if (!mountable) continue;
            var chip = SlotRowScene.Instantiate<WeaponChip>();
            shelfList.AddChild(chip);
            Dress(chip, w, w.Id);
            chip.GetNode<Control>("Row/Group").Visible = false;
            chip.GetNode<Control>("Row/Remove").Visible = false;
        }

        for (int i = 0; i < points.Count; i++)
        {
            int point = i;
            Hardpoint hp = points[i];
            int used = Used(i);

            var row = HardpointRowScene.Instantiate<HardpointDrop>();
            _slotList.AddChild(row);
            Color colour = SlotColors[(int)hp.Type];
            Tint(row, colour);
            row.GetNode<Label>("Column/Header/Name").Text = hp.Title;
            var type = row.GetNode<Label>("Column/Header/Type");
            type.Text = SlotNames[(int)hp.Type];
            type.AddThemeColorOverride("font_color", colour);
            var pips = row.GetNode<Label>("Column/Header/Pips");
            pips.Text = Pips(used, hp.Slots - used);
            pips.AddThemeColorOverride("font_color", colour);
            row.GetNode<Control>("Column/Empty").Visible = fits[i].Count == 0;

            // What may be dropped here: the right sort, and no bigger than the room left (a weapon
            // moved within its own hardpoint doesn't count against itself).
            row.Accepts = (id, from, index) =>
            {
                var info = Info(id);
                if (info == null || !Hardpoint.Fits(types[point], info.Slot)) return false;
                if (from == point) return false;
                return Used(point) + info.Slots <= room[point];
            };
            row.Dropped = (id, from, index) =>
            {
                int group = 0;
                if (from >= 0 && index >= 0 && index < fits[from].Count)
                {
                    group = fits[from][index].group;
                    fits[from].RemoveAt(index);
                }
                fits[point].Add((id, group));
                Commit(true);
            };

            var list = row.GetNode<VBoxContainer>("Column/Weapons");
            for (int k = 0; k < fits[i].Count; k++)
            {
                int at = k;
                var info = Info(fits[i][k].id);
                var chip = SlotRowScene.Instantiate<WeaponChip>();
                list.AddChild(chip);
                Dress(chip, info, fits[i][k].id);
                chip.Point = point;
                chip.Index = k;
                // Which trigger it is on: its usual group unless the player chose otherwise.
                var group = chip.GetNode<OptionButton>("Row/Group");
                group.Selected = fits[i][k].group;
                group.ItemSelected += index =>
                {
                    fits[point][at] = (fits[point][at].id, (int)index);
                    Commit(false);
                };
                chip.GetNode<Button>("Row/Remove").Pressed += () =>
                {
                    fits[point].RemoveAt(at);
                    Commit(true);
                };
            }
        }
        stockNode.Free();
    }

    static string Pips(int full, int empty)
        => string.Join(" ", System.Linq.Enumerable.Concat(System.Linq.Enumerable.Repeat("■", full), System.Linq.Enumerable.Repeat("□", Mathf.Max(0, empty))));

    /// <summary>Gives a panel the colour of a sort of weapon along its edge.</summary>
    static void Tint(Control panel, Color colour)
    {
        if (panel.GetThemeStylebox("panel") is not StyleBoxFlat box) return;
        var own = (StyleBoxFlat)box.Duplicate();
        own.BorderColor = colour;
        panel.AddThemeStyleboxOverride("panel", own);
    }

    static void Dress(WeaponChip chip, WeaponInfo info, string id)
    {
        chip.WeaponId = id;
        chip.GetNode<Label>("Row/Name").Text = info?.Name ?? id;
        var size = chip.GetNode<Label>("Row/Size");
        size.Text = Pips(info?.Slots ?? 1, 0);
        if (info == null) return;
        Color colour = SlotColors[(int)info.Slot];
        size.AddThemeColorOverride("font_color", colour);
        Tint(chip, colour);
    }
}
