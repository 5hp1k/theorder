using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Root of game.tscn. Builds the battle chosen in the menus: loads the map, puts the player's unit
/// (a mech with paint, emblem and loadout, a vehicle, or a soldier next to a suit of power armour) on
/// the map's PlayerSpawn marker and the enemy mechs and vehicles around the map, each with its pilot.
/// Also owns pausing and the battle clock.
/// A campaign mission (<see cref="CampaignRun.Mission"/>) is played instead from its own scene
/// (<see cref="Mission"/>): its map, its machines, its named lance; it ends when the mission says so.
/// </summary>
public partial class Game : Node3D
{
    public static Game Instance { get; private set; }
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
    /// <summary>A campaign mission is over (won or lost) and its debriefing is up.</summary>
    public bool MissionOver { get; private set; }
    public bool MissionWon { get; private set; }
    /// <summary>The mission was won for the first time: its rewards went into the hangar.</summary>
    public bool Rewarded { get; private set; }
    public MissionInfo MissionInfo { get; private set; }

    Control _pauseMenu;
    Mission _mission;
    Trooper _soldier, _suit;
    Node3D _units, _map;
    readonly List<Node3D> _spawns = new();
    int _nextSpawn;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        // Time runs again in the next battle; the console's speed-up or slow-down goes with this one.
        Cheats.TimeStopped = false;
        Engine.TimeScale = 1.0;
        // Nothing of this battle may outlive it in the static lists.
        Impacts.Reset();
        Units.All.Clear();
    }

    public override void _Ready()
    {
        GameConfig.EnsureLoaded();
        ControlModes.Apply();
        PlayerUnit = null;
        Cockpit = null;
        Units.All.Clear();
        Impacts.Reset();
        Net.Reset();
        Score.Reset();
        _pauseMenu = GetNode<Control>("Menu/PauseMenu");
        _pauseMenu.Visible = false;
        MissionInfo = CampaignRun.Mission;
        TimeLimited = (MissionInfo?.TimeLimit ?? GameConfig.TimeLimitMinutes) > 0;
        TimeLeft = (MissionInfo?.TimeLimit ?? GameConfig.TimeLimitMinutes) * 60f;

        if (MissionInfo != null)
        {
            // A campaign mission: its scene brings the map with it. (One made in the editor is built from its data.)
            _mission = CampaignRun.Custom != null ? MissionBuilder.Build(CampaignRun.Custom) : GD.Load<PackedScene>(MissionInfo.Scene).Instantiate<Mission>();
            _map = _mission.Map;
            SetWeather(_map, MissionInfo.Weather);
            GetNode("Map").AddChild(_mission);
        }
        else
        {
            _map = GD.Load<PackedScene>(Catalog.Find(Catalog.Maps, GameConfig.MapId).Path).Instantiate<Node3D>();
            SetWeather(_map, GameConfig.WeatherId);
            GetNode("Map").AddChild(_map);
        }
        // The edge of the battlefield, seen only from the console's free camera.
        _map.AddChild(new BoundsWall { Name = "BoundsWall", OnlyFreeCamera = true });
        AddChild(new VisionFx { Name = "VisionFx" });
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
            // A mission together: the host plays it; here it is only shown.
            _mission?.Mirror();
            NetLink.I.ClientSceneReady();
        }
        else
        {
            SpawnPlayer();
            // One's own lance only when playing alone or together against the computer.
            if (MissionInfo != null) SpawnLance();
            else if (!Net.Online || Net.Mode == Net.GameMode.Pve) SpawnSquad();
            if (_mission != null) _mission.Begin();
            else SpawnEnemies();
            if (Net.Online) NetLink.I.HostSceneReady(this);
        }
        // Effects and the shapes hit marks are laid on are prepared now, not at the first shot.
        Warmup.Run(_units);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Weather in place of the map's own (0 — the map's own).</summary>
    public static void SetWeather(Node3D map, int id)
    {
        if (id <= 0 || id >= Weather.Scenes.Length) return;
        if (map.GetNodeOrNull("Weather") is { } own)
        {
            map.RemoveChild(own);
            own.QueueFree();
        }
        var weather = GD.Load<PackedScene>(Weather.Scenes[id]).Instantiate();
        weather.Name = "Weather";
        map.AddChild(weather);
    }

    // ── Spawning ────────────────────────────────────────────────

    /// <summary>Where the player starts: a mission's own marker, or the map's.</summary>
    Transform3D PlayerStart()
        => OnGround(_mission?.GetNodeOrNull<Node3D>("PlayerSpawn")?.GlobalTransform ?? _map.GetNode<Node3D>("PlayerSpawn").Transform);

    /// <summary>Not under the ground (a mission made in the editor keeps only where on the map, not how high).</summary>
    static Transform3D OnGround(Transform3D at)
    {
        if (MapTerrain.Instance is { } terrain) at.Origin.Y = Mathf.Max(at.Origin.Y, terrain.HeightAt(at.Origin.X, at.Origin.Z));
        return at;
    }

    void SpawnPlayer()
    {
        Transform3D spawn = PlayerStart();
        // In a mission: the machine the story puts the player in, or the one chosen in the campaign's hangar.
        string unitId = MissionInfo is { PlayerUnit.Length: > 0 } ? MissionInfo.PlayerUnit : GameConfig.MechId;
        var entry = Catalog.Find(Catalog.PlayerUnits, unitId);
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
            GameConfig.WearInsignia(mech, entry.Id);
        }
        else if (node is Vehicle vehicle)
        {
            vehicle.Team = Team.Player;
            vehicle.DisplayName = entry.Name;
            vehicle.RespawnTime = PlayerRespawnTime;
            vehicle.ApplyPaint(paint);
            GameConfig.WearInsignia(vehicle, entry.Id);
            // A ship starts at sea; where the map has none, in the nearest water there is.
            if (entry.Kind == Catalog.Kind.Water)
                spawn.Origin = MapTerrain.Instance?.RandomSeaSpot(24f) ?? WaterSpotNear(spawn.Origin) ?? spawn.Origin;
        }
        // What the player fitted in the hangar (or what a mission made in the editor gives the machine it puts the player in).
        string[] loadout = CampaignRun.Custom?.Player is { UnitId.Length: > 0, Loadout.Length: > 0 } given && given.UnitId == entry.Id ? given.Loadout : GameConfig.LoadoutFor(entry.Id);
        Hardpoint.Apply(node, loadout);
        node.AddChild(PlayerPilotScene.Instantiate());
        if (node is Mech named && Net.Online) named.DisplayName = NetLink.I.HostName;
        if (MissionInfo != null && CampaignRun.Custom == null && CampaignState.Campaign?.Hero is { } hero)
        {
            if (node is Mech heroMech) heroMech.DisplayName = hero.Label;
            else if (node is Vehicle heroVehicle) heroVehicle.DisplayName = hero.Label;
        }
        node.Transform = spawn;
        _units.AddChild(node);
        // A mission is played with one life.
        bool respawns = MissionInfo == null && (!Net.Online || Net.Respawns);
        if (node is Mech own) own.Respawns = respawns;
        else if (node is Vehicle ownVehicle) ownVehicle.Respawns = respawns;
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
        Vector3 at;
        if (MissionInfo != null)
        {
            // A mission together: alongside the host at the mission's start, right and left in turn.
            var start = PlayerStart();
            float side = 16f * ((_joined + 1) / 2) * (_joined % 2 == 1 ? 1f : -1f);
            at = OnGround(new Transform3D(start.Basis, start * new Vector3(side, 0f, 10f))).Origin;
        }
        else at = Net.Mode == Net.GameMode.Pve
            ? _map.GetNode<Node3D>("PlayerSpawn").Position + new Vector3(18f * _joined, 0f, 14f)
            : NextSpawn();
        if (entry.Kind == Catalog.Kind.Water) at = MapTerrain.Instance?.RandomSeaSpot(24f) ?? WaterSpotNear(at) ?? at;
        node.Transform = MissionInfo != null ? new Transform3D(PlayerStart().Basis, at) : new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(at.X, at.Z)), at);
        // A mission is played with one life, together as alone.
        if (MissionInfo != null)
        {
            if (node is Mech once) once.Respawns = false;
            else if (node is Vehicle onceVehicle) onceVehicle.Respawns = false;
        }
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
            mech.Respawns = GameConfig.SquadRespawn;
            mech.RespawnTime = EnemyRespawnTime;
            mech.ApplyPaint(paint);
            GameConfig.WearInsignia(mech, entry.Id);
            // A wedge behind the leader: odd numbers to the left, even to the right, each pair further back.
            int row = i / 2 + 1;
            var slot = new Vector3((i % 2 == 0 ? -1f : 1f) * SquadSpacing * row, 0f, SquadSpacing * 0.8f * row);
            var pilot = AiPilotScene.Instantiate<AiPilot>();
            pilot.Leader = leader;
            pilot.FormationSlot = slot;
            mech.AddChild(pilot);
            mech.Transform = new Transform3D(spawn.Basis, ClearNear(spawn.Origin + spawn.Basis * slot, Vector3.Zero));
            _units.AddChild(mech);
            NetLink.I.Track(mech, 0, null, null);
            Squad.Add(pilot);
        }
    }

    /// <summary>A mission's lance: its named pilots in the machines the mission gives them, in formation on the player.</summary>
    void SpawnLance()
    {
        Squad.Clear();
        if (PlayerUnit is not IDrivable leader) return;
        Transform3D spawn = PlayerStart();
        PaintJob paint = GameConfig.PlayerPaint();
        int i = 0;
        foreach (var member in MissionInfo.Lance)
        {
            if (member == null) continue;
            var entry = Catalog.Find(Catalog.PlayerUnits, member.UnitId);
            var node = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
            string name = member.Pilot?.Label ?? $"Звено-{i + 1}";
            if (node is Mech mech)
            {
                mech.Team = Team.Player;
                mech.DisplayName = name;
                mech.Respawns = false;
                mech.ApplyPaint(paint);
                GameConfig.WearInsignia(mech, entry.Id);
            }
            else if (node is Vehicle vehicle)
            {
                vehicle.Team = Team.Player;
                vehicle.DisplayName = name;
                vehicle.Respawns = false;
                vehicle.ApplyPaint(paint);
                GameConfig.WearInsignia(vehicle, entry.Id);
            }
            if (!string.IsNullOrEmpty(member.Loadout)) Hardpoint.Apply(node, member.Loadout.Split('|'));
            int row = i / 2 + 1;
            var slot = new Vector3((i % 2 == 0 ? -1f : 1f) * SquadSpacing * row, 0f, SquadSpacing * 0.8f * row);
            var pilot = AiPilotScene.Instantiate<AiPilot>();
            member.Pilot?.ApplyTo(pilot);
            pilot.Leader = leader;
            pilot.FormationSlot = slot;
            node.AddChild(pilot);
            node.Transform = new Transform3D(spawn.Basis, ClearNear(spawn.Origin + spawn.Basis * slot, Vector3.Zero));
            _units.AddChild(node);
            NetLink.I.Track(node, 0, null, null);
            Squad.Add(pilot);
            i++;
        }
    }

    /// <summary>A machine of a mission takes the field where its marker stands.</summary>
    public Node3D SpawnStoryUnit(MissionUnit spec)
    {
        var entry = Catalog.Find(Catalog.PlayerUnits, spec.UnitId);
        var node = GD.Load<PackedScene>(entry.Path).Instantiate<Node3D>();
        string name = spec.DisplayName.Length > 0 ? spec.DisplayName : spec.Pilot?.Label ?? entry.Name;
        var paint = PaintJob.Unpack(spec.Paint) ?? (spec.Team == Team.Player ? GameConfig.PlayerPaint() : new PaintJob { Camo = EnemyCamo, Second = EnemyStripe, Scheme = EnemyScheme });
        if (node is Mech mech)
        {
            mech.Team = spec.Team;
            mech.DisplayName = name;
            mech.Respawns = false;
            mech.ApplyPaint(paint);
            mech.ApplyInsignia(spec.Team == Team.Player ? GameConfig.Insignia : null);
        }
        else if (node is Vehicle vehicle)
        {
            vehicle.Team = spec.Team;
            vehicle.DisplayName = name;
            vehicle.Respawns = false;
            vehicle.ApplyPaint(paint);
        }
        if (!string.IsNullOrEmpty(spec.Loadout)) Hardpoint.Apply(node, spec.Loadout.Split('|'));
        var pilot = AiPilotScene.Instantiate<AiPilot>();
        spec.Pilot?.ApplyTo(pilot);
        pilot.PatrolRadius = spec.PatrolRadius;
        node.AddChild(pilot);
        Transform3D at = OnGround(spec.GlobalTransform.Orthonormalized());
        if (entry.Kind == Catalog.Kind.Water) at.Origin = WaterSpotNear(at.Origin) ?? at.Origin;
        node.Transform = at;
        _units.AddChild(node);
        NetLink.I.Track(node, 0, string.IsNullOrEmpty(spec.Loadout) ? null : spec.Loadout.Split('|'), spec.Paint.Length > 0 ? spec.Paint : null);
        if (spec.HuntPlayer && PlayerUnit != null) pilot.Alert(PlayerUnit);
        return node;
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
    readonly List<Vector3> _placed = new();

    /// <summary>Nothing built stands there (a machine put inside a wall or a pylon would be thrown out of it, often down through the ground).</summary>
    bool Clear(Vector3 at)
    {
        if (MapTerrain.Instance is { } terrain && !terrain.InsideEnclosure(at, 8f)) return false;
        float ground = MapTerrain.Instance?.HeightAt(at.X, at.Z) ?? at.Y;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 9f },
            Transform = new Transform3D(Basis.Identity, new Vector3(at.X, ground + 7f, at.Z)),
            CollisionMask = 32,
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }

    /// <summary>The spot, or the nearest clear one on the way from it to <paramref name="toward"/> (a formation
    /// slot that falls in a wall or outside an arena's is drawn in).</summary>
    Vector3 ClearNear(Vector3 at, Vector3 toward)
    {
        Vector3 p = at;
        for (int i = 0; i < 12 && !Clear(p); i++)
            p = p.MoveToward(new Vector3(toward.X, p.Y, toward.Z), 6f) + new Vector3((float)GD.RandRange(-3.0, 3.0), 0f, (float)GD.RandRange(-3.0, 3.0));
        return p;
    }

    Vector3 NextSpawn()
    {
        if (_nextSpawn < _spawns.Count && Clear(_spawns[_nextSpawn].Position))
        {
            _placed.Add(_spawns[_nextSpawn].Position);
            return _spawns[_nextSpawn++].Position;
        }
        // More machines than markers: beside one of the markers, a little way in towards the middle — never
        // somewhere the map does not mean anyone to stand (under a colosseum's stands, in the sea).
        if (_spawns.Count > 0)
        {
            Vector3 mark = _spawns[_nextSpawn++ % _spawns.Count].Position;
            Vector3 best = mark;
            float bestRoom = -1f;
            // (a few tries: the one farthest from every machine already put down, so no two stand in each other)
            for (int i = 0; i < 12; i++)
            {
                float angle = GD.Randf() * Mathf.Tau;
                Vector3 p = mark.Lerp(new Vector3(0f, mark.Y, 0f), (float)GD.RandRange(0.06, 0.26)) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (float)GD.RandRange(10.0, 30.0);
                if (!Clear(p)) continue;
                float room = float.MaxValue;
                foreach (var u in Units.All)
                    if (u is Node3D n && IsInstanceValid(n)) room = Mathf.Min(room, new Vector2(n.GlobalPosition.X - p.X, n.GlobalPosition.Z - p.Z).Length());
                foreach (var q in _placed) room = Mathf.Min(room, new Vector2(q.X - p.X, q.Z - p.Z).Length());
                if (room > bestRoom)
                {
                    bestRoom = room;
                    best = p;
                }
                if (room > 18f) break;
            }
            _placed.Add(best);
            return best;
        }
        float a = GD.Randf() * Mathf.Tau;
        float reach = Mathf.Min(MapTerrain.Instance?.PlayHalf ?? 300f, 340f) * 0.8f;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (float)GD.RandRange(reach * 0.6, reach);
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
            if (TimeLeft <= 0f && _mission != null)
            {
                // A mission run out of time is lost.
                TimeLeft = 0f;
                TimeUp = true;
                _mission.Lose("Время вышло");
            }
            else if (TimeLeft <= 0f)
            {
                // Out of time: stop the battle and offer the way out.
                TimeLeft = 0f;
                TimeUp = true;
                SetPaused(true);
                return;
            }
        }

        if (Victory || GameConfig.EnemyRespawn || _mission != null) return;
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
            // Esc in a cutscene skips it.
            if (Cutscene.Playing != null && !Paused)
            {
                Cutscene.Playing.Skip();
                GetViewport().SetInputAsHandled();
                return;
            }
            // Once time is up, or the mission over, there's no going back in.
            if (!TimeUp && !MissionOver) SetPaused(!Paused);
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
            // (time stopped from the console stays stopped when the pause menu closes)
            GetTree().Paused = paused || Cheats.TimeStopped;
            NetLink.I?.HostPaused(paused);
        }
        _pauseMenu.Visible = paused;
        Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        // The click on "resume" shouldn't also fire the guns.
        if (!paused && Cockpit is PlayerPilot pilot) pilot.SuppressFireUntilRelease();
    }

    /// <summary>The mission is over: the first win puts its rewards in the hangar; the debriefing comes up.</summary>
    public void EndMission(bool won)
    {
        if (MissionOver) return;
        MissionOver = true;
        MissionWon = won;
        if (won && MissionInfo != null && CampaignRun.Custom == null && Net.Authority) Rewarded = CampaignState.Complete(MissionInfo);
        NetLink.I?.MissionOver(won, Mission.Current?.Reason ?? "");
        SetPaused(true);
    }

    /// <summary>Whether a machine is a player's (this computer's, or in a network game anyone's), not the computer's.</summary>
    public static bool IsHuman(ITarget unit)
    {
        if (unit == null) return false;
        if (unit == PlayerUnit) return true;
        return Net.Online && unit is Node node && node.HasMeta("net_id")
            && NetLink.I.UnitsById.TryGetValue((int)node.GetMeta("net_id"), out var info) && info.Owner != 0;
    }

    /// <summary>Every player's machine is destroyed (alone: this one's; in a network game: everyone's).</summary>
    public static bool AllPlayersDown()
    {
        if (PlayerUnit is not { IsDestroyed: true }) return false;
        if (!Net.Online) return true;
        foreach (var info in NetLink.I.UnitsById.Values)
            if (info.Owner != 0 && IsInstanceValid(info.Node) && info.Node is ITarget { IsDestroyed: false }) return false;
        return true;
    }

    public void QuitToMenu()
    {
        // From a mission, back to the campaign's page.
        CampaignRun.BackToCampaign = MissionInfo != null && CampaignRun.Custom == null && !Net.Online;
        CampaignRun.BackToMissions = CampaignRun.Custom != null && !CampaignRun.FromEditor && !Net.Online;
        CampaignRun.Mission = null;
        if (CampaignRun.Custom != null && CampaignRun.FromEditor)
        {
            // Tried out from the editor: back to it, with the mission as it was.
            CampaignRun.FromEditor = false;
            MissionEditor.Opening = CampaignRun.Custom;
            CampaignRun.Custom = null;
            NetLink.I?.Stop();
            GetTree().Paused = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetTree().ChangeSceneToFile(MissionEditor.ScenePath);
            return;
        }
        CampaignRun.Custom = null;
        NetLink.I?.Stop();
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ChangeSceneToFile(MenuScene);
    }
}
