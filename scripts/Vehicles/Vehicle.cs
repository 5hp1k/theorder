using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A vehicle: tank, armoured car, amphibious tank, ship, helicopter or bomber, depending on Kind.
/// Like a mech it is driven by a <see cref="MechPilot"/> child (the player or the AI) through throttle,
/// steering and a turret; unlike a mech it has one hull strength instead of sections.
/// Scene layout: optional "Hull" (visual; tilts with the ground or banks in flight);
/// optional "Turret" (turns) with "Gun" under it (elevates) and "CockpitCam" under that — without a
/// turret, "CockpitCam" sits on the hull and the weapons point where the nose does;
/// Weapon nodes anywhere; nodes named "Rotor…" spin; optional "Sounds/Engine" loop.
/// </summary>
public partial class Vehicle : CharacterBody3D, IDrivable, IDamageable
{
    public enum MoveKind { Ground, Amphibious, Ship, Helicopter, Plane }

    [Export] public string DisplayName { get; set; } = "Техника";
    [Export] public MoveKind Kind;
    [Export] public float MaxHp = 120f;
    /// <summary>Height of the point enemies aim at, above the origin.</summary>
    [Export] public float CenterHeight = 1.5f;
    /// <summary>Scales the chase camera's distance.</summary>
    [Export] public float ChaseScale { get; set; } = 0.6f;

    [ExportGroup("Movement")]
    [Export] public float MaxForwardSpeed { get; set; } = 16f;
    [Export] public float ReverseFraction { get; set; } = 0.4f;
    [Export] public float Acceleration = 6f;
    [Export] public float Deceleration = 10f;
    [Export] public float TurnRate = 1.0f;
    [Export] public float ThrottleRate { get; set; } = 1f;
    [Export] public float Gravity = 20f;
    /// <summary>How deep it sits when afloat (amphibians, ships); ships need this much water plus a little.</summary>
    [Export] public float Draft = 1.2f;
    /// <summary>How far ahead of the middle a ship checks the depth (about half its length).</summary>
    [Export] public float BowDistance = 12f;

    [ExportGroup("Flight")]
    /// <summary>Height above the ground the AI flies at, and where aircraft start.</summary>
    [Export] public float CruiseAltitude = 45f;
    [Export] public float ClimbRate = 9f;
    [Export] public float MinAltitude = 4f;
    [Export] public float MaxAltitude = 220f;
    /// <summary>Slowest a plane can fly, as a share of top speed.</summary>
    [Export] public float StallFraction = 0.45f;
    /// <summary>How far a plane rolls into a turn, radians.</summary>
    [Export] public float BankAngle = 0.7f;

    [ExportGroup("Turret")]
    /// <summary>Travel each way; π = all the way round; 0 = fixed guns.</summary>
    [Export] public float MaxTwist { get; set; } = Mathf.Pi;
    [Export] public float TwistRate = 1.6f;
    [Export] public float PitchRate = 1.2f;
    [Export] public float MinPitch { get; set; } = -0.2f;
    [Export] public float MaxPitch { get; set; } = 0.5f;

    [ExportGroup("Damage")]
    [Export] public Material CharredMaterial;
    [Export] public PackedScene ExplosionScene;
    [Export] public PackedScene WreckFireScene;

    [ExportGroup("Paint")]
    /// <summary>The material the repaintable plating uses in this scene; replaced by a camouflage.</summary>
    [Export] public Material ArmorMaterial;

    // ── Set by whoever spawns it, before it enters the tree ─────
    public Team Team { get; set; } = Team.Enemy;
    public bool Respawns = true;
    public float RespawnTime = 10f;
    public float DamageDealtScale = 1f;

    // ── State ───────────────────────────────────────────────────
    public MechPilot Pilot { get; private set; }
    public float Hp { get; private set; }
    public float Speed { get; private set; }
    public bool IsDestroyed => Hp <= 0f;
    public bool Offline => IsDestroyed;
    public float Integrity => Mathf.Clamp(Hp / MaxHp, 0f, 1f);
    public DamageModel Damage => null;
    public float RespawnLeft { get; private set; }
    public float TorsoYaw => _yaw;
    public float TorsoPitch => _pitch;
    public float HeadingDeg => Mathf.PosMod(Mathf.RadToDeg(-(Rotation.Y + _yaw)), 360f);
    public bool Blocked { get; private set; }
    public float Time { get; private set; }
    public WeaponSystem Weapons { get; private set; }
    public GodotObject AimCollider { get; private set; }
    public Vector3 AimPoint { get; private set; }
    public Node3D AimAnchor { get; private set; }
    public CharacterBody3D Body => this;
    public Node3D CockpitInterior => null;
    public Vector3 ViewPivot => (_turret ?? this).GlobalPosition;
    public float HullYaw => Rotation.Y;
    public Vector3 AimCenter => GlobalPosition + Vector3.Up * CenterHeight;
    public bool IsAircraft => Kind == MoveKind.Helicopter || Kind == MoveKind.Plane;
    /// <summary>Half the width and half the length of the hull on the ground.</summary>
    public Vector2 HalfExtents { get; private set; } = new(1.6f, 3.2f);

    // No reactor heat and no jump jets on vehicles.
    public bool HasHeat => false;
    public float Heat => 0f;
    public bool IsShutdown => false;
    public float RestartHeat => 0f;
    public float RestartProgress => 0f;
    public bool HasJumpJets => false;
    public float JumpFuel => 0f;
    public bool Thrusting => false;

    // ── Commands ────────────────────────────────────────────────
    public float Throttle { get; set; }
    public float TurnInput { get; set; }
    public float TargetYaw { get; set; }
    public float TargetPitch { get; set; }
    public bool Fire1 { get; set; }
    public bool Fire2 { get; set; }
    public bool Fire3 { get; set; }
    public bool Jump { get; set; }
    public bool Descend { get; set; }
    public ITarget LockTarget { get; set; }
    public float Shake { get; set; }

    Node3D _hull, _turret, _gun;
    readonly List<Node3D> _rotors = new();
    readonly List<MeshInstance3D> _meshes = new();
    readonly List<Material> _original = new();
    readonly Godot.Collections.Array<Rid> _exclude = new();
    AudioStreamPlayer3D _engine;
    Node3D _wreckFx;
    // Loose fittings that hits knock off one by one (wheels, tracks, masts…), and what has come off so far.
    readonly List<Node3D> _fittings = new();
    readonly List<Debris.Detached> _torn = new();
    int _fittingsLost;
    Transform3D _spawn;
    uint _layer, _mask;
    float _yaw, _pitch, _climb, _roll, _tilt;
    bool _falling;
    int _tick, _wake;

    public override void _EnterTree() => Units.All.Add(this);

    public override void _ExitTree()
    {
        Units.All.Remove(this);
        if (Game.PlayerUnit == this) Game.PlayerUnit = null;
    }

    public override void _Ready()
    {
        _hull = GetNodeOrNull<Node3D>("Hull");
        _turret = GetNodeOrNull<Node3D>("Turret");
        _gun = GetNodeOrNull<Node3D>("Turret/Gun");
        AimAnchor = GetNodeOrNull<Node3D>("Turret/Gun/CockpitCam") ?? GetNodeOrNull<Node3D>("Hull/CockpitCam") ?? GetNode<Node3D>("CockpitCam");
        _engine = GetNodeOrNull<AudioStreamPlayer3D>("Sounds/Engine");
        foreach (var node in Geo.Collect<Node3D>(this))
            if (node.Name.ToString().StartsWith("Rotor")) _rotors.Add(node);
        Geo.Collect(this, _meshes);
        foreach (var mesh in _meshes) _original.Add(mesh.MaterialOverride);
        // Everything bolted to the hull except the main body can be shot off.
        if (_hull != null)
            foreach (Node child in _hull.GetChildren())
                if (child is Node3D fitting and not Marker3D and not Weapon && child.GetIndex() > 0) _fittings.Add(fitting);

        // The hull itself is what shots hit. Ground vehicles and ships are also solid to mechs.
        uint hit = Team == Team.Player ? Phys.LayerHitPlayer : Phys.LayerHitEnemy;
        // Mech bodies only get in the way if mechs aren't allowed to walk over vehicles.
        uint mechs = GameConfig.MechsCrush ? 0 : Phys.LayerMech | Phys.LayerTargets;
        CollisionLayer = _layer = IsAircraft ? hit : hit | Phys.LayerVehicles;
        CollisionMask = _mask = IsAircraft ? Phys.LayerWorld : Phys.LayerWorld | Phys.LayerVehicles | Phys.LayerProps | mechs;

        if (GetNodeOrNull<CollisionShape3D>("Collision")?.Shape is BoxShape3D box)
            HalfExtents = new Vector2(box.Size.X, box.Size.Z) * 0.5f;

        PlaceOnMap();
        _spawn = GlobalTransform;
        Hp = MaxHp;
        if (Kind == MoveKind.Plane) Speed = MaxForwardSpeed * 0.8f;
        _exclude.Add(GetRid());
        Weapons = new WeaponSystem(this, GetParent(), _exclude);

        foreach (Node child in GetChildren())
            if (child is MechPilot pilot) Pilot = pilot;
        Pilot?.Attach(this);
    }

    /// <summary>Repaints the plating with a camouflage material. Call before it enters the tree.</summary>
    public void ApplyPaint(Material camo)
    {
        Units.Repaint(this, ArmorMaterial, camo);
        if (camo != null) ArmorMaterial = camo;
    }

    public void SetPreview(bool on, int layer) => Units.SetPreview(this, on, layer);

    float SurfaceAt(float x, float z)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return 0f;
        float ground = terrain.HeightAt(x, z);
        return terrain.HasWater ? Mathf.Max(ground, terrain.WaterLevel) : ground;
    }

    void PlaceOnMap()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        Vector3 p = GlobalPosition;
        float y = terrain.HeightAt(p.X, p.Z) + 0.2f;
        if (IsAircraft) y = SurfaceAt(p.X, p.Z) + CruiseAltitude;
        else if (Kind == MoveKind.Ship) y = terrain.WaterLevel - Draft;
        GlobalPosition = new Vector3(p.X, y, p.Z);
    }

    // ── Main loop ───────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Time += dt;
        var space = GetWorld3D().DirectSpaceState;

        if (IsDestroyed)
        {
            UpdateDead(dt);
            Pilot?.UpdateView(dt);
            Weapons.Update(dt, AimPoint, space, LockTarget);
            return;
        }

        TurnInput = 0f;
        Fire1 = Fire2 = Fire3 = Jump = Descend = false;
        Pilot?.Think(dt);
        Throttle = Mathf.Clamp(Throttle, -ReverseFraction, 1f);
        Blocked = false;

        switch (Kind)
        {
            case MoveKind.Helicopter: Hover(dt); break;
            case MoveKind.Plane: Fly(dt); break;
            default: Drive(dt); break;
        }
        KeepOnMap();

        UpdateTurret(dt);
        foreach (var rotor in _rotors) rotor.RotateY(28f * dt);
        Pilot?.UpdateView(dt);
        UpdateAim(space);

        Weapons.DamageScale = DamageDealtScale;
        Weapons.Mask = Units.FireMask(Team);
        Weapons.CarrierVelocity = Velocity;
        if (Pilot?.CanFire ?? false)
        {
            if (Fire1) Weapons.FireGroup(1, AimPoint, space);
            if (Fire2) Weapons.FireGroup(2, AimPoint, space);
            if (Fire3) Weapons.FireGroup(3, AimPoint, space);
        }
        Weapons.Update(dt, AimPoint, space, LockTarget);

        if (_engine != null)
        {
            if (!_engine.Playing) _engine.Play();
            _engine.PitchScale = 0.8f + 0.6f * Mathf.Clamp(Mathf.Abs(Speed) / MaxForwardSpeed, 0f, 1f);
        }
    }

    void Accelerate(float target, float dt)
    {
        bool speedingUp = Mathf.Abs(target) > Mathf.Abs(Speed) && target * Speed >= 0f;
        Speed = Mathf.MoveToward(Speed, target, (speedingUp ? Acceleration : Deceleration) * dt);
    }

    /// <summary>Tanks, armoured cars, amphibians and ships: throttle and steering on the ground or the water.</summary>
    void Drive(float dt)
    {
        var terrain = MapTerrain.Instance;
        Vector3 p = GlobalPosition;
        float depth = terrain?.WaterDepthAt(p.X, p.Z) ?? 0f;
        Vector3 forward = -GlobalTransform.Basis.Z;

        float factor = 1f;
        bool afloat = false;
        if (Kind == MoveKind.Ground)
        {
            // Not built for water: bogs down.
            factor = Mathf.Lerp(1f, 0.25f, Mathf.Clamp((depth - 0.6f) / 1.2f, 0f, 1f));
        }
        else if (Kind == MoveKind.Amphibious)
        {
            afloat = depth > Draft;
            if (afloat) factor = 0.55f;
        }
        else
        {
            afloat = true;
            // A ship stops rather than run aground.
            Vector3 ahead = p + forward * (Mathf.Sign(Throttle) * BowDistance);
            if (terrain == null || terrain.WaterDepthAt(ahead.X, ahead.Z) < Draft + 0.3f)
            {
                Blocked = true;
                factor = 0f;
            }
        }

        // Ships need way on to answer the helm; tracked things can turn on the spot.
        float steer = Kind == MoveKind.Ship ? Mathf.Clamp(Mathf.Abs(Speed) / (MaxForwardSpeed * 0.3f), 0.15f, 1f) : 1f;
        Rotation = new Vector3(0f, Rotation.Y + Mathf.Clamp(TurnInput, -1f, 1f) * TurnRate * steer * dt, 0f);
        Accelerate(Throttle * MaxForwardSpeed * factor, dt);

        forward = -GlobalTransform.Basis.Z;
        Vector3 v = forward * Speed;
        if (afloat && terrain != null) v.Y = (terrain.WaterLevel - Draft - p.Y) * 4f;
        else v.Y = IsOnFloor() ? -1f : Velocity.Y - Gravity * dt;
        Velocity = v;
        FloorSnapLength = afloat ? 0f : 0.6f;
        MoveAndSlide();

        if (depth > 0.2f && Mathf.Abs(Speed) > 1f && (++_wake & 31) == 0) WaterSurface.AddRipple(GlobalPosition, afloat ? 1.5f : 0.8f);

        Vector3 hv = new Vector3(Velocity.X, 0f, Velocity.Z);
        if (IsOnWall() && hv.Length() < Mathf.Abs(Speed) * 0.8f)
        {
            Speed = Mathf.Sign(Speed) * hv.Length();
            Blocked = true;
        }

        // The hull follows the slope under it (or rides level on the water).
        if (_hull == null) return;
        float pitch = 0f, roll = 0f;
        if (!afloat && terrain != null)
        {
            Vector3 right = GlobalTransform.Basis.X;
            p = GlobalPosition;
            Vector3 f = p + forward * 2f, b = p - forward * 2f, l = p - right * 1.3f, r = p + right * 1.3f;
            pitch = Mathf.Atan2(terrain.HeightAt(f.X, f.Z) - terrain.HeightAt(b.X, b.Z), 4f);
            roll = Mathf.Atan2(terrain.HeightAt(r.X, r.Z) - terrain.HeightAt(l.X, l.Z), 2.6f);
        }
        float k = 1f - Mathf.Exp(-6f * dt);
        _tilt = Mathf.Lerp(_tilt, pitch, k);
        _roll = Mathf.Lerp(_roll, roll, k);
        _hull.Rotation = new Vector3(_tilt, 0f, _roll);
    }

    /// <summary>Helicopter: flies where it points, climbs and sinks on command (the AI holds its cruise altitude).</summary>
    void Hover(float dt)
    {
        Rotation = new Vector3(0f, Rotation.Y + Mathf.Clamp(TurnInput, -1f, 1f) * TurnRate * dt, 0f);
        Accelerate(Throttle * MaxForwardSpeed, dt);

        Vector3 p = GlobalPosition;
        float floor = SurfaceAt(p.X, p.Z) + MinAltitude;
        float climb;
        if (Pilot is PlayerPilot) climb = ((Jump ? 1f : 0f) - (Descend ? 1f : 0f)) * ClimbRate;
        else climb = Mathf.Clamp((floor - MinAltitude + CruiseAltitude - p.Y) * 0.8f, -ClimbRate, ClimbRate);
        _climb = Mathf.Lerp(_climb, climb, 1f - Mathf.Exp(-3f * dt));

        Velocity = -GlobalTransform.Basis.Z * Speed + Vector3.Up * _climb;
        MoveAndSlide();
        p = GlobalPosition;
        GlobalPosition = new Vector3(p.X, Mathf.Clamp(p.Y, floor, floor + MaxAltitude), p.Z);

        if (_hull == null) return;
        // Nose down to go forward, leaning into turns.
        float k = 1f - Mathf.Exp(-4f * dt);
        _tilt = Mathf.Lerp(_tilt, -0.22f * Speed / MaxForwardSpeed, k);
        _roll = Mathf.Lerp(_roll, -0.25f * Mathf.Clamp(TurnInput, -1f, 1f), k);
        _hull.Rotation = new Vector3(_tilt, 0f, _roll);
    }

    /// <summary>Plane: always moving; the throttle sets the speed, TargetPitch the climb, steering banks it round.</summary>
    void Fly(float dt)
    {
        float turn = Mathf.Clamp(TurnInput, -1f, 1f);
        Rotation = new Vector3(0f, Rotation.Y + turn * TurnRate * dt, 0f);
        float want = Mathf.Lerp(StallFraction, 1f, Mathf.Clamp(Throttle, 0f, 1f)) * MaxForwardSpeed;
        Speed = Mathf.MoveToward(Speed, want, Acceleration * dt);
        _climb = Mathf.MoveToward(_climb, Mathf.Clamp(TargetPitch, MinPitch, MaxPitch), PitchRate * dt);

        Vector3 p = GlobalPosition;
        float floor = SurfaceAt(p.X, p.Z) + MinAltitude;
        // Pull out rather than fly into the ground.
        if (p.Y <= floor + 2f && _climb < 0.05f) _climb = 0.05f;

        Vector3 dir = -GlobalTransform.Basis.Z * Mathf.Cos(_climb) + Vector3.Up * Mathf.Sin(_climb);
        Velocity = dir * Speed;
        p += Velocity * dt;
        GlobalPosition = new Vector3(p.X, Mathf.Clamp(p.Y, floor, floor + MaxAltitude), p.Z);

        _roll = Mathf.Lerp(_roll, turn * BankAngle, 1f - Mathf.Exp(-3f * dt));
        if (_hull != null) _hull.Rotation = new Vector3(_climb, 0f, _roll);
    }

    void KeepOnMap()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        float limit = terrain.Size * 0.5f - 12f;
        Vector3 p = GlobalPosition;
        if (Mathf.Abs(p.X) <= limit && Mathf.Abs(p.Z) <= limit) return;
        if (Kind == MoveKind.Plane)
        {
            // A plane can't stop at the edge: it comes back in from the opposite side.
            if (Mathf.Abs(p.X) > limit) p.X = -Mathf.Sign(p.X) * (limit - 5f);
            if (Mathf.Abs(p.Z) > limit) p.Z = -Mathf.Sign(p.Z) * (limit - 5f);
            GlobalPosition = p;
            return;
        }
        GlobalPosition = new Vector3(Mathf.Clamp(p.X, -limit, limit), p.Y, Mathf.Clamp(p.Z, -limit, limit));
        Blocked = true;
    }

    void UpdateTurret(float dt)
    {
        if (_turret == null) return;
        if (MaxTwist >= Mathf.Pi - 0.01f)
        {
            // All-round traverse: go the short way.
            float diff = Mathf.Wrap(TargetYaw - _yaw, -Mathf.Pi, Mathf.Pi);
            _yaw = Mathf.Wrap(_yaw + Mathf.Clamp(diff, -TwistRate * dt, TwistRate * dt), -Mathf.Pi, Mathf.Pi);
        }
        else _yaw = Mathf.MoveToward(_yaw, Mathf.Clamp(TargetYaw, -MaxTwist, MaxTwist), TwistRate * dt);
        _pitch = Mathf.MoveToward(_pitch, Mathf.Clamp(TargetPitch, MinPitch, MaxPitch), PitchRate * dt);
        _turret.Rotation = new Vector3(0f, _yaw, 0f);
        if (_gun != null) _gun.Rotation = new Vector3(_pitch, 0f, 0f);
    }

    void UpdateAim(PhysicsDirectSpaceState3D space)
    {
        // The AI can do with a shorter sight line, checked every other tick.
        bool player = Pilot is PlayerPilot;
        if (!player && (++_tick & 1) == 1) return;
        Transform3D view = default;
        if (Pilot == null || !Pilot.TryGetAimView(out view))
            view = AimAnchor.GlobalTransform.Orthonormalized();
        Vector3 origin = view.Origin;
        Vector3 dir = -view.Basis.Z;
        // Start the ray level with the vehicle so a chase camera can't aim at things behind it.
        float start = Mathf.Max(0f, (ViewPivot - origin).Dot(dir));
        Vector3 from = origin + dir * start;
        Vector3 to = origin + dir * (player ? 1500f : 700f);
        if (Phys.Ray(space, from, to, Units.FireMask(Team), _exclude, out var hit))
        {
            AimPoint = hit.Position;
            AimCollider = hit.Collider;
        }
        else
        {
            AimPoint = to;
            AimCollider = null;
        }
    }

    // ── Damage ──────────────────────────────────────────────────

    public void TakeDamage(float amount, Vector3 point)
    {
        if (IsDestroyed) return;
        Hp -= amount;
        Shake += 0.1f;
        Pilot?.OnHit();
        Node world = GetParent();
        if (GD.Randf() < 0.25f) Debris.Burst(world, point, 1, 0.3f, ArmorMaterial, Vector3.Up * 3f, 4f);
        // Every fifth of the hull lost, something comes off.
        int due = Mathf.FloorToInt((1f - Mathf.Max(Hp, 0f) / MaxHp) * 5f);
        while (_fittingsLost < due && _fittings.Count > 0)
        {
            _fittingsLost++;
            int pick = (int)(GD.Randi() % (uint)_fittings.Count);
            var fitting = _fittings[pick];
            _fittings.RemoveAt(pick);
            Vector3 away = (fitting.GlobalPosition - GlobalPosition).Normalized() * 4f + Vector3.Up * 3f + Velocity;
            _torn.Add(Debris.Detach(fitting, world, away, 300f));
        }
        if (Hp > 0f) return;

        Hp = 0f;
        RespawnLeft = RespawnTime;
        Throttle = 0f;
        Weapons.CancelBeams();
        _engine?.Stop();
        CollisionLayer = 0;
        _falling = IsAircraft;
        if (!IsAircraft) Speed = 0f;
        Explode();
        foreach (var mesh in _meshes) mesh.MaterialOverride = CharredMaterial;
        Debris.Burst(world, AimCenter, 8, 0.7f, CharredMaterial, Vector3.Up * 5f + Velocity, 10f);
        // The turret is blown clean off.
        if (_turret != null && !IsAircraft)
            _torn.Add(Debris.Detach(_turret, world, new Vector3((float)GD.RandRange(-2.0, 2.0), (float)GD.RandRange(7.0, 11.0), (float)GD.RandRange(-2.0, 2.0)) + Velocity, 4000f));
        if (!IsAircraft && Kind != MoveKind.Ship) MapTerrain.Instance?.AddCrater(GlobalPosition, 4f, 0.8f);
        if (WreckFireScene != null)
        {
            _wreckFx = WreckFireScene.Instantiate<Node3D>();
            AddChild(_wreckFx);
            _wreckFx.Position = Vector3.Up * CenterHeight;
        }
        Pilot?.OnKilled();
        if (Team == Team.Enemy) Game.Kills++;
    }

    void Explode()
    {
        if (ExplosionScene == null) return;
        var boom = ExplosionScene.Instantiate<Node3D>();
        GetParent().AddChild(boom);
        boom.GlobalPosition = AimCenter;
    }

    void UpdateDead(float dt)
    {
        if (_falling)
        {
            // Shot down: it comes down where it was heading, tumbling, and blows up again on the ground.
            Vector3 v = Velocity;
            v.Y -= Gravity * dt;
            Velocity = v;
            Vector3 p = GlobalPosition + v * dt;
            float floor = SurfaceAt(p.X, p.Z) + 0.8f;
            if (_hull != null) _hull.Rotate(Vector3.Forward, 1.5f * dt);
            if (p.Y <= floor)
            {
                p.Y = floor;
                _falling = false;
                Velocity = Vector3.Zero;
                Explode();
            }
            GlobalPosition = p;
        }
        if (!Respawns) return;
        RespawnLeft -= dt;
        if (RespawnLeft <= 0f) Respawn();
    }

    void Respawn()
    {
        if (IsInstanceValid(_wreckFx)) _wreckFx.QueueFree();
        _wreckFx = null;
        // Everything that came off goes back on.
        for (int i = _torn.Count - 1; i >= 0; i--)
        {
            _torn[i].Restore();
            if (_torn[i].Part != _turret) _fittings.Add(_torn[i].Part);
        }
        _torn.Clear();
        _fittingsLost = 0;
        for (int i = 0; i < _meshes.Count; i++) _meshes[i].MaterialOverride = _original[i];
        Hp = MaxHp;
        Weapons.Rearm();
        CollisionLayer = _layer;
        GlobalTransform = _spawn;
        Velocity = Vector3.Zero;
        Speed = Kind == MoveKind.Plane ? MaxForwardSpeed * 0.8f : 0f;
        Throttle = 0f;
        _yaw = _pitch = _climb = _roll = _tilt = 0f;
        TargetYaw = TargetPitch = 0f;
        _falling = false;
        if (_hull != null) _hull.Rotation = Vector3.Zero;
        Pilot?.OnRespawn();
    }
}
