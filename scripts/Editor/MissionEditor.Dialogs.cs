using System;
using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>The editor's dialogs: a new mission, opening one, the mission's settings, its characters, a machine's weapons and paint.</summary>
public partial class MissionEditor
{
    void SetupDialogs()
    {
        var newDialog = GetNode<ConfirmationDialog>("%NewDialog");
        var type = GetNode<OptionButton>("%NewType");
        foreach (var t in MissionLibrary.Types) type.AddItem(Loc.T(t.Name));
        type.ItemSelected += i => GetNode<Label>("%NewAbout").Text = Loc.T(MissionLibrary.Types[i].About);
        var map = GetNode<OptionButton>("%NewMap");
        foreach (var m in Catalog.Maps) map.AddItem(Loc.T(m.Name));
        newDialog.Confirmed += () =>
        {
            var doc = MissionLibrary.Create(MissionLibrary.Types[Math.Max(type.Selected, 0)].Id, Catalog.Maps[Math.Max(map.Selected, 0)].Id);
            Load(doc);
            _dirty = true;
            RefreshTitle();
        };

        var open = GetNode<ConfirmationDialog>("%OpenDialog");
        var list = GetNode<ItemList>("%MissionList");
        open.Confirmed += () =>
        {
            var picked = list.GetSelectedItems();
            if (picked.Length > 0 && MissionLibrary.All().Find(d => d.Id == (string)list.GetItemMetadata(picked[0])) is { } doc) Load(doc);
        };
        list.ItemActivated += i => { open.Hide(); if (MissionLibrary.All().Find(d => d.Id == (string)list.GetItemMetadata((int)i)) is { } doc) Load(doc); };

        GetNode<AcceptDialog>("%SettingsDialog").Confirmed += () => { RefreshTitle(); if (_mapLoaded != _doc.Map) { LoadMap(true); } };
        GetNode<AcceptDialog>("%PilotsDialog").Confirmed += () => { RebuildMarks(); ShowInspector(); };
        GetNode<Button>("%BtnAddPilot").Pressed += () =>
        {
            var p = new PilotDoc { Id = "p" + System.DateTime.UtcNow.Ticks.ToString("x"), Name = Loc.T("Новый персонаж") };
            _doc.Pilots.Add(p);
            Changed();
            FillPilots(_doc.Pilots.Count - 1);
        };
        GetNode<Button>("%BtnRemovePilot").Pressed += () =>
        {
            var sel = GetNode<ItemList>("%PilotList").GetSelectedItems();
            if (sel.Length == 0) return;
            string id = _doc.Pilots[sel[0]].Id;
            _doc.Pilots.RemoveAt(sel[0]);
            foreach (var u in _doc.Units) if (u.PilotId == id) u.PilotId = "";
            foreach (var l in _doc.Lance) if (l.PilotId == id) l.PilotId = "";
            Changed();
            FillPilots(-1);
        };
        GetNode<ItemList>("%PilotList").ItemSelected += i => PilotForm((int)i);
        // The campaign's characters can be taken into a mission (copied: changing them here changes only the mission's).
        var campaign = GetNode<OptionButton>("%CampaignPilots");
        campaign.AddItem(Loc.T("Взять из кампании…"));
        var campaignPilots = new List<PilotProfile>();
        using (var dir = DirAccess.Open("res://campaign/pilots"))
            if (dir != null)
                foreach (string file in dir.GetFiles())
                    if (file.EndsWith(".tres") || file.EndsWith(".tres.remap"))
                        if (GD.Load<PilotProfile>("res://campaign/pilots/" + file.Replace(".remap", "")) is { } p) { campaignPilots.Add(p); campaign.AddItem(p.Label); }
        campaign.ItemSelected += i =>
        {
            campaign.Selected = 0;
            if (i == 0) return;
            var src = campaignPilots[(int)i - 1];
            _doc.Pilots.Add(new PilotDoc
            {
                Id = "c_" + src.Id, Name = src.Name, Callsign = src.Callsign, Bio = src.Bio,
                Skills = new[] { src.Gunnery, src.Piloting, src.Awareness, src.Aggression },
                Portrait = src.Portrait?.GetImage() is { } img ? System.Convert.ToBase64String(img.SavePngToBuffer()) : "",
            });
            Changed();
            FillPilots(_doc.Pilots.Count - 1);
        };
        GetNode<FileDialog>("%PortraitDialog").FileSelected += path =>
        {
            var sel = GetNode<ItemList>("%PilotList").GetSelectedItems();
            var image = Image.LoadFromFile(path);
            if (sel.Length == 0 || image == null || image.IsEmpty()) { Message(Loc.T("Не удалось открыть картинку.")); return; }
            // Kept small: it travels inside the mission's file.
            int side = Math.Max(image.GetWidth(), image.GetHeight());
            if (side > 256) image.Resize(image.GetWidth() * 256 / side, image.GetHeight() * 256 / side, Image.Interpolation.Lanczos);
            _doc.Pilots[sel[0]].Portrait = System.Convert.ToBase64String(image.SavePngToBuffer());
            Changed();
            PilotForm(sel[0]);
        };
    }

    void AskNew()
    {
        GetNode<Label>("%NewAbout").Text = Loc.T(MissionLibrary.Types[Math.Max(GetNode<OptionButton>("%NewType").Selected, 0)].About);
        var dialog = GetNode<ConfirmationDialog>("%NewDialog");
        // (its size set outright: a wrapped label must not stretch it past the screen)
        dialog.PopupCentered(new Vector2I(460, 260));
    }

    void AskOpen()
    {
        var list = GetNode<ItemList>("%MissionList");
        list.Clear();
        foreach (var doc in MissionLibrary.All())
        {
            int i = list.AddItem($"{doc.Title}   ·  {Loc.T(MissionLibrary.TypeName(doc.Type))}, {Loc.T(Catalog.Find(Catalog.Maps, doc.Map).Name)}");
            list.SetItemMetadata(i, doc.Id);
        }
        GetNode<ConfirmationDialog>("%OpenDialog").PopupCenteredClamped(new Vector2I(520, 460), 0.9f);
    }

    /// <summary>Runs a form-making action with its rows going into another box (a dialog's).</summary>
    void Into(VBoxContainer box, Action fill)
    {
        foreach (Node child in box.GetChildren()) child.QueueFree();
        var keep = _form;
        _form = box;
        fill();
        _form = keep;
    }

    // ── The mission's settings ──────────────────────────────────

    void OpenSettings()
    {
        Into(GetNode<VBoxContainer>("%SettingsForm"), SettingsForm);
        GetNode<AcceptDialog>("%SettingsDialog").PopupCenteredClamped(new Vector2I(720, 720), 0.9f);
    }

    void SettingsForm()
    {
        var d = _doc;
        Line(Loc.T("Название"), d.Title, v => d.Title = v);
        Line(Loc.T("Автор"), d.Author, v => d.Author = v);
        var typeNames = new List<string>();
        foreach (var t in MissionLibrary.Types) typeNames.Add(Loc.T(t.Name));
        Option(Loc.T("Тип"), typeNames, Array.FindIndex(MissionLibrary.Types, t => t.Id == d.Type), i => d.Type = MissionLibrary.Types[i].Id);
        Line(Loc.T("Место и время"), d.Location, v => d.Location = v);
        var maps = new List<string>();
        foreach (var m in Catalog.Maps) maps.Add(Loc.T(m.Name));
        Option(Loc.T("Карта"), maps, Math.Max(Catalog.IndexOf(Catalog.Maps, d.Map), 0), i => d.Map = Catalog.Maps[i].Id);
        var hours = new List<string>();
        foreach (string n in MapLighting.TimeNames) hours.Add(Loc.T(n));
        Option(Loc.T("Время суток"), hours, d.TimeOfDay, i => { d.TimeOfDay = i; _mapLoaded = ""; });
        var weathers = new List<string>();
        foreach (string n in Weather.Names) weathers.Add(Loc.T(n));
        Option(Loc.T("Погода"), weathers, d.Weather, i => { d.Weather = i; _mapLoaded = ""; });
        Spin(Loc.T("Лимит времени, мин (0 — нет)"), 0, 180, 1, d.TimeLimit, v => d.TimeLimit = (float)v);
        Check(Loc.T("Враг сбрасывает ядерные бомбы"), d.EnemyNukes, v => d.EnemyNukes = v);
        Check(Loc.T("Враг сбрасывает ЭМИ-бомбы"), d.EnemyEmp, v => d.EnemyEmp = v);
        Text(Loc.T("Брифинг"), d.Briefing, v => d.Briefing = v, 110f);
        Text(Loc.T("Итог при победе"), d.Debriefing, v => d.Debriefing = v);
        StepsPick(Loc.T("При победе"), d.OnWon, null);
        Header(Loc.T("Звено игрока (идёт строем за ним)"));
        for (int i = 0; i < d.Lance.Count; i++)
        {
            int at = i;
            var m = d.Lance[i];
            var units = new List<string>();
            foreach (var e in Catalog.PlayerUnits) units.Add(Loc.T(e.Name));
            Option($"{i + 1}. " + Loc.T("Машина"), units, Math.Max(Catalog.IndexOf(Catalog.PlayerUnits, m.UnitId), 0), k => { m.UnitId = Catalog.PlayerUnits[k].Id; m.Loadout = Array.Empty<string>(); });
            PilotPick(Loc.T("   пилот"), m.PilotId, id => m.PilotId = id);
            var row = new HBoxContainer();
            var arm = new Button { Text = Loc.T("Вооружение…") };
            arm.Pressed += () => OpenLoadout(m.UnitId, m.Loadout, l => { m.Loadout = l; Changed(); });
            var drop = new Button { Text = Loc.T("Убрать") };
            drop.Pressed += () => { d.Lance.RemoveAt(at); Changed(); Into(GetNode<VBoxContainer>("%SettingsForm"), SettingsForm); };
            row.AddChild(arm);
            row.AddChild(drop);
            _form.AddChild(row);
        }
        if (d.Lance.Count < 4)
            Button(Loc.T("Добавить в звено"), () => { d.Lance.Add(new LanceDoc()); Changed(); Into(GetNode<VBoxContainer>("%SettingsForm"), SettingsForm); });
    }

    // ── Characters ──────────────────────────────────────────────

    void OpenPilots()
    {
        FillPilots(_doc.Pilots.Count > 0 ? 0 : -1);
        GetNode<AcceptDialog>("%PilotsDialog").PopupCenteredClamped(new Vector2I(820, 620), 0.9f);
    }

    void FillPilots(int select)
    {
        var list = GetNode<ItemList>("%PilotList");
        list.Clear();
        foreach (var p in _doc.Pilots) list.AddItem(p.Label, MissionBuilder.Portrait(p.Portrait));
        if (select >= 0 && select < _doc.Pilots.Count) list.Select(select);
        PilotForm(select);
    }

    void PilotForm(int index)
    {
        Into(GetNode<VBoxContainer>("%PilotForm"), () =>
        {
            if (index < 0 || index >= _doc.Pilots.Count)
            {
                Note(Loc.T("Именные персонажи: пилоты машин (своих и врага), голоса по рации и в катсценах. Добавьте нового или возьмите из кампании."));
                return;
            }
            var p = _doc.Pilots[index];
            void Relabel() => GetNode<ItemList>("%PilotList").SetItemText(index, p.Label);
            Line(Loc.T("Имя"), p.Name, v => { p.Name = v; Relabel(); });
            Line(Loc.T("Позывной"), p.Callsign, v => { p.Callsign = v; Relabel(); });
            Text(Loc.T("Биография"), p.Bio, v => p.Bio = v);
            string[] skills = { "Стрельба", "Пилотирование", "Внимание", "Напор" };
            for (int s = 0; s < 4; s++)
            {
                int k = s;
                Spin(Loc.T(skills[s]), 0, 100, 5, p.Skills[k], v => p.Skills[k] = (int)v);
            }
            var face = new TextureRect { Texture = MissionBuilder.Portrait(p.Portrait), CustomMinimumSize = new Vector2(128, 128), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
            _form.AddChild(face);
            Button(Loc.T("Портрет из файла…"), () => GetNode<FileDialog>("%PortraitDialog").PopupCentered());
            if (p.Portrait.Length > 0) Button(Loc.T("Убрать портрет"), () => { p.Portrait = ""; Changed(); PilotForm(index); });
        });
    }

    // ── Weapons ─────────────────────────────────────────────────

    static readonly Dictionary<string, (string name, SlotType slot, int slots)> WeaponInfo = new();

    static (string name, SlotType slot, int slots) Weapon(string id)
    {
        if (WeaponInfo.TryGetValue(id, out var info)) return info;
        var entry = Catalog.Find(Catalog.Weapons, id);
        var w = GD.Load<PackedScene>(entry.Path).Instantiate<Weapon>();
        info = (entry.Name, w.Slot, w.Slots);
        w.Free();
        return WeaponInfo[id] = info;
    }

    /// <summary>What a machine carries, hardpoint by hardpoint: take off, add what fits, or go back to the factory's.</summary>
    void OpenLoadout(string unitId, string[] loadout, Action<string[]> set)
    {
        var unit = GD.Load<PackedScene>(Catalog.Find(Catalog.PlayerUnits, unitId).Path).Instantiate<Node3D>();
        var points = Hardpoint.Of(unit);
        string[] stock = Hardpoint.Stock(unit);
        var types = new List<(string title, SlotType type, int slots)>();
        foreach (var hp in points) types.Add((hp.Title, hp.Type, hp.Slots));
        unit.Free();
        string[] current = loadout is { Length: > 0 } && loadout.Length == stock.Length ? (string[])loadout.Clone() : (string[])stock.Clone();

        void Fill()
        {
            Into(GetNode<VBoxContainer>("%LoadoutForm"), () =>
            {
                if (types.Count == 0) { Note(Loc.T("У этой машины нет точек подвески.")); return; }
                Button(Loc.T("Как с завода"), () => { current = (string[])stock.Clone(); set(Array.Empty<string>()); Changed(); Fill(); });
                for (int i = 0; i < types.Count; i++)
                {
                    int point = i;
                    var fits = new List<(string id, int group, int ammo)>(Hardpoint.Parse(current[i]));
                    int used = 0;
                    foreach (var f in fits) used += Weapon(f.id).slots;
                    Header($"{Loc.T(types[i].title)}  ({used}/{types[i].slots})");
                    for (int k = 0; k < fits.Count; k++)
                    {
                        int at = k;
                        var row = new HBoxContainer();
                        row.AddChild(new Label { Text = Loc.T(Weapon(fits[k].id).name), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                        row.AddChild(SmallButton("✕", Loc.T("Снять"), () =>
                        {
                            fits.RemoveAt(at);
                            current[point] = Hardpoint.Format(fits);
                            set((string[])current.Clone());
                            Changed();
                            Fill();
                        }));
                        _form.AddChild(row);
                    }
                    var add = new OptionButton { FitToLongestItem = false };
                    add.AddItem(Loc.T("добавить…"));
                    var ids = new List<string>();
                    foreach (var e in Catalog.Weapons)
                    {
                        var w = Weapon(e.Id);
                        if (!Hardpoint.Fits(types[i].type, w.slot) || used + w.slots > types[i].slots) continue;
                        add.AddItem(Loc.T(w.name));
                        ids.Add(e.Id);
                    }
                    add.Disabled = ids.Count == 0;
                    add.ItemSelected += sel =>
                    {
                        if (sel == 0) return;
                        fits.Add((ids[(int)sel - 1], 0, 0));
                        current[point] = Hardpoint.Format(fits);
                        set((string[])current.Clone());
                        Changed();
                        Fill();
                    };
                    _form.AddChild(add);
                }
            });
        }
        Fill();
        GetNode<AcceptDialog>("%LoadoutDialog").PopupCenteredClamped(new Vector2I(560, 620), 0.9f);
    }

    // ── Paint ───────────────────────────────────────────────────

    void OpenPaint(PaintDoc paint, Action changed)
    {
        void Fill()
        {
            Into(GetNode<VBoxContainer>("%PaintForm"), () =>
            {
                var names = new List<string> { Loc.T("Обычная для стороны"), Loc.T("Своя") };
                foreach (var p in PaintJob.Presets) names.Add(Loc.T(p.Name));
                int selected = paint.Preset == -1 ? 0 : paint.Preset == -2 ? 1 : paint.Preset + 2;
                Option(Loc.T("Окраска"), names, selected, i =>
                {
                    paint.Preset = i == 0 ? -1 : i == 1 ? -2 : i - 2;
                    if (paint.Preset == -2 && paint.Custom.Length < 21) paint.Custom = (MissionBuilder.PaintOf(new PaintDoc { Preset = 0 }) ?? new PaintJob()).Pack();
                    changed();
                    Fill();
                });
                if (paint.Preset != -2) return;
                var job = PaintJob.Unpack(paint.Custom) ?? new PaintJob();
                void Commit() { paint.Custom = job.Pack(); Changed(); changed(); }
                Colour(Loc.T("Цвет"), job.Base, c => { job.Base = c; Commit(); });
                var patterns = new List<string>();
                foreach (string n in PaintJob.PatternNames) patterns.Add(Loc.T(n));
                Option(Loc.T("Узор"), patterns, job.Pattern, i => { job.Pattern = i; Commit(); });
                Colour(Loc.T("Цвет узора 1"), job.PatternA, c => { job.PatternA = c; Commit(); });
                Colour(Loc.T("Цвет узора 2"), job.PatternB, c => { job.PatternB = c; Commit(); });
                Spin(Loc.T("Размер узора"), 0.2, 4, 0.1, job.PatternSize, v => { job.PatternSize = (float)v; Commit(); });
                var schemes = new List<string>();
                foreach (string n in PaintJob.SchemeNames) schemes.Add(Loc.T(n));
                Option(Loc.T("Второй цвет на"), schemes, job.Scheme, i => { job.Scheme = i; Commit(); });
                Colour(Loc.T("Второй цвет"), job.Second, c => { job.Second = c; Commit(); });
                Spin(Loc.T("Износ"), 0, 1, 0.05, Math.Max(job.Wear, 0f), v => { job.Wear = (float)v; Commit(); });
                Spin(Loc.T("Грязь"), 0, 1, 0.05, Math.Max(job.Dirt, 0f), v => { job.Dirt = (float)v; Commit(); });
                Spin(Loc.T("Глянец"), 0, 1, 0.05, Math.Max(job.Gloss, 0f), v => { job.Gloss = (float)v; Commit(); });
            });
        }
        Fill();
        GetNode<AcceptDialog>("%PaintDialog").PopupCenteredClamped(new Vector2I(480, 560), 0.9f);
    }

    void Colour(string label, Color value, Action<Color> set)
    {
        var picker = new ColorPickerButton { Color = value, EditAlpha = false, CustomMinimumSize = new Vector2(0, 28) };
        picker.ColorChanged += c => set(c);
        Row(label, picker);
    }
}
