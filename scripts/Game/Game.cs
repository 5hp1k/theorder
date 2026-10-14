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
    /// <summary>The player is typing a chat message: the keys are not commands.</summary>
    public static bool Typing;

    [Export] public PackedScene PlayerPilotScene;
    [Export] public PackedScene AiPilotScene;
    [Export] public Material EnemyCamo;
    /// <summary>The enemy's second colour, and which parts wear it (see <see cref="PaintJob.SchemeNames"/>).</summary>
    [Export] public Color EnemyStripe = new(0.5f, 0.1f, 0.06f);
    [Export] public int EnemyScheme = 1;
    [Export] public float PlayerRespawnTime = 7f;
    [Export] public float EnemyRespawnTime = 14f;
    /// <summary>Distance between neighbours in the player's squad formation, metres.</summary>
    [Export] public float SquadSpacing = 16f;
    [Export] public PackedScene SoldierScene;
    [Export] public PackedScene SuitScene;
    [Export(PropertyHint.File, "*.tscn")] public string MenuScene = "res://scenes/ui/main_menu.tscn";

    public bool Paused => _pauseMenu != null && _pauseMenu.Visible;
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
        Net.Reset();
        Score.Reset();
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

        if (Net.Online && !Net.IsHost)
        {
            // A client of a network game: the host says what is on the field.
            TimeLeft = Net.ClientTimeLeft;
            NetLink.I.ClientSceneReady();
        }
        else
        {
            SpawnPlayer();
            // One's own lance only when playing alone or together against the computer.
            if (!Net.Online || Net.Mode == Net.GameMode.Pve) SpawnSquad();
            SpawnEnemies();
            if (Net.Online) NetLink.I.HostSceneReady(this);
        }
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
        if (node is Mech named && Net.Online) named.DisplayName = NetLink.I.HostName;
        node.Transform = spawn;
        _units.AddChild(node);
        if (node is Mech own) own.Respawns = !Net.Online || Net.Respawns;
        else if (node is Vehicle ownVehicle) ownVehicle.Respawns = !Net.Online || Net.Respawns;
        NetLink.I.Track(node, 1, loadout, GameConfig.PlayerPaint().Pack(), GameConfig.InsigniaBytes());
    }

    // ── Network game ────────────────────────────────────────────

    /// <summary>The pilot of the given kind sitting in a machine, if that is who drives it.</summary>
    public static T PilotOf<T>(Node unit) where T : MechPilot
    {
        foreach (Node child in unit.GetChildren())
            if (child is T pilot) return pilot;
        return null;
    }

    int _joined;

    /// <summary>Host: puts a machine on the field for a player who has joined; it is driven from that player's computer.</summary>
    public Node3D SpawnForPeer(string unitId, string name, string[] loadout, int peer, float[] paint, byte[] emblem)
    {
        var entry = Catalog.Find(Catalog.PlayerUnits, unitId);
        var node = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
        _joined++;
        // Teams: all together against the computer; two sides, filled in turn; or (free-for-all) it doesn't matter.
        Team team = Net.Mode == Net.GameMode.Tdm && _joined % 2 == 1 ? Team.Enemy : Team.Player;
        Outfit(node, team, name, loadout, paint, emblem);
        node.AddChild(new RemotePilot());
        Vector3 at = Net.Mode == Net.GameMode.Pve
            ? _map.GetNode<Node3D>("PlayerSpawn").Position + new Vector3(18f * _joined, 0f, 14f)
            : NextSpawn();
        if (entry.Kind == Catalog.Kind.Water) at = MapTerrain.Instance?.RandomSeaSpot(24f) ?? WaterSpotNear(at) ?? at;
        node.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(at.X, at.Z)), at);
        _units.AddChild(node);
        NetLink.I.Track(node, peer, loadout, paint, emblem);
        return node;
    }

    /// <summary>Client: shows a machine the host has told about; one's own gets the player's seat.</summary>
    public Node3D SpawnRemote(string unitId, Team team, string name, string[] loadout, Transform3D at, bool mine, float[] paint, byte[] emblem)
    {
        var entry = Catalog.Find(Catalog.PlayerUnits, unitId);
        var node = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
        Outfit(node, team, name, loadout, paint, emblem);
        node.AddChild(mine ? PlayerPilotScene.Instantiate() : new RemotePilot());
        node.Transform = at;
        _units.AddChild(node);
        return node;
    }

    /// <summary>Side, name, weapons and colours of a network player's machine. It comes back only when the host says so.</summary>
    void Outfit(Node3D node, Team team, string name, string[] loadout, float[] colours, byte[] emblem)
    {
        Texture2D badge = GameConfig.InsigniaFrom(emblem);
        // Each player's machine in that player's own colours; the computer's in its side's.
        var paint = PaintJob.Unpack(colours) ?? (team == Team.Player ? GameConfig.PlayerPaint() : new PaintJob { Camo = EnemyCamo, Second = EnemyStripe, Scheme = EnemyScheme });
        if (node is Mech mech)
        {
            mech.Team = team;
            mech.DisplayName = name;
            mech.Respawns = Net.IsHost && Net.Respawns;
            mech.RespawnTime = PlayerRespawnTime;
            mech.ApplyPaint(paint);
            mech.ApplyInsignia(badge);
        }
        else if (node is Vehicle vehicle)
        {
            vehicle.Team = team;
            vehicle.DisplayName = name;
            vehicle.Respawns = Net.IsHost && Net.Respawns;
            vehicle.RespawnTime = PlayerRespawnTime;
            vehicle.ApplyPaint(paint);
            vehicle.ApplyInsignia(badge);
        }
        Hardpoint.Apply(node, loadout);
    }

    /// <summary>The player's squad, in the order of their numbers (key 1 is the first).</summary>
    public readonly List<AiPilot> Squad = new();

    /// <summary>
    /// The player's own lance: mechs on the player's side, each with a computer pilot that keeps
    /// formation on the player and takes orders (see <see cref="SquadCommand"/>).
    /// </summary>
    void SpawnSquad()
    {
        Squad.Clear();
        if (PlayerUnit is not IDrivable leader) return;
        Transform3D spawn = _map.GetNode<Node3D>("PlayerSpawn").Transform;
        PaintJob paint = GameConfig.PlayerPaint();
        for (int i = 0; i < GameConfig.SquadSize; i++)
        {
            var entry = GameConfig.SquadMechId switch
            {
                "random" => Catalog.Mechs[GD.Randi() % (uint)Catalog.Mechs.Length],
                "same" => Catalog.Find(Catalog.Mechs, GameConfig.MechId),
                _ => Catalog.Find(Catalog.Mechs, GameConfig.SquadMechId),
            };
            var mech = GD.Load<PackedScene>(entry.Path).Instantiate<Mech>();
            mech.Team = Team.Player;
            mech.DisplayName = $"Звено-{i + 1} ({entry.Name})";
            mech.Respawns = true;
            mech.RespawnTime = EnemyRespawnTime;
            mech.ApplyPaint(paint);
            mech.ApplyInsignia(GameConfig.Insignia);
            // A wedge behind the leader: odd numbers to the left, even to the right, each pair further back.
            int row = i / 2 + 1;
            var slot = new Vector3((i % 2 == 0 ? -1f : 1f) * SquadSpacing * row, 0f, SquadSpacing * 0.8f * row);
            var pilot = AiPilotScene.Instantiate<AiPilot>();
            pilot.Leader = leader;
            pilot.FormationSlot = slot;
            mech.AddChild(pilot);
            mech.Transform = new Transform3D(spawn.Basis, spawn.Origin + spawn.Basis * slot);
            _units.AddChild(mech);
            NetLink.I.Track(mech, 0, null, null);
            Squad.Add(pilot);
        }
    }

    void SpawnEnemies()
    {
        // Only the machines ticked on the battle page take the field.
        var mechPool = GameConfig.EnemyPool(Catalog.Mechs);
        var vehiclePool = GameConfig.EnemyPool(Catalog.Vehicles);
        int mechs = mechPool.Length > 0 ? GameConfig.EnemyCount : 0;
        for (int i = 0; i < mechs; i++)
        {
            var entry = mechPool[GD.Randi() % (uint)mechPool.Length];
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
        for (int i = 0; i < GameConfig.EnemyVehicleCount && vehiclePool.Length > 0; i++)
        {
            Catalog.Entry entry = null;
            Vector3 position = default;
            for (int tries = 0; tries < 8 && entry == null; tries++)
            {
                var pick = vehiclePool[GD.Randi() % (uint)vehiclePool.Length];
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
        NetLink.I.Track(unit, 0, null, null);
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
        if (Net.Online && !Net.IsHost)
        {
            // The host keeps the clock.
            TimeLeft = Net.ClientTimeLeft;
            if (Net.ClientTimeUp && !TimeUp)
            {
                TimeUp = true;
                SetPaused(true);
            }
            return;
        }
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
        // In a network game only the host stops the battle, and stops it for everyone; a client's menu
        // opens over a battle that goes on.
        if (!Net.Online || Net.IsHost)
        {
            GetTree().Paused = paused;
            NetLink.I?.HostPaused(paused);
        }
        _pauseMenu.Visible = paused;
        Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        // The click on "resume" shouldn't also fire the guns.
        if (!paused && Cockpit is PlayerPilot pilot) pilot.SuppressFireUntilRelease();
    }

    public void QuitToMenu()
    {
        NetLink.I?.Stop();
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ChangeSceneToFile(MenuScene);
    }
}
