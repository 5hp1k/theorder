using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Root of game.tscn. Builds the battle chosen in the menus: loads the map, puts the player's unit
/// (a mech with paint, emblem and loadout, a vehicle, or a soldier next to a suit of power armour) on
/// the map's PlayerSpawn marker and the enemy mechs and vehicles around the map, each with its pilot.
/// Also owns pausing and the battle clock.
/// </summary>
public partial class Game : Node3D
{
    public static Game Instance { get; private set; }
    public static int Kills;
    /// <summary>Whatever the player is controlling right now (a mech, a vehicle, a soldier, a suit).</summary>
    public static ITarget PlayerUnit;
    /// <summary>The seat the HUD shows.</summary>
    public static ICockpit Cockpit;

    [Export] public PackedScene PlayerPilotScene;
    [Export] public PackedScene AiPilotScene;
    [Export] public Material EnemyCamo;
    /// <summary>The enemy's second colour, and which parts wear it (see <see cref="PaintJob.SchemeNames"/>).</summary>
    [Export] public Color EnemyStripe = new(0.5f, 0.1f, 0.06f);
    [Export] public int EnemyScheme = 1;
    [Export] public float PlayerRespawnTime = 7f;
    [Export] public float EnemyRespawnTime = 14f;
    [Export] public PackedScene SoldierScene;
    [Export] public PackedScene SuitScene;
    [Export(PropertyHint.File, "*.tscn")] public string MenuScene = "res://scenes/ui/main_menu.tscn";

    public bool Paused => GetTree().Paused;
    /// <summary>True once every enemy is down in a battle without respawns.</summary>
    public bool Victory { get; private set; }
    public bool TimeLimited { get; private set; }
    public float TimeLeft { get; private set; }
    public bool TimeUp { get; private set; }

    Control _pauseMenu;
    Trooper _soldier, _suit;
    Node3D _units, _map;
    readonly List<Node3D> _spawns = new();
    int _nextSpawn;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        // Nothing of this battle may outlive it in the static lists.
        Impacts.Reset();
        Units.All.Clear();
    }

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        Kills = 0;
        PlayerUnit = null;
        Cockpit = null;
        Units.All.Clear();
        Impacts.Reset();
        _pauseMenu = GetNode<Control>("Menu/PauseMenu");
        _pauseMenu.Visible = false;
        TimeLimited = GameConfig.TimeLimitMinutes > 0;
        TimeLeft = GameConfig.TimeLimitMinutes * 60f;

        _map = GD.Load<PackedScene>(Catalog.Find(Catalog.Maps, GameConfig.MapId).Path).Instantiate<Node3D>();
        // The weather the player asked for, in place of the map's own.
        if (GameConfig.WeatherId > 0 && GameConfig.WeatherId < Weather.Scenes.Length)
        {
            if (_map.GetNodeOrNull("Weather") is { } own)
            {
                _map.RemoveChild(own);
                own.QueueFree();
            }
            var weather = GD.Load<PackedScene>(Weather.Scenes[GameConfig.WeatherId]).Instantiate();
            weather.Name = "Weather";
            _map.AddChild(weather);
        }
        GetNode("Map").AddChild(_map);
        _units = GetNode<Node3D>("Units");

        // Enemy spawn markers, in random order.
        foreach (Node n in _map.GetNode("EnemySpawns").GetChildren())
            if (n is Node3D marker) _spawns.Add(marker);
        for (int i = _spawns.Count - 1; i > 0; i--)
        {
            int j = (int)(GD.Randi() % (uint)(i + 1));
            (_spawns[i], _spawns[j]) = (_spawns[j], _spawns[i]);
        }

        SpawnPlayer();
        SpawnEnemies();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ── Spawning ────────────────────────────────────────────────

    void SpawnPlayer()
    {
        Transform3D spawn = _map.GetNode<Node3D>("PlayerSpawn").Transform;
        var entry = Catalog.Find(Catalog.PlayerUnits, GameConfig.MechId);
        PaintJob paint = GameConfig.PlayerPaint();

        if (entry.Kind == Catalog.Kind.Infantry)
        {
            _soldier = SoldierScene.Instantiate<Trooper>();
            _soldier.Transform = spawn;
            _units.AddChild(_soldier);
            _suit = SuitScene.Instantiate<Trooper>();
            _suit.Transform = spawn.Translated(new Vector3(5f, 0f, -6f));
            _units.AddChild(_suit);
            _soldier.TakeControl();
            return;
        }

        var node = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
        if (node is Mech mech)
        {
            mech.Team = Team.Player;
            mech.DisplayName = entry.Name;
            mech.RespawnTime = PlayerRespawnTime;
            mech.ApplyPaint(paint);
            mech.ApplyInsignia(GameConfig.Insignia);
        }
        else if (node is Vehicle vehicle)
        {
            vehicle.Team = Team.Player;
            vehicle.DisplayName = entry.Name;
            vehicle.RespawnTime = PlayerRespawnTime;
            vehicle.ApplyPaint(paint);
            vehicle.ApplyInsignia(GameConfig.Insignia);
            // A ship starts at sea; where the map has none, in the nearest water there is.
            if (entry.Kind == Catalog.Kind.Water)
                spawn.Origin = MapTerrain.Instance?.RandomSeaSpot(24f) ?? WaterSpotNear(spawn.Origin) ?? spawn.Origin;
        }
        // What the player fitted in the hangar.
        GameConfig.Loadouts.TryGetValue(entry.Id, out string[] loadout);
        Hardpoint.Apply(node, loadout);
        node.AddChild(PlayerPilotScene.Instantiate());
        node.Transform = spawn;
        _units.AddChild(node);
    }

    void SpawnEnemies()
    {
        int mechs = GameConfig.EnemyCount;
        for (int i = 0; i < mechs; i++)
        {
            var entry = GameConfig.EnemyMechId == "random"
                ? Catalog.Mechs[GD.Randi() % (uint)Catalog.Mechs.Length]
                : Catalog.Find(Catalog.Mechs, GameConfig.EnemyMechId);
            var mech = GD.Load<PackedScene>(entry.Path).Instantiate<Mech>();
            mech.Team = Team.Enemy;
            mech.DisplayName = $"{entry.Name}-{i + 1}";
            mech.Respawns = GameConfig.EnemyRespawn;
            mech.RespawnTime = EnemyRespawnTime;
            mech.DamageDealtScale = GameConfig.EnemyDamageScale;
            mech.ApplyPaint(new PaintJob { Camo = EnemyCamo, Second = EnemyStripe, Scheme = EnemyScheme });
            mech.ApplyInsignia(null);
            Place(mech, NextSpawn());
        }

        // Vehicles: whatever the map can take. Ships and amphibians only where there's water to start in.
        var terrain = MapTerrain.Instance;
        bool hasWater = terrain != null && terrain.HasWater;
        for (int i = 0; i < GameConfig.EnemyVehicleCount; i++)
        {
            Catalog.Entry entry = null;
            Vector3 position = default;
            for (int tries = 0; tries < 8 && entry == null; tries++)
            {
                var pick = Catalog.Vehicles[GD.Randi() % (uint)Catalog.Vehicles.Length];
                if (pick.Kind == Catalog.Kind.Water)
                {
                    // Ships belong at sea: none on maps without one.
                    Vector3? water = hasWater ? terrain.RandomSeaSpot(24f) : null;
                    if (water == null) continue;
                    position = water.Value;
                }
                else position = NextSpawn();
                entry = pick;
            }
            if (entry == null) continue;

            var vehicle = GD.Load<PackedScene>(entry.Path).Instantiate<Vehicle>();
            vehicle.Team = Team.Enemy;
            vehicle.DisplayName = $"{entry.Name}-{i + 1}";
            vehicle.Respawns = GameConfig.EnemyRespawn;
            vehicle.RespawnTime = EnemyRespawnTime;
            vehicle.DamageDealtScale = GameConfig.EnemyDamageScale;
            vehicle.ApplyPaint(new PaintJob { Camo = EnemyCamo, Second = EnemyStripe, Scheme = EnemyScheme });
            Place(vehicle, position);
        }
    }

    /// <summary>The next unused spawn marker; when they run out, somewhere on a ring around the middle.</summary>
    Vector3 NextSpawn()
    {
        if (_nextSpawn < _spawns.Count) return _spawns[_nextSpawn++].Position;
        float angle = GD.Randf() * Mathf.Tau;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (float)GD.RandRange(220.0, 340.0);
    }

    void Place(Node3D unit, Vector3 position)
    {
        unit.AddChild(AiPilotScene.Instantiate());
        // Facing the middle of the map.
        unit.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(position.X, position.Z)), position);
        _units.AddChild(unit);
    }

    static Vector3? WaterSpotNear(Vector3 origin)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null || !terrain.HasWater) return null;
        // Widening rings around the start until one reaches open water with room for the hull;
        // failing that, any water deep enough (a river: cramped, but afloat).
        float edge = terrain.PlayHalf - 33f;
        foreach (float clear in new[] { 24f, 0f })
        for (float r = 20f; r < terrain.Size; r += 15f)
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.Tau / 16f;
            Vector3 p = origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            if (Mathf.Abs(p.X) < edge && Mathf.Abs(p.Z) < edge && terrain.IsOpenWater(p.X, p.Z, 2.2f, clear))
                return p;
        }
        return null;
    }

    // ── Battle state ────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (Paused) return;
        if (TimeLimited && !TimeUp)
        {
            TimeLeft -= (float)delta;
            if (TimeLeft <= 0f)
            {
                // Out of time: stop the battle and offer the way out.
                TimeLeft = 0f;
                TimeUp = true;
                SetPaused(true);
                return;
            }
        }

        if (Victory || GameConfig.EnemyRespawn) return;
        bool any = false, alive = false;
        foreach (var unit in Units.All)
        {
            if (unit.Team != Team.Enemy) continue;
            any = true;
            alive |= !unit.IsDestroyed;
        }
        Victory = any && !alive;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("release_mouse"))
        {
            // Once time is up there's no going back in.
            if (!TimeUp) SetPaused(!Paused);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>The soldier or the suit was destroyed: after a pause both are back at the start.</summary>
    public void OnTrooperDied()
    {
        GetTree().CreateTimer(PlayerRespawnTime, false).Timeout += () =>
        {
            _suit.ResetToSpawn();
            _soldier.ResetToSpawn();
            _soldier.TakeControl();
        };
    }

    public void SetPaused(bool paused)
    {
        GetTree().Paused = paused;
        _pauseMenu.Visible = paused;
        Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        // The click on "resume" shouldn't also fire the guns.
        if (!paused && Cockpit is PlayerPilot pilot) pilot.SuppressFireUntilRelease();
    }

    public void QuitToMenu()
    {
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ChangeSceneToFile(MenuScene);
    }
}
