using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The developer console (scenes/ui/console.tscn, an autoload: in the menus and in battle alike). The
/// "toggle_console" key (` — ё on a Russian keyboard) opens and closes it; while it is open the keys do
/// not drive the machine. Up and down go through the commands typed before. "help" lists the commands.
/// </summary>
public partial class DevConsole : CanvasLayer
{
    Control _panel;
    RichTextLabel _log;
    LineEdit _input;
    readonly List<string> _history = new();
    int _back;
    Input.MouseModeEnum _mouseBefore;

    public bool IsOpen => _panel.Visible;

    public override void _Ready()
    {
        _panel = GetNode<Control>("Panel");
        _log = GetNode<RichTextLabel>("%Log");
        _input = GetNode<LineEdit>("%Input");
        _input.TextSubmitted += Submit;
        Print("[color=#f2c04c]Консоль. help — список команд.[/color]");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_console") && @event is InputEventKey { Echo: false })
        {
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!IsOpen || @event is not InputEventKey { Pressed: true } key) return;
        if (key.Keycode == Key.Escape)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Up or Key.Down && _history.Count > 0)
        {
            _back = Mathf.Clamp(_back + (key.Keycode == Key.Up ? 1 : -1), 0, _history.Count);
            _input.Text = _back == 0 ? "" : _history[^_back];
            _input.CaretColumn = _input.Text.Length;
            GetViewport().SetInputAsHandled();
        }
    }

    void Toggle()
    {
        bool open = !IsOpen;
        _panel.Visible = open;
        Game.Typing = open;
        if (open)
        {
            _mouseBefore = Input.MouseMode;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _input.Clear();
            _input.GrabFocus();
            _back = 0;
        }
        else
        {
            _input.ReleaseFocus();
            Input.MouseMode = _mouseBefore;
        }
    }

    void Print(string text) => _log.AppendText(Loc.T(text) + "\n");

    void Submit(string line)
    {
        _input.Clear();
        line = line.Trim();
        if (line.Length == 0) return;
        _history.Add(line);
        _back = 0;
        Print("[color=#8a9590]> " + line + "[/color]");
        string[] words = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        string command = words[0].ToLowerInvariant();
        string arg = words.Length > 1 ? words[1].ToLowerInvariant() : "";
        switch (command)
        {
            case "help": case "помощь": Help(); break;
            case "clear": _log.Clear(); break;
            case "god": Cheats.God = Switch(arg, Cheats.God); Print($"Бессмертие: {OnOff(Cheats.God)}"); break;
            case "ammo": Cheats.InfiniteAmmo = Switch(arg, Cheats.InfiniteAmmo); Print($"Бесконечные боеприпасы: {OnOff(Cheats.InfiniteAmmo)}"); break;
            case "heat": case "noheat": Cheats.NoHeat = Switch(arg, Cheats.NoHeat); Print($"Без нагрева: {OnOff(Cheats.NoHeat)}"); break;
            case "cheats": Print($"god {OnOff(Cheats.God)}, ammo {OnOff(Cheats.InfiniteAmmo)}, noheat {OnOff(Cheats.NoHeat)}, freecam {OnOff(FreeCamera.Current != null)}, timestop {OnOff(Cheats.TimeStopped)}, timescale {Engine.TimeScale:0.##}"); break;
            case "freecam": FreeCam(Switch(arg, FreeCamera.Current != null)); break;
            case "timestop": case "freeze": TimeStop(Switch(arg, Cheats.TimeStopped)); break;
            case "timescale": TimeScale(arg); break;
            case "win": Win(); break;
            case "skip": Skip(arg); break;
            case "unlock": Unlock(); break;
            default: Print($"[color=#ff6a5a]Нет такой команды: {command}. help — список команд.[/color]"); break;
        }
    }

    void Help()
    {
        Print("god [on|off] — бессмертие своей машины");
        Print("ammo [on|off] — бесконечные боеприпасы");
        Print("noheat [on|off] — мех не нагревается");
        Print("cheats — что сейчас включено");
        Print("freecam [on|off] — свободная камера: WASD, Q/E вниз/вверх, Shift быстрее, Alt медленнее, колесо — скорость");
        Print("timestop [on|off] — остановить время (свободная камера летает и при остановленном)");
        Print("timescale X — скорость времени (0.05–4; 1 — обычная)");
        Print("win — выполнить текущую миссию кампании (в бою)");
        Print("skip [N] — засчитать следующие N миссий кампании (по умолчанию одну) с их наградами; в бою — то же, что win");
        Print("unlock — все мехи, техника и оружие в ангар кампании");
        Print("clear — очистить консоль");
    }

    static bool Switch(string arg, bool now) => arg switch
    {
        "on" or "1" or "вкл" => true,
        "off" or "0" or "выкл" => false,
        _ => !now,
    };

    static string OnOff(bool on) => Loc.English ? (on ? "on" : "off") : on ? "вкл" : "выкл";

    const string FreeCameraScene = "res://scenes/fx/free_camera.tscn";

    void FreeCam(bool on)
    {
        if (Game.Instance == null)
        {
            Print("Только в бою.");
            return;
        }
        if (on && FreeCamera.Current == null)
        {
            // From where the player's eye is now.
            var from = GetViewport().GetCamera3D();
            var cam = GD.Load<PackedScene>(FreeCameraScene).Instantiate<FreeCamera>();
            Game.Instance.AddChild(cam);
            if (from != null) cam.GlobalTransform = from.GlobalTransform;
            // (after the console closes it gives the mouse back as it found it: captured, for the camera)
            _mouseBefore = Input.MouseModeEnum.Captured;
        }
        else if (!on && FreeCamera.Current is { } free)
        {
            free.QueueFree();
            Game.Cockpit?.Camera?.MakeCurrent();
        }
        Print($"Свободная камера: {OnOff(on)}");
    }

    void TimeStop(bool on)
    {
        if (Game.Instance == null)
        {
            Print("Только в бою.");
            return;
        }
        Cheats.TimeStopped = on;
        GetTree().Paused = on || Game.Instance.Paused;
        Print(Loc.English ? $"Time: {(on ? "stopped" : "running")}" : $"Время: {(on ? "остановлено" : "идёт")}");
    }

    void TimeScale(string arg)
    {
        if (!float.TryParse(arg.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float k))
        {
            Print($"Скорость времени: {Engine.TimeScale:0.##}. timescale X — задать (0.05–4).");
            return;
        }
        Engine.TimeScale = Mathf.Clamp(k, 0.05f, 4f);
        Print($"Скорость времени: {Engine.TimeScale:0.##}");
    }

    void Win()
    {
        if (Mission.Current is not { State: Mission.Outcome.Playing } mission)
        {
            Print("Сейчас не идёт миссия кампании.");
            return;
        }
        mission.Win();
        Print("Миссия засчитана.");
    }

    void Skip(string arg)
    {
        if (Mission.Current is { State: Mission.Outcome.Playing })
        {
            Win();
            return;
        }
        CampaignState.EnsureLoaded();
        if (CampaignState.Campaign == null)
        {
            Print("Кампания не найдена.");
            return;
        }
        int count = int.TryParse(arg, out int n) && n > 0 ? n : 1;
        CampaignState.MarkStarted();
        for (int i = 0; i < count && CampaignState.Next is { } next; i++)
        {
            CampaignState.Complete(next);
            Print($"Засчитана миссия: {next.Title}");
        }
        if (CampaignState.Finished) Print("Кампания пройдена.");
        Refresh();
    }

    void Unlock()
    {
        CampaignState.EnsureLoaded();
        if (CampaignState.Campaign == null)
        {
            Print("Кампания не найдена.");
            return;
        }
        foreach (var e in Catalog.PlayerUnits) CampaignState.Units.Add(e.Id);
        foreach (var e in Catalog.Weapons) CampaignState.Weapons.Add(e.Id);
        CampaignState.Save();
        Print("В ангар кампании выданы все машины и всё оружие.");
        Refresh();
    }

    /// <summary>The main menu shows what the console has changed.</summary>
    void Refresh() => (GetTree().CurrentScene as MainMenu)?.RefreshCampaign();
}
