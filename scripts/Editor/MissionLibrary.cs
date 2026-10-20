using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The player's own missions: kept one JSON file apiece in user://missions/, exported to and imported
/// from any file the player picks (a mission is one file, its characters' portraits inside it). Also the
/// kinds of mission the editor offers and what it starts each of them with.
/// </summary>
public static class MissionLibrary
{
    public const string Folder = "user://missions";
    public const string Extension = ".json";

    /// <summary>Kinds of mission: id, name, what it is about. A new mission of a kind starts filled in for it.</summary>
    public static readonly (string Id, string Name, string About)[] Types =
    {
        ("free", "Свободная", "Пустая карта: всё расставляется вручную."),
        ("destroy", "Уничтожение", "Найти и уничтожить группу врага."),
        ("defend", "Оборона", "Удержать позицию и сберечь своих, пока идут волны врага."),
        ("escort", "Сопровождение", "Провести колонну до точки назначения."),
        ("recon", "Разведка", "Дойти до точки, по пути — дополнительные цели."),
        ("assassinate", "Охота на командира", "Выследить и уничтожить именного вражеского пилота."),
        ("survive", "Выживание", "Продержаться против всё прибывающего врага."),
    };

    public static string TypeName(string id)
    {
        foreach (var t in Types) if (t.Id == id) return t.Name;
        return id;
    }

    static string PathOf(string id) => $"{Folder}/{id}{Extension}";

    /// <summary>Every mission kept, newest first.</summary>
    public static List<MissionDoc> All()
    {
        var list = new List<MissionDoc>();
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        using var dir = DirAccess.Open(Folder);
        if (dir == null) return list;
        foreach (string file in dir.GetFiles())
        {
            if (!file.EndsWith(Extension)) continue;
            if (Read($"{Folder}/{file}") is { } doc) list.Add(doc);
        }
        list.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
        return list;
    }

    static MissionDoc Read(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f == null ? null : MissionDoc.FromJson(f.GetAsText());
    }

    public static void Save(MissionDoc doc)
    {
        if (string.IsNullOrEmpty(doc.Id)) doc.Id = NewId();
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        using var f = FileAccess.Open(PathOf(doc.Id), FileAccess.ModeFlags.Write);
        f?.StoreString(doc.ToJson());
    }

    public static void Delete(string id)
    {
        if (!string.IsNullOrEmpty(id)) DirAccess.RemoveAbsolute(PathOf(id));
    }

    /// <summary>Writes the mission to a file of the player's choosing; false if it could not be written.</summary>
    public static bool Export(MissionDoc doc, string path)
    {
        if (!path.EndsWith(Extension)) path += Extension;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) return false;
        f.StoreString(doc.ToJson());
        return true;
    }

    /// <summary>A mission from a file into the library (as a new one, if one of that id is kept already); null if the file is not a mission.</summary>
    public static MissionDoc Import(string path)
    {
        var doc = Read(path);
        if (doc == null || string.IsNullOrWhiteSpace(doc.Title)) return null;
        if (string.IsNullOrEmpty(doc.Id) || FileAccess.FileExists(PathOf(doc.Id)) || !IsPlainId(doc.Id)) doc.Id = NewId();
        // Only what the game has: an unknown map or machine from a newer version falls back to one it knows.
        if (Catalog.Find(Catalog.Maps, doc.Map).Id != doc.Map) doc.Map = Catalog.Maps[0].Id;
        foreach (var u in doc.Units)
            if (Catalog.Find(Catalog.PlayerUnits, u.UnitId).Id != u.UnitId) u.UnitId = Catalog.Mechs[0].Id;
        Save(doc);
        return doc;
    }

    static bool IsPlainId(string id)
    {
        foreach (char c in id) if (!char.IsLetterOrDigit(c) && c != '_') return false;
        return id.Length is > 0 and < 40;
    }

    /// <summary>Sorts in the order missions were made.</summary>
    public static string NewId() => "m" + System.DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

    // ── New missions ────────────────────────────────────────────

    /// <summary>A new mission of the given kind on the given map, filled in for its kind.</summary>
    public static MissionDoc Create(string type, string map)
    {
        var doc = new MissionDoc { Id = NewId(), Type = type, Map = map, Title = Loc.T(TypeName(type)) };
        doc.Location = Loc.T(Catalog.Find(Catalog.Maps, map).Name);
        doc.Player.X = 0f;
        doc.Player.Z = 80f;
        doc.Endings.Add(new EndingDoc { Name = "Победа", Success = true });
        switch (type)
        {
            case "destroy":
                doc.Briefing = "Разведка засекла группу противника к северу от вас. Найдите её и уничтожьте.";
                Enemies(doc, "Цели", new[] { "hound", "cleaver", "ostrich" }, 0f, -260f);
                doc.Objectives.Add(new ObjectiveDoc { Name = "Уничтожить", Kind = "destroy", Text = "Уничтожить вражескую группу", Group = "Цели" });
                break;
            case "defend":
                doc.Briefing = "Удерживайте позицию: враг пойдёт волнами. Сберегите технику на базе.";
                doc.Units.Add(Unit(doc, "apc", "player", "Объект", 20f, 40f));
                doc.Units.Add(Unit(doc, "apc", "player", "Объект", -20f, 40f));
                doc.Objectives.Add(new ObjectiveDoc { Name = "Удержать", Kind = "survive", Text = "Продержаться до подхода своих", Seconds = 240f });
                doc.Objectives.Add(new ObjectiveDoc { Name = "Сберечь", Kind = "protect", Text = "Сберечь технику (можно потерять одну)", Group = "Объект", MayLose = 1 });
                for (int w = 0; w < 3; w++)
                {
                    string group = "Волна" + (w + 1);
                    var wave = Enemies(doc, group, new[] { "hound", "ostrich" }, (w - 1) * 160f, -320f, spawn: false, hunt: true);
                    var steps = new List<string>(wave);
                    doc.Triggers.Add(new TriggerDoc { Name = "Таймер" + (w + 1), Kind = "timer", Delay = 10f + w * 70f, Steps = steps });
                }
                break;
            case "escort":
                doc.Briefing = "Проведите колонну через опасный район до точки назначения.";
                for (int i = 0; i < 3; i++) doc.Units.Add(Unit(doc, "apc", "player", "Колонна", 0f, 60f - i * 18f, patrol: 0f));
                doc.Objectives.Add(new ObjectiveDoc { Name = "Колонна", Kind = "protect", Text = "Сберечь колонну", Group = "Колонна" });
                doc.Objectives.Add(new ObjectiveDoc { Name = "Точка", Kind = "reach", Text = "Дойти до точки назначения", Z = -320f, Radius = 50f });
                Enemies(doc, "Засада", new[] { "hound", "tank" }, 120f, -160f);
                break;
            case "recon":
                doc.Briefing = "Скрытно пройдите к точке наблюдения. Если сможете — уничтожьте вражеский пост.";
                doc.Objectives.Add(new ObjectiveDoc { Name = "Наблюдение", Kind = "reach", Text = "Дойти до точки наблюдения", X = 200f, Z = -300f, Radius = 40f });
                Enemies(doc, "Пост", new[] { "tank", "apc" }, -150f, -200f, patrol: 30f);
                doc.Objectives.Add(new ObjectiveDoc { Name = "Пост", Kind = "destroy", Text = "Уничтожить вражеский пост", Required = false, Group = "Пост" });
                break;
            case "assassinate":
                doc.Briefing = "Вожак противника, позывной «Ястреб», ведёт свою группу через этот район. Найдите его и уничтожьте.";
                doc.Pilots.Add(new PilotDoc { Id = "hawk", Name = "Арсений Гром", Callsign = "Ястреб", Bio = "Командир вражеской роты. Опытный и дерзкий.", Skills = new[] { 80, 70, 75, 80 } });
                var boss = Unit(doc, "executioner", "enemy", "Командир", 0f, -300f, patrol: 40f);
                boss.PilotId = "hawk";
                doc.Units.Add(boss);
                Enemies(doc, "Охрана", new[] { "hound", "hound" }, 0f, -260f);
                doc.Objectives.Add(new ObjectiveDoc { Name = "Ястреб", Kind = "destroy", Text = "Уничтожить «Ястреба»", Units = new List<string> { boss.Name } });
                doc.Radio.Add(new RadioDoc { Name = "Угроза", Lines = new List<LineDoc> { new() { Speaker = "hawk", Text = "Ещё один охотник? Посмотрим, на что ты годен." } } });
                doc.Triggers.Add(new TriggerDoc { Name = "Встреча", Kind = "area", X = 0f, Z = -300f, Radius = 220f, Steps = new List<string> { "Угроза" } });
                break;
            case "survive":
                doc.Briefing = "Вы отрезаны. Держитесь, пока не придёт помощь.";
                doc.Objectives.Add(new ObjectiveDoc { Name = "Выжить", Kind = "survive", Text = "Продержаться", Seconds = 300f });
                for (int w = 0; w < 4; w++)
                {
                    var wave = Enemies(doc, "Волна" + (w + 1), new[] { "hound", "cleaver" }, (w % 2 == 0 ? -1f : 1f) * 200f, -260f + w * 40f, spawn: false, hunt: true);
                    doc.Triggers.Add(new TriggerDoc { Name = "Таймер" + (w + 1), Kind = "timer", Delay = 5f + w * 60f, Steps = new List<string>(wave) });
                }
                break;
        }
        if (Loc.English) Translate(doc);
        return doc;
    }

    /// <summary>A new mission's own words in the language of the game (names alike everywhere they are used).</summary>
    static void Translate(MissionDoc doc)
    {
        static void List(List<string> l) { for (int i = 0; i < l.Count; i++) l[i] = Loc.T(l[i]); }
        doc.Briefing = Loc.T(doc.Briefing);
        foreach (var u in doc.Units) { u.Name = Loc.T(u.Name); u.Group = Loc.T(u.Group); }
        foreach (var o in doc.Objectives) { o.Name = Loc.T(o.Name); o.Text = Loc.T(o.Text); o.Group = Loc.T(o.Group); List(o.Units); List(o.OnDone); List(o.OnFailed); }
        foreach (var t in doc.Triggers) { t.Name = Loc.T(t.Name); t.Group = Loc.T(t.Group); List(t.Units); List(t.Steps); }
        foreach (var r in doc.Radio) { r.Name = Loc.T(r.Name); foreach (var l in r.Lines) { l.Text = Loc.T(l.Text); l.SpeakerName = Loc.T(l.SpeakerName); } }
        foreach (var e in doc.Endings) e.Name = Loc.T(e.Name);
        foreach (var p in doc.Pilots) { p.Name = Loc.T(p.Name); p.Callsign = Loc.T(p.Callsign); p.Bio = Loc.T(p.Bio); }
        List(doc.OnWon);
    }

    static UnitDoc Unit(MissionDoc doc, string unitId, string team, string group, float x, float z, float patrol = 70f)
    {
        var entry = Catalog.Find(Catalog.PlayerUnits, unitId);
        return new UnitDoc { Name = doc.FreeName(Loc.T(entry.Name)), UnitId = entry.Id, Team = team, Group = group, X = x, Z = z, Patrol = patrol, Yaw = team == "enemy" ? Mathf.Pi : 0f };
    }

    /// <summary>Enemy machines in a loose knot round a point; their names.</summary>
    static List<string> Enemies(MissionDoc doc, string group, string[] ids, float x, float z, bool spawn = true, bool hunt = false, float patrol = 70f)
    {
        var names = new List<string>();
        for (int i = 0; i < ids.Length; i++)
        {
            var u = Unit(doc, ids[i], "enemy", group, x + (i - (ids.Length - 1) * 0.5f) * 25f, z - (i % 2) * 15f, patrol);
            u.SpawnAtStart = spawn;
            u.HuntPlayer = hunt;
            doc.Units.Add(u);
            names.Add(u.Name);
        }
        return names;
    }
}
