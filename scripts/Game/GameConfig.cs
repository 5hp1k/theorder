using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Choices made in the menus. Lives for the whole run of the game and is saved to user://settings.cfg.
/// </summary>
public static class GameConfig
{
    const string FilePath = "user://settings.cfg";

    /// <summary>Actions the player can rebind from the customisation page.</summary>
    public static readonly string[] BindableActions =
    {
        "throttle_up", "throttle_down", "turn_left", "turn_right", "full_stop", "jump", "descend",
        "fire_group_1", "fire_group_2", "fire_group_3", "fire_group_4", "fire_group_5", "fire_group_6",
        "target_nearest", "target_next", "target_reticle", "target_friendly", "center_torso", "arm_look", "counter_rotate", "free_look", "zoom",
        "toggle_view", "toggle_lights", "radar_mode", "scoreboard", "toggle_help", "toggle_hud", "toggle_fps",
    };
    /// <summary>What the actions above are called on the controls page, in the same order.</summary>
    public static readonly string[] ActionNames =
    {
        "Газ вперёд", "Газ назад", "Поворот влево", "Поворот вправо", "Полный стоп", "Прыжок / вверх", "Вниз (вертолёт)",
        "Огонь: группа 1", "Огонь: группа 2", "Огонь: группа 3", "Огонь: группа 4", "Огонь: группа 5", "Огонь: группа 6",
        "Цель: ближайшая", "Цель: следующая", "Цель: под прицелом", "Цель: свой (следующий)", "Торс по ногам", "Руки отдельно от торса (держать)", "Контрвращение торса", "Свободный обзор (держать)", "Приближение",
        "Кабина / внешний вид", "Фары", "Режим радара", "Таблица боя (держать)", "Справка", "Интерфейс вкл/выкл", "Счётчик кадров",
    };

    // ── Battle ──────────────────────────────────────────────────
    public static string MapId = "desert";
    /// <summary>Id of a mech or a vehicle, or "infantry".</summary>
    public static string MechId = "ostrich";
    public static int EnemyCount = 4;
    /// <summary>Tanks, aircraft and so on, on top of the mechs.</summary>
    public static int EnemyVehicleCount = 2;
    public static bool EnemyRespawn = true;
    /// <summary>The player's lancemates come back after they are destroyed.</summary>
    public static bool SquadRespawn = true;
    /// <summary>
    /// Mechs and vehicles the enemy does not field (catalog ids): the boxes left unticked on the battle
    /// page. Everything else — anything added to the catalog later included — may be spawned.
    /// </summary>
    public static readonly HashSet<string> EnemyExcluded = new();

    /// <summary>Those of a catalog list the enemy may field.</summary>
    public static Catalog.Entry[] EnemyPool(Catalog.Entry[] list)
        => System.Array.FindAll(list, e => !EnemyExcluded.Contains(e.Id));

    /// <summary>Leaves the enemy just one sort of mech (or, with "random", all of them).</summary>
    static void OnlyEnemyMech(string id)
    {
        foreach (var e in Catalog.Mechs) EnemyExcluded.Remove(e.Id);
        if (id == "random" || Catalog.Find(Catalog.Mechs, id).Id != id) return;
        foreach (var e in Catalog.Mechs)
            if (e.Id != id) EnemyExcluded.Add(e.Id);
    }
    /// <summary>0 = easy, 1 = normal, 2 = hard: scales enemy weapon damage.</summary>
    public static int Difficulty = 1;
    /// <summary>Free-for-all: the bots also shoot each other.</summary>
    public static bool BotsFight;
    /// <summary>Whether computer pilots may drop the nuclear bomb (once a battle, on a cluster of targets).</summary>
    public static bool BotNukes;
    /// <summary>Whether computer pilots drop electromagnetic bombs.</summary>
    public static bool BotEmp = true;
    /// <summary>Battle length in minutes; 0 = no limit.</summary>
    public static int TimeLimitMinutes;
    /// <summary>A mech's foot coming down on a tank or an armoured car destroys it (and they don't block mechs).</summary>
    public static bool MechsCrush = true;
    /// <summary>Mechs of the player's own squad, 0..8.</summary>
    public static int SquadSize = 2;
    /// <summary>A mech id for the squad, "same" (as the player's) or "random".</summary>
    public static string SquadMechId = "random";
    /// <summary>Index into <see cref="MapLighting.TimeIds"/>; 0 = the hour the map was made for.</summary>
    public static int TimeOfDay;
    /// <summary>Index into <see cref="Weather.Scenes"/>; 0 = the map's own weather.</summary>
    public static int WeatherId;

    // ── Network game ────────────────────────────────────────────
    public static string PlayerName = "Пилот";
    public static string NetAddress = "127.0.0.1";
    public static int NetPort = Net.DefaultPort;

    // ── Customisation ───────────────────────────────────────────
    /// <summary>The colour of the paint, the pattern over it (index into <see cref="PaintJob.PatternNames"/>) and the pattern's two colours.</summary>
    public static Color PaintBase = new(0.74f, 0.62f, 0.42f);
    public static int PatternId = 1;
    public static Color PatternA = new(0.5f, 0.4f, 0.26f), PatternB = new(0.86f, 0.77f, 0.58f);
    /// <summary>The second colour, for the parts picked out by <see cref="PaintScheme"/>.</summary>
    public static Color StripeColor = new(0.33f, 0.36f, 0.2f);
    /// <summary>Which parts wear the second colour: index into <see cref="PaintJob.SchemeNames"/>.</summary>
    public static int PaintScheme;
    public static Color GlassColor = new(0.02f, 0.045f, 0.06f);
    public static float Wear = 0.5f, Dirt = 0.35f, Gloss = 0.45f, PatternSize = 1f, InsigniaSize = 1f;

    /// <summary>The player's paint, as chosen in the hangar.</summary>
    public static PaintJob PlayerPaint() => new()
    {
        Base = PaintBase, Pattern = PatternId, PatternA = PatternA, PatternB = PatternB,
        Second = StripeColor, Scheme = PaintScheme,
        Wear = Wear, Dirt = Dirt, Gloss = Gloss, PatternSize = PatternSize,
        Glass = GlassColor, InsigniaSize = InsigniaSize,
    };
    /// <summary>
    /// What is fitted in each hardpoint, per unit id (see <see cref="Hardpoint"/> for the format).
    /// Missing = the unit's stock weapons.
    /// </summary>
    public static readonly Dictionary<string, string[]> Loadouts = new();
    /// <summary>
    /// Equipment chosen in the hangar, per machine: "jump_jets" and "ams" on or off (see
    /// <see cref="MechSim.Equipment"/>). What isn't set here is as the machine's scene has it.
    /// </summary>
    public static readonly Dictionary<string, Dictionary<string, bool>> Fitted = new();

    /// <summary>The loadout to send a machine into battle with: the hardpoints, then its equipment.</summary>
    public static string[] LoadoutFor(string unit)
    {
        Loadouts.TryGetValue(unit, out string[] fit);
        if (!Fitted.TryGetValue(unit, out var kit) || kit.Count == 0) return fit;
        var list = new List<string>(fit ?? System.Array.Empty<string>());
        list.Add(Equipment.Pack(kit));
        return list.ToArray();
    }
    /// <summary>Rebound actions: action name → "k:physical keycode" or "m:mouse button".</summary>
    public static readonly Dictionary<string, string> Bindings = new();
    /// <summary>Picture file the player chose as an emblem; empty = none.</summary>
    public static string InsigniaPath = "";

    // ── Settings ────────────────────────────────────────────────
    /// <summary>"ru" or "en" (see <see cref="Loc"/>). Until the player chooses: Russian on a Russian system, English elsewhere.</summary>
    public static string Language = OS.GetLocaleLanguage() == "ru" ? "ru" : "en";
    public static float MasterVolume = 0.8f;
    /// <summary>Loudness of each kind of sound, 0..1, on its own audio bus (default_bus_layout.tres).</summary>
    public static float EffectsVolume = 1f, AmbienceVolume = 1f, SignalsVolume = 1f, VoiceVolume = 1f;
    /// <summary>Whose voice the cockpit computer speaks with (a <see cref="VoicePack"/> id); <see cref="VoiceOff"/> = silent.</summary>
    public static string Voice = "betty";
    public const string VoiceOff = "off";
    /// <summary>Audio buses and the setting for each (Master is the overall volume).</summary>
    static (string bus, System.Func<float> volume)[] Buses => new (string, System.Func<float>)[]
    {
        ("Master", () => MasterVolume), ("Effects", () => EffectsVolume), ("Ambience", () => AmbienceVolume),
        ("Signals", () => SignalsVolume), ("Voice", () => VoiceVolume),
    };
    public static float MouseSensitivity = 1f;
    public static float Fov = 75f;
    public static bool VSync = true;
    /// <summary>0 = a window, 1 = full screen without borders, 2 = exclusive full screen.</summary>
    public static int WindowMode;
    public static bool Fullscreen
    {
        get => WindowMode != 0;
        set => WindowMode = value ? 1 : 0;
    }
    /// <summary>Size of the window when it is a window; (0, 0) = leave it as it is.</summary>
    public static Vector2I WindowSize;
    public static readonly Vector2I[] WindowSizes = { new(0, 0), new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
    /// <summary>Frames per second are held to this; 0 = no limit.</summary>
    public static int MaxFps;
    public static readonly int[] FpsLimits = { 0, 30, 60, 75, 90, 120, 144, 165, 240 };
    /// <summary>The 3D picture is drawn at this share of the window's resolution and stretched to fit (0.5..1).</summary>
    public static float RenderScale = 1f;
    /// <summary>Multisampling of edges: 0 = off, 1 = 2×, 2 = 4×, 3 = 8×.</summary>
    public static int Msaa = 1;
    /// <summary>Smoothing done on the finished picture (FXAA).</summary>
    public static bool Fxaa = true;
    /// <summary>Smoothing over time (TAA): steadier, a little softer.</summary>
    public static bool Taa;
    /// <summary>0 = off, 1 = low, 2 = medium, 3 = high.</summary>
    public static int Shadows = 3;
    public static bool Ssao = true, Ssil = true, Sdfgi = true, VolumetricFog = true, Glow = true, WaterMirror = true;
    /// <summary>Share of the weather's particles that are drawn (rain, snow, sand).</summary>
    public static float WeatherDensity = 1f;
    /// <summary>How far small things are drawn, as a multiple of the usual (see <see cref="Lod"/>). Takes effect with the next battle.</summary>
    public static float DrawDistance = 1f;
    /// <summary>How far away effects that are only for the eye (muzzle flashes and smoke, sparks) are made at all, metres.</summary>
    public static float FxRange = 650f;
    /// <summary>
    /// How coarse a model may get before it switches to a simpler level of detail: the error, in pixels,
    /// that is let through (1 = none to see; higher = simpler models sooner, less for the graphics card).
    /// </summary>
    public static float MeshDetail = 1f;
    /// <summary>Share of the loose wreckage that hits and collapsing buildings throw out (see <see cref="Debris"/>).</summary>
    public static float DebrisAmount = 1f;
    /// <summary>Frames per second in a corner of the screen (F3).</summary>
    public static bool ShowFps;
    /// <summary>The preset the detailed settings were last filled from: 0 = low, 1 = medium, 2 = high, 3 = the player's own mix.</summary>
    public static int EffectsQuality = 2;

    /// <summary>Fills the detailed graphics settings from a preset (0 low, 1 medium, 2 high).</summary>
    public static void ApplyPreset(int preset)
    {
        EffectsQuality = preset;
        if (preset > 2) return;
        Sdfgi = VolumetricFog = Ssil = preset >= 2;
        Ssao = WaterMirror = preset >= 1;
        Glow = true;
        Shadows = preset + 1;
        Msaa = preset >= 2 ? 1 : 0;
        Fxaa = true;
        Taa = false;
        WeatherDensity = preset switch { 0 => 0.3f, 1 => 0.65f, _ => 1f };
        RenderScale = preset == 0 ? 0.8f : 1f;
        DrawDistance = preset switch { 0 => 0.7f, 1 => 1f, _ => 1.4f };
        FxRange = preset switch { 0 => 260f, 1 => 420f, _ => 650f };
        MeshDetail = preset switch { 0 => 4f, 1 => 2f, _ => 1f };
        DebrisAmount = preset switch { 0 => 0.4f, 1 => 0.7f, _ => 1f };
    }
    /// <summary>Simulation steps per second: 60 is half the work of 120, 120 is smoother on fast screens.</summary>
    public static int SimulationRate = 60;

    public static float EnemyDamageScale => Difficulty switch { 0 => 0.3f, 1 => 0.5f, _ => 0.8f };

    static bool _loaded;
    static Texture2D _insignia;
    static string _insigniaLoadedFrom;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) == Error.Ok)
        {
            MapId = (string)cfg.GetValue("battle", "map", MapId);
            MechId = (string)cfg.GetValue("battle", "mech", MechId);
            EnemyCount = (int)cfg.GetValue("battle", "enemy_count", EnemyCount);
            EnemyVehicleCount = (int)cfg.GetValue("battle", "enemy_vehicles", EnemyVehicleCount);
            EnemyRespawn = (bool)cfg.GetValue("battle", "enemy_respawn", EnemyRespawn);
            SquadRespawn = (bool)cfg.GetValue("battle", "squad_respawn", SquadRespawn);
            EnemyExcluded.Clear();
            if (cfg.HasSectionKey("battle", "enemy_excluded"))
                foreach (string id in (string[])cfg.GetValue("battle", "enemy_excluded")) EnemyExcluded.Add(id);
            // (Settings saved when the enemy had one sort of mech or a random mix.)
            else OnlyEnemyMech((string)cfg.GetValue("battle", "enemy_mech", "random"));
            Difficulty = (int)cfg.GetValue("battle", "difficulty", Difficulty);
            BotsFight = (bool)cfg.GetValue("battle", "bots_fight", BotsFight);
            BotNukes = (bool)cfg.GetValue("battle", "bot_nukes", BotNukes);
            BotEmp = (bool)cfg.GetValue("battle", "bot_emp", BotEmp);
            TimeLimitMinutes = (int)cfg.GetValue("battle", "time_limit", TimeLimitMinutes);
            MechsCrush = (bool)cfg.GetValue("battle", "mechs_crush", MechsCrush);
            SquadSize = Mathf.Clamp((int)cfg.GetValue("battle", "squad_size", SquadSize), 0, 8);
            SquadMechId = (string)cfg.GetValue("battle", "squad_mech", SquadMechId);
            TimeOfDay = Mathf.Clamp((int)cfg.GetValue("battle", "time_of_day", TimeOfDay), 0, MapLighting.TimeIds.Length - 1);
            WeatherId = Mathf.Clamp((int)cfg.GetValue("battle", "weather", WeatherId), 0, Weather.Scenes.Length - 1);
            PlayerName = (string)cfg.GetValue("net", "name", PlayerName);
            NetAddress = (string)cfg.GetValue("net", "address", NetAddress);
            NetPort = (int)cfg.GetValue("net", "port", NetPort);
            Net.Mode = (Net.GameMode)Mathf.Clamp((int)cfg.GetValue("net", "mode", (int)Net.Mode), 0, 2);
            Net.Respawns = (bool)cfg.GetValue("net", "respawns", Net.Respawns);
            PaintBase = (Color)cfg.GetValue("paint", "base", PaintBase);
            PatternId = Mathf.Clamp((int)cfg.GetValue("paint", "pattern", PatternId), 0, PaintJob.PatternNames.Length - 1);
            PatternA = (Color)cfg.GetValue("paint", "pattern_a", PatternA);
            PatternB = (Color)cfg.GetValue("paint", "pattern_b", PatternB);
            StripeColor = (Color)cfg.GetValue("paint", "stripe", StripeColor);
            InsigniaPath = (string)cfg.GetValue("paint", "insignia", InsigniaPath);
            PaintScheme = (int)cfg.GetValue("paint", "scheme", PaintScheme);
            GlassColor = (Color)cfg.GetValue("paint", "glass", GlassColor);
            Wear = (float)cfg.GetValue("paint", "wear", Wear);
            Dirt = (float)cfg.GetValue("paint", "dirt", Dirt);
            Gloss = (float)cfg.GetValue("paint", "gloss", Gloss);
            PatternSize = (float)cfg.GetValue("paint", "pattern_size", PatternSize);
            InsigniaSize = (float)cfg.GetValue("paint", "insignia_size", InsigniaSize);
            if (cfg.HasSection("equipment"))
                foreach (string unit in cfg.GetSectionKeys("equipment"))
                    Fitted[unit] = Equipment.Unpack((string)cfg.GetValue("equipment", unit));
            if (cfg.HasSection("hardpoints"))
                foreach (string unit in cfg.GetSectionKeys("hardpoints"))
                    Loadouts[unit] = (string[])cfg.GetValue("hardpoints", unit);
            if (cfg.HasSection("bindings"))
                foreach (string action in cfg.GetSectionKeys("bindings"))
                    Bindings[action] = (string)cfg.GetValue("bindings", action);
            Language = (string)cfg.GetValue("settings", "language", Language) == "en" ? "en" : "ru";
            MasterVolume = (float)cfg.GetValue("settings", "volume", MasterVolume);
            EffectsVolume = (float)cfg.GetValue("sound", "effects", EffectsVolume);
            AmbienceVolume = (float)cfg.GetValue("sound", "ambience", AmbienceVolume);
            SignalsVolume = (float)cfg.GetValue("sound", "signals", SignalsVolume);
            VoiceVolume = (float)cfg.GetValue("sound", "voice_volume", VoiceVolume);
            Voice = (string)cfg.GetValue("sound", "voice", Voice);
            MouseSensitivity = (float)cfg.GetValue("settings", "sensitivity", MouseSensitivity);
            Fov = (float)cfg.GetValue("settings", "fov", Fov);
            WindowMode = cfg.HasSectionKey("settings", "window_mode") ? (int)cfg.GetValue("settings", "window_mode", 0)
                       : (bool)cfg.GetValue("settings", "fullscreen", false) ? 1 : 0;
            WindowSize = (Vector2I)cfg.GetValue("settings", "window_size", WindowSize);
            MaxFps = (int)cfg.GetValue("settings", "max_fps", MaxFps);
            ShowFps = (bool)cfg.GetValue("settings", "show_fps", ShowFps);
            VSync = (bool)cfg.GetValue("settings", "vsync", VSync);
            EffectsQuality = (int)cfg.GetValue("settings", "effects", EffectsQuality);
            // Settings saved before the detailed ones existed carry only the preset.
            ApplyPreset(Mathf.Clamp(EffectsQuality, 0, 3));
            RenderScale = Mathf.Clamp((float)cfg.GetValue("graphics", "render_scale", RenderScale), 0.5f, 1f);
            Msaa = (int)cfg.GetValue("graphics", "msaa", Msaa);
            Fxaa = (bool)cfg.GetValue("graphics", "fxaa", Fxaa);
            Taa = (bool)cfg.GetValue("graphics", "taa", Taa);
            Shadows = (int)cfg.GetValue("graphics", "shadows", Shadows);
            Ssao = (bool)cfg.GetValue("graphics", "ssao", Ssao);
            Ssil = (bool)cfg.GetValue("graphics", "ssil", Ssil);
            Sdfgi = (bool)cfg.GetValue("graphics", "sdfgi", Sdfgi);
            VolumetricFog = (bool)cfg.GetValue("graphics", "volumetric_fog", VolumetricFog);
            Glow = (bool)cfg.GetValue("graphics", "glow", Glow);
            WaterMirror = (bool)cfg.GetValue("graphics", "water_mirror", WaterMirror);
            WeatherDensity = (float)cfg.GetValue("graphics", "weather_density", WeatherDensity);
            DrawDistance = (float)cfg.GetValue("graphics", "draw_distance", DrawDistance);
            FxRange = (float)cfg.GetValue("graphics", "fx_range", FxRange);
            MeshDetail = Mathf.Clamp((float)cfg.GetValue("graphics", "mesh_detail", MeshDetail), 0.5f, 8f);
            DebrisAmount = Mathf.Clamp((float)cfg.GetValue("graphics", "debris", DebrisAmount), 0.1f, 1f);
            SimulationRate = (int)cfg.GetValue("settings", "simulation_rate", SimulationRate);
        }
        ApplyQuickBattle();
        ApplySettings();
        foreach (var (action, code) in Bindings) ApplyBinding(action, code);
    }

    /// <summary>
    /// For testing: the environment variable THEORDER_BATTLE="unit,map,mechs,vehicles[,enemy mech[,time of day[,weather]]]" sets
    /// up a battle for this run without touching the saved choices (e.g. "tank,city,2,0,ostrich").
    /// </summary>
    static void ApplyQuickBattle()
    {
        string spec = OS.GetEnvironment("THEORDER_BATTLE");
        if (string.IsNullOrEmpty(spec)) return;
        string[] p = spec.Split(',');
        if (p.Length < 4) return;
        MechId = p[0];
        MapId = p[1];
        int.TryParse(p[2], out EnemyCount);
        int.TryParse(p[3], out EnemyVehicleCount);
        EnemyExcluded.Clear();
        OnlyEnemyMech(p.Length > 4 ? p[4] : "random");
        // (THEORDER_VEHICLES: the only vehicles the enemy fields, e.g. "spg,mlrs")
        string only = OS.GetEnvironment("THEORDER_VEHICLES");
        if (!string.IsNullOrEmpty(only))
            foreach (var e in Catalog.Vehicles)
                if (System.Array.IndexOf(only.Split(','), e.Id) < 0) EnemyExcluded.Add(e.Id);
        TimeOfDay = WeatherId = 0;
        BotsFight = OS.GetEnvironment("THEORDER_FFA") == "1";
        // (THEORDER_LANG: "ru" or "en")
        string lang = OS.GetEnvironment("THEORDER_LANG");
        if (lang is "ru" or "en") Language = lang;
        // (THEORDER_SQUAD: how many mechs in the player's squad)
        SquadSize = 0;
        int.TryParse(OS.GetEnvironment("THEORDER_SQUAD"), out SquadSize);
        SquadMechId = "random";
        MaxFps = 0;
        VSync = false;
        // (THEORDER_GFX: a quality preset 0..2, then optional switches to turn off, e.g. "2,-sdfgi,-mirror")
        string gfx = OS.GetEnvironment("THEORDER_GFX");
        if (!string.IsNullOrEmpty(gfx))
        {
            string[] g = gfx.Split(',');
            if (int.TryParse(g[0], out int preset)) ApplyPreset(preset);
            foreach (string off in g)
                switch (off)
                {
                    case "-sdfgi": Sdfgi = false; break;
                    case "-fog": VolumetricFog = false; break;
                    case "-ssil": Ssil = false; break;
                    case "-ssao": Ssao = false; break;
                    case "-mirror": WaterMirror = false; break;
                    case "-msaa": Msaa = 0; break;
                    case "-shadows": Shadows = 1; break;
                    case "-glow": Glow = false; break;
                }
        }
        if (p.Length > 5) int.TryParse(p[5], out TimeOfDay);
        if (p.Length > 6) int.TryParse(p[6], out WeatherId);
        TimeLimitMinutes = 0;
        WindowMode = 0;
        WindowSize = default;
        Loadouts.Clear();
        // (THEORDER_LOADOUT: what to fit, hardpoints separated by "|", e.g. "ppc:1|medium_laser:1|autocannon:2")
        string fit = OS.GetEnvironment("THEORDER_LOADOUT");
        if (!string.IsNullOrEmpty(fit)) Loadouts[MechId] = fit.Split('|');
        // (and THEORDER_INSIGNIA: a picture file to wear as the emblem)
        string emblem = OS.GetEnvironment("THEORDER_INSIGNIA");
        if (!string.IsNullOrEmpty(emblem)) InsigniaPath = emblem;
        // Nothing of this is to end up in the settings file.
        _quick = true;
    }

    static bool _quick;

    public static void Save()
    {
        if (_quick) return;
        var cfg = new ConfigFile();
        cfg.SetValue("battle", "map", MapId);
        cfg.SetValue("battle", "mech", MechId);
        cfg.SetValue("battle", "enemy_count", EnemyCount);
        cfg.SetValue("battle", "enemy_vehicles", EnemyVehicleCount);
        cfg.SetValue("battle", "enemy_respawn", EnemyRespawn);
        cfg.SetValue("battle", "squad_respawn", SquadRespawn);
        var excluded = new string[EnemyExcluded.Count];
        EnemyExcluded.CopyTo(excluded);
        cfg.SetValue("battle", "enemy_excluded", excluded);
        cfg.SetValue("battle", "difficulty", Difficulty);
        cfg.SetValue("battle", "bots_fight", BotsFight);
        cfg.SetValue("battle", "bot_nukes", BotNukes);
        cfg.SetValue("battle", "bot_emp", BotEmp);
        cfg.SetValue("battle", "time_limit", TimeLimitMinutes);
        cfg.SetValue("battle", "mechs_crush", MechsCrush);
        cfg.SetValue("battle", "squad_size", SquadSize);
        cfg.SetValue("battle", "squad_mech", SquadMechId);
        cfg.SetValue("battle", "time_of_day", TimeOfDay);
        cfg.SetValue("battle", "weather", WeatherId);
        cfg.SetValue("net", "name", PlayerName);
        cfg.SetValue("net", "address", NetAddress);
        cfg.SetValue("net", "port", NetPort);
        cfg.SetValue("net", "mode", (int)Net.Mode);
        cfg.SetValue("net", "respawns", Net.Respawns);
        cfg.SetValue("paint", "base", PaintBase);
        cfg.SetValue("paint", "pattern", PatternId);
        cfg.SetValue("paint", "pattern_a", PatternA);
        cfg.SetValue("paint", "pattern_b", PatternB);
        cfg.SetValue("paint", "stripe", StripeColor);
        cfg.SetValue("paint", "insignia", InsigniaPath);
        cfg.SetValue("paint", "scheme", PaintScheme);
        cfg.SetValue("paint", "glass", GlassColor);
        cfg.SetValue("paint", "wear", Wear);
        cfg.SetValue("paint", "dirt", Dirt);
        cfg.SetValue("paint", "gloss", Gloss);
        cfg.SetValue("paint", "pattern_size", PatternSize);
        cfg.SetValue("paint", "insignia_size", InsigniaSize);
        foreach (var (unit, fit) in Loadouts) cfg.SetValue("hardpoints", unit, fit);
        foreach (var (unit, kit) in Fitted) cfg.SetValue("equipment", unit, Equipment.Pack(kit));
        foreach (var (action, code) in Bindings) cfg.SetValue("bindings", action, code);
        cfg.SetValue("settings", "language", Language);
        cfg.SetValue("settings", "volume", MasterVolume);
        cfg.SetValue("sound", "effects", EffectsVolume);
        cfg.SetValue("sound", "ambience", AmbienceVolume);
        cfg.SetValue("sound", "signals", SignalsVolume);
        cfg.SetValue("sound", "voice_volume", VoiceVolume);
        cfg.SetValue("sound", "voice", Voice);
        cfg.SetValue("settings", "sensitivity", MouseSensitivity);
        cfg.SetValue("settings", "fov", Fov);
        cfg.SetValue("settings", "window_mode", WindowMode);
        cfg.SetValue("settings", "window_size", WindowSize);
        cfg.SetValue("settings", "max_fps", MaxFps);
        cfg.SetValue("settings", "show_fps", ShowFps);
        cfg.SetValue("graphics", "render_scale", RenderScale);
        cfg.SetValue("graphics", "msaa", Msaa);
        cfg.SetValue("graphics", "fxaa", Fxaa);
        cfg.SetValue("graphics", "taa", Taa);
        cfg.SetValue("graphics", "shadows", Shadows);
        cfg.SetValue("graphics", "ssao", Ssao);
        cfg.SetValue("graphics", "ssil", Ssil);
        cfg.SetValue("graphics", "sdfgi", Sdfgi);
        cfg.SetValue("graphics", "volumetric_fog", VolumetricFog);
        cfg.SetValue("graphics", "glow", Glow);
        cfg.SetValue("graphics", "water_mirror", WaterMirror);
        cfg.SetValue("graphics", "weather_density", WeatherDensity);
        cfg.SetValue("graphics", "draw_distance", DrawDistance);
        cfg.SetValue("graphics", "fx_range", FxRange);
        cfg.SetValue("graphics", "mesh_detail", MeshDetail);
        cfg.SetValue("graphics", "debris", DebrisAmount);
        cfg.SetValue("settings", "vsync", VSync);
        cfg.SetValue("settings", "effects", EffectsQuality);
        cfg.SetValue("settings", "simulation_rate", SimulationRate);
        cfg.Save(FilePath);
    }

    /// <summary>Pushes the settings into the engine (audio volume, window mode, vsync).</summary>
    public static void ApplySettings()
    {
        foreach (var (name, volume) in Buses)
        {
            int bus = AudioServer.GetBusIndex(name);
            if (bus < 0) continue;
            float v = volume();
            AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(v, 0.0001f)));
            AudioServer.SetBusMute(bus, v <= 0.001f);
        }
        Engine.PhysicsTicksPerSecond = SimulationRate >= 120 ? 120 : 60;
        // No more simulation steps in one frame than keep the game in real time at 30 frames a second.
        // When a moment of battle costs more than that, letting the steps pile up only makes each frame
        // longer still (and the next one needs more steps): capped, it stays at 30 instead of sliding to 15.
        Engine.MaxPhysicsStepsPerFrame = Engine.PhysicsTicksPerSecond / 30;
        Engine.MaxFps = MaxFps;
        if (DisplayServer.GetName() == "headless") return;
        var want = WindowMode switch
        {
            1 => DisplayServer.WindowMode.Fullscreen,
            2 => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != want) DisplayServer.WindowSetMode(want);
        if (WindowMode == 0 && WindowSize.X > 0 && DisplayServer.WindowGetSize() != WindowSize)
        {
            DisplayServer.WindowSetSize(WindowSize);
            // Back to the middle of the screen it is on.
            Vector2I screen = DisplayServer.ScreenGetSize(), origin = DisplayServer.ScreenGetPosition();
            DisplayServer.WindowSetPosition(origin + (screen - WindowSize) / 2);
        }
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        // The picture itself: resolution of the 3D view and the smoothing of its edges.
        if (Engine.GetMainLoop() is SceneTree { Root: { } root })
        {
            root.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
            root.Scaling3DScale = Mathf.Clamp(RenderScale, 0.5f, 1f);
            root.Msaa3D = (Viewport.Msaa)Mathf.Clamp(Msaa, 0, 3);
            root.ScreenSpaceAA = Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
            root.UseTaa = Taa;
            root.MeshLodThreshold = MeshDetail;
        }
        // What the map has switched on or off is the map's own business (see MapLighting).
        MapLighting.Instance?.ApplyGraphics();
    }

    /// <summary>Puts every key back as the project has it.</summary>
    public static void ResetBindings()
    {
        Bindings.Clear();
        InputMap.LoadFromProjectSettings();
    }

    // ── Key bindings ────────────────────────────────────────────

    /// <summary>Puts an action on a single key or mouse button and remembers it.</summary>
    public static void Rebind(string action, InputEvent @event)
    {
        string code = @event switch
        {
            InputEventKey key => $"k:{(long)(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode)}",
            InputEventMouseButton mouse => $"m:{(long)mouse.ButtonIndex}",
            _ => null,
        };
        if (code == null) return;
        Bindings[action] = code;
        ApplyBinding(action, code);
    }

    static void ApplyBinding(string action, string code)
    {
        if (!InputMap.HasAction(action) || code.Length < 3 || !long.TryParse(code[2..], out long value)) return;
        InputEvent ev = code[0] == 'm'
            ? new InputEventMouseButton { ButtonIndex = (MouseButton)value }
            : new InputEventKey { PhysicalKeycode = (Key)value };
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, ev);
    }

    /// <summary>What an action is bound to, in words.</summary>
    public static string BindingText(string action)
    {
        var events = InputMap.ActionGetEvents(action);
        if (events.Count == 0) return "—";
        var parts = new List<string>();
        foreach (InputEvent ev in events) parts.Add(ev.AsText().Replace(" - Physical", "").Replace(" (Physical)", ""));
        return string.Join(" / ", parts);
    }

    // ── Emblem ──────────────────────────────────────────────────

    /// <summary>The player's emblem as a small PNG (128 pixels a side at most), to send to the other players; empty if there is none.</summary>
    public static byte[] InsigniaBytes()
    {
        Image image = Insignia?.GetImage();
        if (image == null) return System.Array.Empty<byte>();
        image = (Image)image.Duplicate();
        if (image.IsCompressed()) image.Decompress();
        image.ClearMipmaps();
        int side = Mathf.Max(image.GetWidth(), image.GetHeight());
        if (side > 128) image.Resize(Mathf.Max(1, image.GetWidth() * 128 / side), Mathf.Max(1, image.GetHeight() * 128 / side));
        return image.SavePngToBuffer();
    }

    /// <summary>An emblem received from another player.</summary>
    public static Texture2D InsigniaFrom(byte[] png)
    {
        if (png == null || png.Length == 0 || png.Length > 200_000) return null;
        var image = new Image();
        if (image.LoadPngFromBuffer(png) != Error.Ok) return null;
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The player's emblem picture, or null if none is set or the file can't be read.</summary>
    public static Texture2D Insignia
    {
        get
        {
            if (_insigniaLoadedFrom == InsigniaPath) return _insignia;
            _insigniaLoadedFrom = InsigniaPath;
            _insignia = null;
            if (string.IsNullOrEmpty(InsigniaPath) || !FileAccess.FileExists(InsigniaPath)) return null;
            var image = new Image();
            if (image.Load(InsigniaPath) != Error.Ok) return null;
            // Big photos are shrunk: it's a patch on a hull, not a poster.
            int side = Mathf.Max(image.GetWidth(), image.GetHeight());
            if (side > 512) image.Resize(image.GetWidth() * 512 / side, image.GetHeight() * 512 / side);
            image.GenerateMipmaps();
            _insignia = ImageTexture.CreateFromImage(image);
            return _insignia;
        }
    }
}
