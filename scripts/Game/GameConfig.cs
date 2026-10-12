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
    public static readonly string[] BindableActions = { "fire_group_1", "fire_group_2", "fire_group_3" };

    // ── Battle ──────────────────────────────────────────────────
    public static string MapId = "desert";
    /// <summary>Id of a mech or a vehicle, or "infantry".</summary>
    public static string MechId = "ostrich";
    public static int EnemyCount = 4;
    /// <summary>Tanks, aircraft and so on, on top of the mechs.</summary>
    public static int EnemyVehicleCount = 2;
    public static bool EnemyRespawn = true;
    /// <summary>A mech id, or "random".</summary>
    public static string EnemyMechId = "random";
    /// <summary>0 = easy, 1 = normal, 2 = hard: scales enemy weapon damage.</summary>
    public static int Difficulty = 1;
    /// <summary>Free-for-all: the bots also shoot each other.</summary>
    public static bool BotsFight;
    /// <summary>Battle length in minutes; 0 = no limit.</summary>
    public static int TimeLimitMinutes;
    /// <summary>A mech's foot coming down on a tank or an armoured car destroys it (and they don't block mechs).</summary>
    public static bool MechsCrush = true;

    // ── Customisation ───────────────────────────────────────────
    public static string CamoId = "desert";
    public static Color StripeColor = new(0.33f, 0.36f, 0.2f);
    /// <summary>Weapon ids per slot, per mech id. Missing = the mech's stock weapons.</summary>
    public static readonly Dictionary<string, string[]> Loadouts = new();
    /// <summary>Weapon group (1..3) per slot, per mech id. Missing = each weapon's own group.</summary>
    public static readonly Dictionary<string, int[]> Groups = new();
    /// <summary>Rebound actions: action name → "k:physical keycode" or "m:mouse button".</summary>
    public static readonly Dictionary<string, string> Bindings = new();
    /// <summary>Picture file the player chose as an emblem; empty = none.</summary>
    public static string InsigniaPath = "";

    // ── Settings ────────────────────────────────────────────────
    public static float MasterVolume = 0.8f;
    public static float MouseSensitivity = 1f;
    public static float Fov = 75f;
    public static bool Fullscreen;
    public static bool VSync = true;
    /// <summary>0 = low, 1 = medium, 2 = high.</summary>
    public static int EffectsQuality = 2;
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
            EnemyMechId = (string)cfg.GetValue("battle", "enemy_mech", EnemyMechId);
            Difficulty = (int)cfg.GetValue("battle", "difficulty", Difficulty);
            BotsFight = (bool)cfg.GetValue("battle", "bots_fight", BotsFight);
            TimeLimitMinutes = (int)cfg.GetValue("battle", "time_limit", TimeLimitMinutes);
            MechsCrush = (bool)cfg.GetValue("battle", "mechs_crush", MechsCrush);
            CamoId = (string)cfg.GetValue("paint", "camo", CamoId);
            StripeColor = (Color)cfg.GetValue("paint", "stripe", StripeColor);
            InsigniaPath = (string)cfg.GetValue("paint", "insignia", InsigniaPath);
            if (cfg.HasSection("loadout"))
                foreach (string mech in cfg.GetSectionKeys("loadout"))
                    Loadouts[mech] = (string[])cfg.GetValue("loadout", mech);
            if (cfg.HasSection("groups"))
                foreach (string mech in cfg.GetSectionKeys("groups"))
                    Groups[mech] = (int[])cfg.GetValue("groups", mech);
            if (cfg.HasSection("bindings"))
                foreach (string action in cfg.GetSectionKeys("bindings"))
                    Bindings[action] = (string)cfg.GetValue("bindings", action);
            MasterVolume = (float)cfg.GetValue("settings", "volume", MasterVolume);
            MouseSensitivity = (float)cfg.GetValue("settings", "sensitivity", MouseSensitivity);
            Fov = (float)cfg.GetValue("settings", "fov", Fov);
            Fullscreen = (bool)cfg.GetValue("settings", "fullscreen", Fullscreen);
            VSync = (bool)cfg.GetValue("settings", "vsync", VSync);
            EffectsQuality = (int)cfg.GetValue("settings", "effects", EffectsQuality);
            SimulationRate = (int)cfg.GetValue("settings", "simulation_rate", SimulationRate);
        }
        ApplySettings();
        foreach (var (action, code) in Bindings) ApplyBinding(action, code);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("battle", "map", MapId);
        cfg.SetValue("battle", "mech", MechId);
        cfg.SetValue("battle", "enemy_count", EnemyCount);
        cfg.SetValue("battle", "enemy_vehicles", EnemyVehicleCount);
        cfg.SetValue("battle", "enemy_respawn", EnemyRespawn);
        cfg.SetValue("battle", "enemy_mech", EnemyMechId);
        cfg.SetValue("battle", "difficulty", Difficulty);
        cfg.SetValue("battle", "bots_fight", BotsFight);
        cfg.SetValue("battle", "time_limit", TimeLimitMinutes);
        cfg.SetValue("battle", "mechs_crush", MechsCrush);
        cfg.SetValue("paint", "camo", CamoId);
        cfg.SetValue("paint", "stripe", StripeColor);
        cfg.SetValue("paint", "insignia", InsigniaPath);
        foreach (var (mech, slots) in Loadouts) cfg.SetValue("loadout", mech, slots);
        foreach (var (mech, groups) in Groups) cfg.SetValue("groups", mech, groups);
        foreach (var (action, code) in Bindings) cfg.SetValue("bindings", action, code);
        cfg.SetValue("settings", "volume", MasterVolume);
        cfg.SetValue("settings", "sensitivity", MouseSensitivity);
        cfg.SetValue("settings", "fov", Fov);
        cfg.SetValue("settings", "fullscreen", Fullscreen);
        cfg.SetValue("settings", "vsync", VSync);
        cfg.SetValue("settings", "effects", EffectsQuality);
        cfg.SetValue("settings", "simulation_rate", SimulationRate);
        cfg.Save(FilePath);
    }

    /// <summary>Pushes the settings into the engine (audio volume, window mode, vsync).</summary>
    public static void ApplySettings()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
        AudioServer.SetBusMute(0, MasterVolume <= 0.001f);
        Engine.PhysicsTicksPerSecond = SimulationRate >= 120 ? 120 : 60;
        if (DisplayServer.GetName() == "headless") return;
        var want = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != want) DisplayServer.WindowSetMode(want);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
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
