using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Shown between the menu and a battle (scenes/ui/loading_screen.tscn). The battle's scenes — the map
/// with its sky and textures, every machine that may take the field — are read in on other threads
/// while the bar fills; the shapes hit marks are laid on are worked out for every machine; then the
/// battle is built under the screen, its effects are warmed up (see <see cref="Warmup"/>), and only
/// then does the screen fade. All that used to happen in one long freeze, and some of it in the battle
/// itself, as a stall at the first explosion or the first hit of a kind.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    const string ScenePath = "res://scenes/ui/loading_screen.tscn";

    /// <summary>Frames the screen stays up once the battle is built (the effects are warmed up meanwhile).</summary>
    [Export] public int SettleFrames = 10;
    /// <summary>Seconds it takes to fade out.</summary>
    [Export] public float FadeTime = 0.4f;

    string _game = "res://scenes/game.tscn";
    readonly List<string> _paths = new();
    readonly List<Resource> _loaded = new();
    readonly Godot.Collections.Array _progress = new();
    Control _panel;
    ProgressBar _bar;
    Label _title;
    int _stage, _wait, _prepared;
    List<string> _units;
    float _fade;

    /// <summary>Leaves whatever is on screen now (the menu, a battle) for a new battle.</summary>
    public static void Start(SceneTree tree, string gameScene = "res://scenes/game.tscn")
    {
        tree.Paused = false;
        var screen = GD.Load<PackedScene>(ScenePath).Instantiate<LoadingScreen>();
        screen._game = gameScene;
        tree.Root.AddChild(screen);
        // As ChangeSceneToFile would: the old scene goes, the new one takes its place later.
        tree.CurrentScene?.QueueFree();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _panel = GetNode<Control>("Panel");
        _bar = GetNode<ProgressBar>("%Bar");
        _title = GetNode<Label>("%Title");
        GameConfig.EnsureLoaded();
        // A campaign mission: its title, and its scene (which brings its map along).
        var mission = CampaignRun.Mission;
        _title.Text = Loc.T(mission != null ? mission.Title : Catalog.Find(Catalog.Maps, GameConfig.MapId).Name);

        _paths.Add(_game);
        _paths.Add(mission != null ? mission.Scene : Catalog.Find(Catalog.Maps, GameConfig.MapId).Path);
        _units = new List<string>();
        foreach (var e in Catalog.Mechs) _units.Add(e.Path);
        foreach (var e in Catalog.Vehicles) _units.Add(e.Path);
        _paths.AddRange(_units);
        foreach (string path in _paths) ResourceLoader.LoadThreadedRequest(path, "", true);
    }

    public override void _Process(double delta)
    {
        switch (_stage)
        {
            case 0:
            {
                // Reading: the bar runs to 70% as the files come in.
                float sum = 0f;
                bool done = true;
                foreach (string path in _paths)
                {
                    var status = ResourceLoader.LoadThreadedGetStatus(path, _progress);
                    if (status == ResourceLoader.ThreadLoadStatus.InProgress)
                    {
                        done = false;
                        sum += _progress.Count > 0 ? (float)_progress[0] : 0f;
                    }
                    else sum += 1f;
                }
                _bar.Value = 70.0 * sum / _paths.Count;
                if (!done) break;
                // Taken off the loader's hands (and held until the battle is up, so nothing is read twice).
                foreach (string path in _paths) _loaded.Add(ResourceLoader.LoadThreadedGet(path));
                _stage = 1;
                break;
            }
            case 1:
                // One machine a frame: what its parts look like to a shot, for the marks hits leave.
                if (_prepared < _units.Count)
                {
                    if (_loaded[_paths.IndexOf(_units[_prepared])] is PackedScene unit)
                    {
                        var node = unit.Instantiate();
                        Impacts.Prepare(node);
                        node.Free();
                    }
                    _prepared++;
                    _bar.Value = 70.0 + 20.0 * _prepared / _units.Count;
                    break;
                }
                // (A frame for the bar to show before the long one that builds the battle.)
                _stage = 2;
                break;
            case 2:
            {
                var game = ((PackedScene)_loaded[0]).Instantiate();
                GetTree().Root.AddChild(game);
                GetTree().CurrentScene = game;
                // Stay on top of it.
                GetTree().Root.MoveChild(this, -1);
                _bar.Value = 100.0;
                _wait = SettleFrames;
                _stage = 3;
                break;
            }
            case 3:
                if (--_wait > 0) break;
                _fade = FadeTime;
                _stage = 4;
                break;
            default:
                _fade -= (float)delta;
                _panel.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_fade / Mathf.Max(FadeTime, 0.01f), 0f, 1f));
                if (_fade <= 0f)
                {
                    _loaded.Clear();
                    QueueFree();
                }
                break;
        }
    }
}
