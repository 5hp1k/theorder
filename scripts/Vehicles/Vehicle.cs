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
/// Weapon nodes anywhere; nodes named "Rotor…" spin; nodes under "Hull" named "Wheel…" roll with the
/// speed (their axle is their local X) and, on a vehicle without tracks, the front ones steer;
/// "Belt" (a TrackBelt) under "TrackL" / "TrackR" is the track itself, drawn round the wheels of its side;
/// optional "Sounds/Engine" loop.
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
    /// <summary>How fast it rolls and (helicopter) tips over to the commanded attitude, rad/s.</summary>
    [Export] public float AttitudeRate = 1.4f;
    /// <summary>Helicopter: how far it tips to accelerate, radians. Steeper = quicker, and harder to stop.</summary>
    [Export] public float MaxTilt = 0.38f;
    /// <summary>Gravity as the flight model feels it, m/s².</summary>
    [Export] public float FlightGravity = 11f;
    /// <summary>Hitting anything faster than this wrecks an aircraft, m/s. A helicopter set down slower than this lands.</summary>
    [Export] public float CrashSpeed = 6f;

    [ExportGroup("Suspension")]
    /// <summary>How far each wheel can move up and down from its rest position, metres.</summary>
    [Export] public float SuspensionTravel = 0.38f;
    /// <summary>How quickly the wheels follow the ground, per second.</summary>
    [Export] public float SuspensionRate = 16f;
    /// <summary>How much the hull squats under acceleration and dips under braking, radians per m/s².</summary>
    [Export] public float BodyPitchPerAccel = 0.012f;
    /// <summary>How quickly the hull takes up a turn and comes out of it (higher = snappier).</summary>
    [Export] public float TurnResponse = 4.5f;
    /// <summary>Share of the speed lost in a full-rate turn.</summary>
    [Export(PropertyHint.Range, "0,0.8")] public float TurnDrag = 0.3f;

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
    // Wheels: the node, its radius, and how far forward it sits (1 = front axle, 0 = middle and back).
    readonly List<(Node3D node, float radius, float front)> _wheels = new();
    // Suspension: where each wheel sits at rest (hull space) and how far it has travelled from there.
    readonly List<Vector3> _wheelRest = new();
    float[] _wheelTravel = System.Array.Empty<float>();
    // Tracks: the belt of each side, the wheels it runs round, how far it has run, and the vehicle's rate of turn.
    readonly List<(TrackBelt belt, Node3D mount, List<int> wheels, float side)> _belts = new();
    readonly float[] _trackRun = new float[2];
    readonly List<Vector3> _beltWheels = new();
    float _groundYawRate;
    float _squat, _lastSpeed;
    float _rollAngle, _steer;
    bool _steers;
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
    bool _falling, _avoiding;
    float _yawRate, _sink;
    int _tick, _wake;

    void FindWheels()
    {
        if (_hull == null) return;
        float reach = 0.1f;
        var found = new List<Node3D>();
        foreach (Node child in _hull.GetChildren())
        {
            if (child is not Node3D wheel || !child.Name.ToString().StartsWith("Wheel")) continue;
            found.Add(wheel);
            reach = Mathf.Max(reach, Mathf.Abs(wheel.Position.Z));
        }
        // Tracked vehicles turn by their tracks; the wheels inside only roll.
        _steers = _hull.GetNodeOrNull("TrackL") == null;
        foreach (Node3D wheel in found)
        {
            Aabb box = wheel is MeshInstance3D { Mesh: not null } mesh ? mesh.Mesh.GetAabb() : new Aabb(Vector3.One * -0.5f, Vector3.One);
            float radius = Mathf.Max(box.Size.Y, box.Size.Z) * 0.5f;
            _wheels.Add((wheel, Mathf.Max(radius, 0.05f), Mathf.Clamp(-wheel.Position.Z / reach, 0f, 1f)));
            _wheelRest.Add(wheel.Position);
        }
        _wheelTravel = new float[_wheels.Count];
        foreach (string name in new[] { "TrackL", "TrackR" })
        {
            if (_hull.GetNodeOrNull<Node3D>(name) is not { } mount || mount.GetNodeOrNull<TrackBelt>("Belt") is not { } belt) continue;
            float side = Mathf.Sign(belt.Position.X);
            var round = new List<int>();
            for (int i = 0; i < _wheelRest.Count; i++)
                if (Mathf.Sign(_wheelRest[i].X) == side) round.Add(i);
            _belts.Add((belt, mount, round, side));
        }
    }

    /// <summary>Half the distance between the two tracks.</summary>
    float TrackGauge => _belts.Count > 0 ? Mathf.Abs(_belts[0].belt.Position.X) : 0f;

    /// <summary>Draws each track round its wheels where they are now.</summary>
    void LayTracks()
    {
        foreach (var (belt, mount, round, side) in _belts)
        {
            // A track that has been shot off lies where it fell.
            if (!IsInstanceValid(belt) || !IsInstanceValid(mount) || mount.GetParent() != _hull) continue;
            _beltWheels.Clear();
            foreach (int i in round)
            {
                Vector3 rest = _wheelRest[i];
                _beltWheels.Add(new Vector3(rest.Z, rest.Y + _wheelTravel[i], _wheels[i].radius));
            }
            belt.Lay(_beltWheels, _trackRun[side < 0f ? 0 : 1]);
        }
    }

    void TurnWheels(float dt)
    {
        if (_wheels.Count == 0) return;
        _rollAngle -= Speed * dt;
        // A turning tank's tracks run at different speeds: the outer one faster, the inner one slower
        // (or backwards, turning on the spot). Forward is -Z; a positive rate of turn is to the left.
        float gauge = TrackGauge;
        _trackRun[0] += (Speed - _groundYawRate * gauge) * dt;
        _trackRun[1] += (Speed + _groundYawRate * gauge) * dt;
        _steer = Mathf.MoveToward(_steer, _steers ? Mathf.Clamp(TurnInput, -1f, 1f) * 0.5f : 0f, 2.5f * dt);
        for (int i = 0; i < _wheels.Count; i++)
        {
            var (wheel, radius, front) = _wheels[i];
            // One that has been shot off lies where it fell.
            if (!IsInstanceValid(wheel) || wheel.GetParent() != _hull) continue;
            // _rollAngle is minus the distance covered; the wheels inside a track turn with that track.
            float rolled = _belts.Count > 0 ? -_trackRun[_wheelRest[i].X < 0f ? 0 : 1] : _rollAngle;
            wheel.Basis = new Basis(Vector3.Up, _steer * front) * new Basis(Vector3.Right, rolled / radius);
        }
    }

    /// <summary>
    /// Suspension: every wheel reaches for the ground under it, as far as its travel allows, so on rough
    /// going the wheels ride up and down while the hull stays put. The tracks are then drawn round them.
    /// </summary>
    void Suspend(float dt, bool afloat)
    {
        if (_wheels.Count == 0) return;
        var terrain = MapTerrain.Instance;
        Transform3D hull = _hull.GlobalTransform;
        float upright = Mathf.Max(hull.Basis.Y.Y, 0.5f);
        float ease = 1f - Mathf.Exp(-SuspensionRate * dt);
        float shoe = _belts.Count > 0 ? _belts[0].belt.Thickness : 0f;
        for (int i = 0; i < _wheels.Count; i++)
        {
            var (wheel, radius, _) = _wheels[i];
            Vector3 rest = _wheelRest[i];
            float want = 0f;
            if (!afloat && terrain != null)
            {
                Vector3 at = hull * rest;
                // (a wheel inside a track stands on the track, not on the ground)
                want = Mathf.Clamp((FloorAt(at.X, at.Z) + radius + shoe - at.Y) / upright, -SuspensionTravel, SuspensionTravel);
            }
            else want = -SuspensionTravel * 0.6f; // hanging free in the water
            _wheelTravel[i] = Mathf.Lerp(_wheelTravel[i], want, ease);
            if (IsInstanceValid(wheel) && wheel.GetParent() == _hull)
                wheel.Position = rest + Vector3.Up * _wheelTravel[i];

        }
        LayTracks();
    }

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
        FindWheels();
        // Everything bolted to the hull except the main body can be shot off.
        if (_hull != null)
            foreach (Node child in _hull.GetChildren())
                if (child is Node3D fitting and not Marker3D and not Weapon && child.GetIndex() > 0) _fittings.Add(fitting);

        // The hull itself is what shots hit. Ground vehicles and ships are also solid to mechs.
        uint hit = Team == Team.Player ? Phys.LayerHitPlayer : Phys.LayerHitEnemy;
        // Mech bodies only get in the way if mechs aren't allowed to walk over vehicles.
        uint mechs = GameConfig.MechsCrush ? 0 : Phys.LayerMech | Phys.LayerTargets;
        CollisionLayer = _layer = IsAircraft ? hit : hit | Phys.LayerVehicles;
        // Aircraft fly into everything there is to fly into.
        CollisionMask = _mask = IsAircraft
            ? Phys.LayerWorld | Phys.LayerProps | Phys.LayerVehicles | Phys.LayerMech | Phys.LayerTargets | Phys.LayerWreck
            : Phys.LayerWorld | Phys.LayerVehicles | Phys.LayerProps | mechs;

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

    /// <summary>Repaints the armour (see <see cref="PaintJob"/>).</summary>
    public void ApplyPaint(PaintJob paint) => ArmorMaterial = paint.Apply(this, ArmorMaterial);

    /// <summary>Shows a picture on the plates named "Insignia…" in the scene, or hides them.</summary>
    public void ApplyInsignia(Texture2D picture) => Units.ApplyInsignia(this, picture);

    public void SetPreview(bool on, int layer) => Units.SetPreview(this, on, layer);

    /// <summary>What the wheels stand on here: the ground, or a bridge deck over it.</summary>
    float FloorAt(float x, float z)
    {
        float ground = MapTerrain.Instance?.HeightAt(x, z) ?? 0f;
        return ChunkedBuilding.DeckAt(x, z, GlobalPosition.Y + 1.5f, out float deck) ? Mathf.Max(ground, deck) : ground;
    }

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

        if (IsAircraft) LookAhead();
        switch (Kind)
        {
            case MoveKind.Helicopter: Hover(dt); break;
            case MoveKind.Plane: Fly(dt); break;
            default: Drive(dt); break;
        }
        KeepOnMap();

        UpdateTurret(dt);
        foreach (var rotor in _rotors) rotor.RotateY(28f * dt);
        TurnWheels(dt);
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
        // Over the water on a bridge isn't in it.
        if (depth > 0f && terrain != null && p.Y > terrain.WaterLevel && ChunkedBuilding.DeckAt(p.X, p.Z, p.Y + 1f, out _)) depth = 0f;
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
        float wantTurn = Mathf.Clamp(TurnInput, -1f, 1f) * TurnRate * steer;
        if (Kind != MoveKind.Ship && !afloat)
        {
            float pace = Mathf.Abs(Speed) / Mathf.Max(MaxForwardSpeed, 0.1f);
            // Wheels steer only while they roll (and the other way in reverse); tracks can pivot on the
            // spot, but at speed the hull's weight resists being swung round.
            if (_steers) wantTurn *= Mathf.Clamp(pace / 0.3f, 0f, 1f) * (Speed < -0.2f ? -1f : 1f);
            else wantTurn *= Mathf.Lerp(1f, 0.6f, pace);
            // Slewing a hull sideways across the ground costs speed.
            factor *= 1f - TurnDrag * Mathf.Abs(_groundYawRate) / Mathf.Max(TurnRate, 0.01f);
        }
        // The hull has inertia about its vertical axis: it winds up into a turn and out of it.
        _groundYawRate = Mathf.Lerp(_groundYawRate, wantTurn, 1f - Mathf.Exp(-TurnResponse * dt));
        Rotation = new Vector3(0f, Rotation.Y + _groundYawRate * dt, 0f);
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
            pitch = Mathf.Atan2(FloorAt(f.X, f.Z) - FloorAt(b.X, b.Z), 4f);
            roll = Mathf.Atan2(FloorAt(r.X, r.Z) - FloorAt(l.X, l.Z), 2.6f);
        }
        float k = 1f - Mathf.Exp(-6f * dt);
        _tilt = Mathf.Lerp(_tilt, pitch, k);
        _roll = Mathf.Lerp(_roll, roll, k);
        // The body rocks on its springs: back on its haunches pulling away, nose down under braking.
        float accel = (Speed - _lastSpeed) / Mathf.Max(dt, 1e-4f);
        _lastSpeed = Speed;
        _squat = Mathf.Lerp(_squat, afloat ? 0f : Mathf.Clamp(accel * BodyPitchPerAccel, -0.09f, 0.09f), 1f - Mathf.Exp(-5f * dt));
        _hull.Rotation = new Vector3(_tilt + _squat, 0f, _roll);
        Suspend(dt, afloat);
    }

    /// <summary>
    /// Helicopter. The rotor lifts along the machine's own "up": tipped forward, part of that lift pulls
    /// it along, so it gathers speed gradually, keeps it when the nose comes up, and slides wide in turns.
    /// Throttle = how far to tip forward (or back); steering = yaw; Jump / Descend = more or less lift.
    /// It hovers by itself with the controls let go. The AI holds its cruise altitude.
    /// </summary>
    void Hover(float dt)
    {
        float turn = Mathf.Clamp(TurnInput, -1f, 1f);
        _yawRate = Mathf.Lerp(_yawRate, turn * TurnRate, 1f - Mathf.Exp(-4f * dt));
        Rotation = new Vector3(0f, Rotation.Y + _yawRate * dt, 0f);

        // Attitude follows the controls at its own pace.
        _tilt = Mathf.MoveToward(_tilt, -Throttle * MaxTilt, AttitudeRate * dt);
        _roll = Mathf.MoveToward(_roll, turn * MaxTilt * 0.6f, AttitudeRate * dt);

        Vector3 p = GlobalPosition;
        float ground = SurfaceAt(p.X, p.Z);
        float lift; // as a share of the machine's weight
        if (Pilot is PlayerPilot) lift = 1f + ((Jump ? 1f : 0f) - (Descend ? 1f : 0f)) * ClimbRate / 10f;
        else
        {
            float wantClimb = Mathf.Clamp((ground + CruiseAltitude - p.Y) * 0.8f, -ClimbRate, ClimbRate);
            if (_avoiding) wantClimb = ClimbRate;
            lift = 1f + Mathf.Clamp((wantClimb - Velocity.Y) * 0.25f, -0.9f, 0.9f);
        }

        Basis body = GlobalTransform.Basis * Basis.FromEuler(new Vector3(_tilt, 0f, _roll));
        // Enough extra lift to make up for the tilt, so tipping forward doesn't by itself lose height.
        float hold = 1f / Mathf.Max(body.Y.Y, 0.6f);
        Vector3 accel = body.Y * (FlightGravity * lift * hold) + Vector3.Down * FlightGravity;

        // Air drag: enough along the nose to top out at MaxForwardSpeed at full tilt, much more sideways.
        Vector3 v = Velocity;
        Vector3 forward = -GlobalTransform.Basis.Z, right = GlobalTransform.Basis.X;
        float along = v.Dot(forward), across = v.Dot(right);
        float drag = FlightGravity * Mathf.Tan(MaxTilt) / Mathf.Max(MaxForwardSpeed, 1f);
        accel -= forward * (along * drag) + right * (across * drag * 2.5f) + Vector3.Up * (v.Y * 0.9f);
        v += accel * dt;
        v.Y = Mathf.Clamp(v.Y, -ClimbRate * 2f, ClimbRate);
        if (p.Y > ground + MaxAltitude) v.Y = Mathf.Min(v.Y, 0f);

        Velocity = v;
        float impact = v.Length();
        MoveAndSlide();
        Speed = Velocity.Dot(forward);

        if (GetSlideCollisionCount() > 0)
        {
            var hit = GetSlideCollision(0);
            // How hard it came against whatever it met.
            float closing = -v.Dot(hit.GetNormal());
            bool ground_ = hit.GetCollider() is MapTerrain && hit.GetNormal().Y > 0.7f;
            if (closing > CrashSpeed || (!ground_ && impact > CrashSpeed)) Crash(hit.GetPosition(), hit.GetCollider());
            // Otherwise it has set down (or nudged something): the skids take the speed off.
            else Velocity *= Mathf.Exp(-3f * dt);
        }
        else if (GlobalPosition.Y < ground + 0.3f) Crash(GlobalPosition, null); // into the water

        if (_hull != null) _hull.Rotation = new Vector3(_tilt, 0f, _roll);
    }

    /// <summary>
    /// Plane. It trades speed and height: the engine pushes, drag holds it back, climbing costs speed and
    /// diving gives it. Steering rolls it and the bank is what turns it — faster round the tighter the
    /// bank and the slower it flies; the stick (TargetPitch) raises and lowers the nose with less
    /// authority at low speed. Too slow and the wing stops flying: the nose drops and it sinks until it
    /// has picked up speed again.
    /// </summary>
    void Fly(float dt)
    {
        float top = MaxForwardSpeed, stall = StallFraction * top;
        float authority = Mathf.Clamp(Speed / (top * 0.6f), 0.25f, 1f);
        bool stalled = Speed < stall;

        float turn = Mathf.Clamp(TurnInput, -1f, 1f);
        _roll = Mathf.MoveToward(_roll, turn * BankAngle, AttitudeRate * authority * dt);
        float wantPitch = Mathf.Clamp(_avoiding ? MaxPitch : TargetPitch, MinPitch, MaxPitch);
        // Stalled, the nose falls whatever the stick says.
        if (stalled) wantPitch = Mathf.Min(wantPitch, MinPitch);
        _climb = Mathf.MoveToward(_climb, wantPitch, PitchRate * authority * dt);

        // A banked wing turns the plane: rate = g · tan(bank) / speed.
        float rate = FlightGravity * Mathf.Tan(Mathf.Clamp(_roll, -1.2f, 1.2f)) / Mathf.Max(Speed, stall * 0.7f);
        Rotation = new Vector3(0f, Rotation.Y + rate * dt, 0f);

        // Thrust against drag (which settles it at top speed on full throttle), plus the pull of the slope
        // it is flying along and the price of a hard turn.
        float thrust = Acceleration * Mathf.Clamp(Throttle, 0f, 1f);
        float drag = Acceleration * (Speed * Speed) / (top * top);
        float turning = Mathf.Abs(Mathf.Tan(_roll)) * 1.5f;
        Speed += (thrust - drag - FlightGravity * Mathf.Sin(_climb) - turning) * dt;
        Speed = Mathf.Max(Speed, stall * 0.4f);

        Vector3 dir = -GlobalTransform.Basis.Z * Mathf.Cos(_climb) + Vector3.Up * Mathf.Sin(_climb);
        Vector3 v = dir * Speed;
        // Below flying speed the wing carries less than the weight and the rest is falling.
        if (stalled) _sink = Mathf.Min(_sink + FlightGravity * (1f - Speed / stall) * dt, 40f);
        else _sink = Mathf.MoveToward(_sink, 0f, FlightGravity * dt);
        v.Y -= _sink;
        Velocity = v;

        Vector3 p = GlobalPosition, next = p + v * dt;
        float ceiling = SurfaceAt(next.X, next.Z) + MaxAltitude;
        if (next.Y > ceiling)
        {
            next.Y = ceiling;
            _climb = Mathf.Min(_climb, 0f);
        }
        // Anything in the way — ground, water, a tower, another machine — is the end of the flight.
        if (Phys.Ray(GetWorld3D().DirectSpaceState, p, next + v.Normalized() * (HalfExtents.Y * 0.8f), _mask, _exclude, out var hit))
        {
            GlobalPosition = hit.Position - v.Normalized() * 2f;
            Crash(hit.Position, hit.Collider);
            return;
        }
        if (next.Y < SurfaceAt(next.X, next.Z) + 1f)
        {
            GlobalPosition = next;
            Crash(next, null);
            return;
        }
        GlobalPosition = next;
        if (_hull != null) _hull.Rotation = new Vector3(_climb, 0f, _roll);
    }

    /// <summary>Flew into something: the aircraft is destroyed there and then, and what it hit feels it.</summary>
    void Crash(Vector3 at, GodotObject into)
    {
        if (IsDestroyed) return;
        var space = GetWorld3D().DirectSpaceState;
        float height = GlobalPosition.Y - SurfaceAt(GlobalPosition.X, GlobalPosition.Z);
        TakeDamage(MaxHp * 4f, at);
        // On the ground already: no long fall, it burns where it hit.
        if (height < 6f)
        {
            _falling = false;
            Velocity = Vector3.Zero;
        }
        else Velocity = new Vector3(Velocity.X * 0.2f, Mathf.Min(Velocity.Y, 0f), Velocity.Z * 0.2f);
        Phys.Blast(space, at, 9f, 45f, Phys.LayerProps | Phys.LayerVehicles | Phys.LayerHitPlayer | Phys.LayerHitEnemy, _exclude);
        if (into is MapTerrain terrain) terrain.AddCrater(at, 4.5f, 1.1f);
        Impacts.Water(GetParent(), at + Vector3.Up * 2f, at + Vector3.Down * 2f, 3f);
    }

    /// <summary>
    /// The AI doesn't fly by sight, so it gets this much help: a look along its path every few ticks, and
    /// if something is in the way it climbs until the way is clear.
    /// </summary>
    void LookAhead()
    {
        if (Pilot is PlayerPilot || (_tick % 8) != 0) return;
        Vector3 v = Velocity;
        if (v.LengthSquared() < 4f)
        {
            _avoiding = false;
            return;
        }
        Vector3 from = GlobalPosition;
        float reach = Mathf.Max(60f, v.Length() * 4f);
        _avoiding = Phys.Ray(GetWorld3D().DirectSpaceState, from, from + new Vector3(v.X, 0f, v.Z).Normalized() * reach + Vector3.Down * 4f,
                             Phys.LayerWorld | Phys.LayerProps, _exclude, out _);
    }

    void KeepOnMap()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        float limit = terrain.PlayHalf;
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
        _yaw = _pitch = _climb = _roll = _tilt = _yawRate = _sink = _squat = 0f;
        _avoiding = false;
        TargetYaw = TargetPitch = 0f;
        _falling = false;
        if (_hull != null) _hull.Rotation = Vector3.Zero;
        Pilot?.OnRespawn();
    }
}
