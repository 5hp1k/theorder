using System;
using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>The panel of properties of what is selected, and the small form-making helpers the dialogs share.</summary>
public partial class MissionEditor
{
    VBoxContainer _form;

    void ShowInspector()
    {
        var box = GetNode<VBoxContainer>("%Inspector");
        foreach (Node child in box.GetChildren()) child.QueueFree();
        _form = box;
        _yawSpin = null;
        var heading = GetNode<Label>("%InspectorHeading");
        switch (_sel)
        {
            case PlayerDoc p: heading.Text = Loc.T("Старт игрока"); PlayerForm(p); break;
            case UnitDoc u: heading.Text = Loc.T("Машина"); UnitForm(u); break;
            case PropDoc pr: heading.Text = Loc.T("Объект на карте"); PropForm(pr); break;
            case ObjectiveDoc o: heading.Text = Loc.T("Цель"); ObjectiveForm(o); break;
            case TriggerDoc t: heading.Text = Loc.T("Триггер"); TriggerForm(t); break;
            case RadioDoc r: heading.Text = Loc.T("Рация"); RadioForm(r); break;
            case CutsceneDoc c: heading.Text = Loc.T("Катсцена"); CutsceneForm(c); break;
            case EndingDoc e: heading.Text = Loc.T("Концовка"); EndingForm(e); break;
            default:
                heading.Text = Loc.T("Свойства");
                Note(Loc.T("Выберите что-нибудь на карте или в списке слева. Добавить — кнопками слева сверху; настройки миссии, брифинг, звено игрока — кнопка «Миссия»; именные персонажи — «Персонажи»."));
                break;
        }
    }

    // ── Forms of the elements ───────────────────────────────────

    void PlayerForm(PlayerDoc p)
    {
        var ids = new List<string> { "" };
        var names = new List<string> { Loc.T("Выбор игрока (из ангара)") };
        foreach (var e in Catalog.PlayerUnits) { ids.Add(e.Id); names.Add(Loc.T(e.Name)); }
        Option(Loc.T("Машина"), names, Math.Max(ids.IndexOf(p.UnitId), 0), i => { p.UnitId = ids[i]; p.Loadout = Array.Empty<string>(); Remark(p); ShowInspector(); });
        if (p.UnitId.Length > 0)
            Button(Loc.T("Вооружение…"), () => OpenLoadout(p.UnitId, p.Loadout, l => { p.Loadout = l; Remark(p); }));
        YawSpin(p);
        Note(Loc.T("Звено игрока задаётся в настройках миссии."));
    }

    void UnitForm(UnitDoc u)
    {
        NameField(u.Name, n => u.Name = n, u);
        var names = new List<string>();
        foreach (var e in Catalog.PlayerUnits) names.Add(Loc.T(e.Name));
        Option(Loc.T("Тип"), names, Math.Max(Catalog.IndexOf(Catalog.PlayerUnits, u.UnitId), 0), i => { u.UnitId = Catalog.PlayerUnits[i].Id; u.Loadout = Array.Empty<string>(); Remark(u); Changed(true); });
        Option(Loc.T("Сторона"), new List<string> { Loc.T("Враг"), Loc.T("Свои") }, u.Team == "player" ? 1 : 0, i => { u.Team = i == 1 ? "player" : "enemy"; Remark(u); Changed(true); });
        PilotPick(Loc.T("Пилот"), u.PilotId, id => { u.PilotId = id; Remark(u); });
        Line(Loc.T("Подпись в бою"), u.DisplayName, v => u.DisplayName = v, Loc.T("пусто — позывной пилота или название"));
        Line(Loc.T("Группа"), u.Group, v => { u.Group = v; Changed(true); }, Loc.T("для целей и триггеров"));
        Button(Loc.T("Вооружение…"), () => OpenLoadout(u.UnitId, u.Loadout, l => { u.Loadout = l; Remark(u); }));
        Button(Loc.T("Окраска…"), () => OpenPaint(u.Paint, () => Remark(u)));
        Check(Loc.T("На поле с начала"), u.SpawnAtStart, v => { u.SpawnAtStart = v; Remark(u); }, Loc.T("иначе выходит, когда его назовёт шаг (подкрепление, засада)"));
        Spin(Loc.T("Радиус патруля, м"), 0, 600, 10, u.Patrol, v => u.Patrol = (float)v);
        Check(Loc.T("Сразу идёт на игрока"), u.HuntPlayer, v => u.HuntPlayer = v);
        YawSpin(u);
    }

    void PropForm(PropDoc p)
    {
        var ids = new List<string>();
        var names = new List<string>();
        foreach (var (group, items) in Catalog.Props)
            foreach (var e in items) { ids.Add(e.Id); names.Add(Loc.T(group) + " · " + Loc.T(e.Name)); }
        Option(Loc.T("Тип объекта"), names, Math.Max(ids.IndexOf(p.Kind), 0), i => { p.Kind = ids[i]; Remark(p); Changed(true); });
        YawSpin(p);
        Note(Loc.T("R / T — повернуть на 15°, Ctrl + колесо — точно, Alt + перетаскивание — повернуть к мыши, Ctrl+D — копия, Delete — убрать."));
    }

    Godot.SpinBox _yawSpin;

    /// <summary>Which way the element faces; kept up to date when it is turned on the map.</summary>
    void YawSpin(object element)
    {
        _yawSpin = SpinBox(-180, 180, 1, Mathf.RadToDeg(YawOf(element)), v => { SetYaw(element, Mathf.DegToRad((float)v)); Changed(); });
        Row(Loc.T("Поворот, °"), _yawSpin, Loc.T("R / T, Shift/Ctrl + колесо или Alt + перетаскивание на карте"));
    }

    void ObjectiveForm(ObjectiveDoc o)
    {
        NameField(o.Name, n => o.Name = n, o);
        var kinds = new[] { "destroy", "reach", "protect", "survive" };
        Option(Loc.T("Тип"), new List<string> { Loc.T("Уничтожить"), Loc.T("Дойти до точки"), Loc.T("Защитить"), Loc.T("Продержаться") },
               Array.IndexOf(kinds, o.Kind), i => { o.Kind = kinds[i]; Remark(o); ShowInspector(); });
        Text(Loc.T("Текст для игрока"), o.Text, v => o.Text = v);
        Check(Loc.T("Обязательная"), o.Required, v => o.Required = v, Loc.T("иначе — дополнительная"));
        Check(Loc.T("Дана с начала"), o.StartActive, v => o.StartActive = v, Loc.T("иначе её даст шаг"));
        if (o.Kind is "destroy" or "protect")
        {
            Line(Loc.T("Группа"), o.Group, v => o.Group = v);
            UnitsPick(Loc.T("И/или машины"), o.Units);
        }
        if (o.Kind == "protect") Spin(Loc.T("Можно потерять"), 0, 50, 1, o.MayLose, v => o.MayLose = (int)v);
        if (o.Kind == "reach") Spin(Loc.T("Радиус, м"), 5, 500, 5, o.Radius, v => { o.Radius = (float)v; Remark(o); });
        if (o.Kind == "survive") Spin(Loc.T("Секунд"), 5, 3600, 5, o.Seconds, v => o.Seconds = (float)v);
        StepsPick(Loc.T("Когда выполнена"), o.OnDone, o);
        StepsPick(Loc.T("Когда провалена"), o.OnFailed, o);
    }

    void TriggerForm(TriggerDoc t)
    {
        NameField(t.Name, n => t.Name = n, t);
        var kinds = new[] { "area", "timer", "destroyed" };
        Option(Loc.T("Тип"), new List<string> { Loc.T("Зона"), Loc.T("Таймер"), Loc.T("Уничтожены") },
               Array.IndexOf(kinds, t.Kind), i => { t.Kind = kinds[i]; Remark(t); ShowInspector(); });
        Check(Loc.T("Взведён с начала"), t.StartArmed, v => t.StartArmed = v, Loc.T("иначе его взведёт шаг"));
        Check(Loc.T("Однократный"), t.Once, v => t.Once = v);
        if (t.Kind == "area")
        {
            Spin(Loc.T("Радиус, м"), 5, 800, 5, t.Radius, v => { t.Radius = (float)v; Remark(t); });
            var who = new[] { "player", "side", "enemy" };
            Option(Loc.T("Кто входит"), new List<string> { Loc.T("Игрок"), Loc.T("Любой из своих"), Loc.T("Враг") }, Array.IndexOf(who, t.Who), i => t.Who = who[i]);
        }
        if (t.Kind == "timer") Spin(Loc.T("Через, с"), 0, 3600, 1, t.Delay, v => t.Delay = (float)v);
        if (t.Kind == "destroyed")
        {
            Line(Loc.T("Группа"), t.Group, v => t.Group = v);
            UnitsPick(Loc.T("И/или машины"), t.Units);
            Spin(Loc.T("Сколько (0 — все)"), 0, 50, 1, t.Count, v => t.Count = (int)v);
        }
        StepsPick(Loc.T("Тогда"), t.Steps, t);
    }

    void RadioForm(RadioDoc r)
    {
        NameField(r.Name, n => r.Name = n, r);
        LinesEditor(r.Lines);
    }

    void CutsceneForm(CutsceneDoc c)
    {
        NameField(c.Name, n => c.Name = n, c);
        Check(Loc.T("В начале миссии"), c.AtStart, v => c.AtStart = v);
        Check(Loc.T("Можно пропустить (Esc)"), c.Skippable, v => c.Skippable = v);
        Check(Loc.T("Бой замирает"), c.HoldBattle, v => c.HoldBattle = v);
        Header(Loc.T("Кадры (камера летит от кадра к кадру)"));
        for (int i = 0; i < c.Shots.Count; i++)
        {
            int at = i;
            var shot = c.Shots[i];
            var row = new HBoxContainer();
            _form.AddChild(row);
            var label = new Label { Text = $"{i + 1}.", CustomMinimumSize = new Vector2(22, 0) };
            if (i == _shot) label.AddThemeColorOverride("font_color", new Color(0.95f, 0.75f, 0.3f));
            row.AddChild(label);
            if (i > 0) row.AddChild(SpinBox(0.1, 120, 0.1, shot.Time, v => { shot.Time = (float)v; Changed(); }, Loc.T("с")));
            row.AddChild(SpinBox(10, 110, 1, shot.Fov, v => { shot.Fov = (float)v; Changed(); }, "°"));
            row.AddChild(SmallButton("👁", Loc.T("Встать камерой сюда"), () => { _camera.GlobalPosition = new Vector3(shot.X, shot.Y, shot.Z); _camera.Look(shot.Yaw, shot.Pitch); _camera.Fov = shot.Fov; Select(c, at); }));
            row.AddChild(SmallButton("⦿", Loc.T("Снять с камеры (как сейчас видно)"), () => { var s = ShotFromCamera(shot.Time); c.Shots[at] = s; Remark(c); Changed(); Select(c, at); }));
            if (c.Shots.Count > 1) row.AddChild(SmallButton("✕", Loc.T("Убрать кадр"), () => Remove(c, at)));
        }
        Button(Loc.T("Добавить кадр с камеры"), () => { c.Shots.Add(ShotFromCamera(3f)); Remark(c); Changed(); Select(c, c.Shots.Count - 1); });
        Button(Loc.T("▶ Просмотр"), () => Preview(c));
        Header(Loc.T("Реплики поверх"));
        LinesEditor(c.Lines);
        StepsPick(Loc.T("Когда закончилась"), c.OnFinished, c);
    }

    void EndingForm(EndingDoc e)
    {
        NameField(e.Name, n => e.Name = n, e);
        Option(Loc.T("Исход"), new List<string> { Loc.T("Победа"), Loc.T("Поражение") }, e.Success ? 0 : 1, i => { e.Success = i == 0; Changed(true); });
        if (!e.Success) Line(Loc.T("Причина"), e.Reason, v => e.Reason = v);
        Note(Loc.T("Концовку запускает шаг (цель, триггер, катсцена). Миссия и так выиграна, когда выполнены все обязательные цели."));
    }

    // ── Cutscene preview: the editor's camera flies the path ────

    Tween _preview;

    void Preview(CutsceneDoc c)
    {
        if (c.Shots.Count == 0) return;
        _preview?.Kill();
        _preview = CreateTween();
        var first = c.Shots[0];
        _camera.GlobalPosition = new Vector3(first.X, first.Y, first.Z);
        _camera.Look(first.Yaw, first.Pitch);
        _camera.Fov = first.Fov;
        for (int i = 1; i < c.Shots.Count; i++)
        {
            var from = c.Shots[i - 1];
            var to = c.Shots[i];
            _preview.TweenMethod(Callable.From<float>(k =>
            {
                var a = MissionBuilder.ShotTransform(from);
                var b = MissionBuilder.ShotTransform(to);
                _camera.GlobalTransform = new Transform3D(a.Basis.Slerp(b.Basis, k), a.Origin.Lerp(b.Origin, k));
                _camera.Fov = Mathf.Lerp(from.Fov, to.Fov, k);
            }), 0f, 1f, Mathf.Max(to.Time, 0.1f)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
        // (the camera's own idea of where it looks, for flying on from there)
        _preview.TweenCallback(Callable.From(() => { var r = _camera.GlobalBasis.GetEuler(); _camera.Look(r.Y, r.X); }));
    }

    // ── Pieces of forms ─────────────────────────────────────────

    void NameField(string name, Action<string> set, object element)
    {
        var edit = new LineEdit { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Row(Loc.T("Имя"), edit);
        edit.TextSubmitted += _ => edit.ReleaseFocus();
        edit.FocusExited += () =>
        {
            string to = edit.Text.Trim();
            string from = NameOf(element);
            if (to == from) return;
            bool taken = false;
            foreach (var (n, _) in _doc.Elements()) taken |= n == to;
            if (to.Length == 0 || taken || to is "Map" or "PlayerSpawn")
            {
                edit.Text = from;
                Say(Loc.T("Такое имя уже есть"));
                return;
            }
            set(to);
            Rename(from, to);
            Remark(element);
            Changed(true);
        };
    }

    Control Row(string label, Control control, string tip = null)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(130, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, TooltipText = tip ?? "" , MouseFilter = Control.MouseFilterEnum.Pass});
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (tip != null) control.TooltipText = tip;
        row.AddChild(control);
        _form.AddChild(row);
        return control;
    }

    void Header(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.75f, 0.3f));
        _form.AddChild(new HSeparator());
        _form.AddChild(label);
    }

    void Note(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) };
        label.AddThemeColorOverride("font_color", new Color(0.7f, 0.74f, 0.78f));
        _form.AddChild(label);
    }

    void Line(string label, string value, Action<string> set, string tip = null)
    {
        var edit = new LineEdit { Text = value ?? "", PlaceholderText = tip ?? "" };
        edit.TextChanged += v => { set(v); Changed(); };
        Row(label, edit, tip);
    }

    void Text(string label, string value, Action<string> set, float height = 70f)
    {
        var edit = new TextEdit { Text = value ?? "", CustomMinimumSize = new Vector2(0, height), WrapMode = TextEdit.LineWrappingMode.Boundary };
        edit.TextChanged += () => { set(edit.Text); Changed(); };
        _form.AddChild(new Label { Text = label });
        _form.AddChild(edit);
    }

    Godot.SpinBox SpinBox(double min, double max, double step, double value, Action<double> set, string suffix = "")
    {
        var spin = new Godot.SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        spin.ValueChanged += v => set(v);
        return spin;
    }

    void Spin(string label, double min, double max, double step, double value, Action<double> set)
        => Row(label, SpinBox(min, max, step, value, v => { set(v); Changed(); }));

    void Check(string label, bool value, Action<bool> set, string tip = null)
    {
        var box = new CheckBox { Text = label, ButtonPressed = value, TooltipText = tip ?? "" };
        box.Toggled += v => { set(v); Changed(); };
        _form.AddChild(box);
    }

    void Option(string label, List<string> items, int selected, Action<int> set)
    {
        var option = new OptionButton { FitToLongestItem = false };
        foreach (string item in items) option.AddItem(item);
        option.Selected = Mathf.Clamp(selected, 0, items.Count - 1);
        option.ItemSelected += i => { set((int)i); Changed(); };
        Row(label, option);
    }

    void Button(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        _form.AddChild(button);
    }

    static Button SmallButton(string text, string tip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(30, 0) };
        button.Pressed += pressed;
        return button;
    }

    /// <summary>One of the mission's characters (or none).</summary>
    void PilotPick(string label, string id, Action<string> set)
    {
        var ids = new List<string> { "" };
        var names = new List<string> { Loc.T("— обычный —") };
        foreach (var p in _doc.Pilots) { ids.Add(p.Id); names.Add(p.Label); }
        Option(label, names, Math.Max(ids.IndexOf(id ?? ""), 0), i => set(ids[i]));
    }

    /// <summary>A list to tick several of: the steps to set going (everything in the mission but the element itself).</summary>
    void StepsPick(string label, List<string> chosen, object self)
    {
        var names = new List<string>();
        foreach (var (name, kind) in _doc.Elements())
            if (name != NameOf(self)) names.Add(name);
        Pick(label, names, chosen, n =>
        {
            foreach (var (name, kind) in _doc.Elements())
                if (name == n) return kind switch { "unit" => Loc.T("выход машины"), "objective" => Loc.T("дать цель"), "trigger" => Loc.T("взвести"), "radio" => Loc.T("сказать"), "cutscene" => Loc.T("показать"), _ => Loc.T("закончить") };
            return "";
        });
    }

    void UnitsPick(string label, List<string> chosen)
    {
        var names = new List<string>();
        foreach (var u in _doc.Units) names.Add(u.Name);
        Pick(label, names, chosen, n => "");
    }

    void Pick(string label, List<string> names, List<string> chosen, Func<string, string> what)
    {
        _form.AddChild(new Label { Text = label });
        var list = new ItemList { SelectMode = ItemList.SelectModeEnum.Multi, CustomMinimumSize = new Vector2(0, Mathf.Clamp(names.Count, 2, 7) * 24 + 8) };
        foreach (string n in names)
        {
            string w = what(n);
            int i = list.AddItem(w.Length > 0 ? $"{n}  — {w}" : n);
            list.SetItemMetadata(i, n);
            if (chosen.Contains(n)) list.Select(i, false);
        }
        list.MultiSelected += (_, _) =>
        {
            chosen.Clear();
            foreach (int i in list.GetSelectedItems()) chosen.Add((string)list.GetItemMetadata(i));
            Changed();
        };
        if (names.Count == 0) Note(Loc.T("(пока нечего выбрать)"));
        else _form.AddChild(list);
    }

    /// <summary>Lines of speech: who says it (a character, or a name), what, how long it stays up.</summary>
    void LinesEditor(List<LineDoc> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            int at = i;
            var line = lines[i];
            var row = new HBoxContainer();
            _form.AddChild(row);
            var who = new OptionButton { FitToLongestItem = false, CustomMinimumSize = new Vector2(110, 0) };
            who.AddItem(Loc.T("Имя:"));
            var ids = new List<string> { "" };
            foreach (var p in _doc.Pilots) { who.AddItem(p.Label); ids.Add(p.Id); }
            who.Selected = Math.Max(ids.IndexOf(line.Speaker ?? ""), 0);
            row.AddChild(who);
            var name = new LineEdit { Text = line.SpeakerName, PlaceholderText = Loc.T("кто говорит"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = who.Selected == 0 };
            row.AddChild(name);
            who.ItemSelected += k => { line.Speaker = ids[(int)k]; name.Visible = k == 0; Changed(); };
            name.TextChanged += v => { line.SpeakerName = v; Changed(); };
            row.AddChild(SmallButton("✕", Loc.T("Убрать реплику"), () => { lines.RemoveAt(at); Changed(); ShowInspector(); }));
            var text = new TextEdit { Text = line.Text, CustomMinimumSize = new Vector2(0, 52), WrapMode = TextEdit.LineWrappingMode.Boundary };
            text.TextChanged += () => { line.Text = text.Text; Changed(); };
            _form.AddChild(text);
        }
        Button(Loc.T("Добавить реплику"), () => { lines.Add(new LineDoc { SpeakerName = Loc.T("Штаб") }); Changed(); ShowInspector(); });
    }
}
