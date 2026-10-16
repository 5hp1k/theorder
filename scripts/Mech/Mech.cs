using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A mech chassis: MechWarrior-style throttle and leg steering, independent torso twist, procedural
/// digitigrade legs, heat with shutdown, sectional damage, weapon groups, ragdoll death.
/// The body (meshes, hit boxes, weapons, markers, sounds) lives in the mech scene; the commands come
/// from a <see cref="MechPilot"/> child, so the same scene serves the player and the AI.
/// </summary>
public partial class Mech : CharacterBody3D, IDrivable
{
    public static readonly List<Mech> All = new();

    [Export] public string DisplayName { get; set; } = "Мех";
    /// <summary>Mass in tonnes: what the hangar shows, and what the class (light, medium, heavy) is reckoned from.</summary>
    [Export] public int Tonnage = DefaultTonnage;
    public const int DefaultTonnage = 50;

    /// <summary>Weight classes, as in BattleTech: the singular for a machine, the plural for a heading.</summary>
    public static readonly string[] ClassNames = { "Лёгкий", "Средний", "Тяжёлый", "Штурмовой" };
    public static readonly string[] ClassHeadings = { "Лёгкие", "Средние", "Тяжёлые", "Штурмовые" };
    /// <summary>The weight class of a mech of this many tonnes: 0 light (under 40), 1 medium, 2 heavy, 3 assault (80 and over).</summary>
    public static int WeightClass(int tonnage) => tonnage < 40 ? 0 : tonnage < 60 ? 1 : tonnage < 80 ? 2 : 3;

    [ExportGroup("Movement")]
    [Export] public float MaxForwardSpeed { get; set; } = 13.9f;   // m/s (~50 km/h)
    [Export] public float ReverseFraction { get; set; } = 0.4f;    // reverse top speed as a fraction of forward
    [Export] public float Acceleration = 5f;         // m/s²
    [Export] public float Deceleration = 8f;
    [Export] public float TurnRateStanding = 1.2f;   // rad/s
    [Export] public float TurnRateFull = 0.75f;
    [Export] public float ThrottleRate { get; set; } = 0.8f;       // throttle units per second
    [Export] public float Gravity = 20f;
    /// <summary>Height of the Hips node above the ground when standing; must match the scene.</summary>
    [Export] public float HipHeight = 5.6f;

    [ExportGroup("Gait")]
    /// <summary>Distance covered per step at top speed. Longer = slower, heavier cadence.</summary>
    [Export] public float StepLength = 4.6f;
    /// <summary>How high the feet are lifted at top speed.</summary>
    [Export] public float StepHeight = 0.9f;
    /// <summary>Share of the cycle a foot is in the air at top speed; over 0.5 is a run.</summary>
    [Export(PropertyHint.Range, "0.3,0.7")] public float RunSwing = 0.5f;
    /// <summary>Sideways shift of the body onto the standing leg, metres.</summary>
    [Export] public float BodySway = 0.38f;
    /// <summary>How far the pelvis swings round with each stride, radians.</summary>
    [Export] public float HipSwing = 0.21f;
    /// <summary>How much the pelvis drops onto the leg that has just taken the weight (1 = as designed, 0 = rigid).</summary>
    [Export] public float HipGive = 1f;

    [ExportGroup("Jump jets")]
    [Export] public bool HasJumpJets { get; set; }
    /// <summary>Upward acceleration while burning, m/s² (gravity is taken off this).</summary>
    [Export] public float JumpThrust = 36f;
    [Export] public float JumpClimbSpeed = 13f;
    /// <summary>Seconds of burn on a full tank.</summary>
    [Export] public float JumpFuelTime = 3.5f;
    /// <summary>Seconds on the ground to refill.</summary>
    [Export] public float JumpRecharge = 9f;
    /// <summary>Heat per second of burn.</summary>
    [Export] public float JumpHeat = 0.09f;

    [ExportGroup("Torso")]
    [Export] public float MaxTwist { get; set; } = 1.75f;          // rad (~100°)
    [Export] public float TwistRate = 1.1f;          // rad/s (the arms are quicker, see ArmTrackRate)
    [Export] public float PitchRate = 1.0f;
    [Export] public float MinPitch { get; set; } = -0.35f;
    [Export] public float MaxPitch { get; set; } = 0.45f;

    [ExportGroup("Arms")]
    /// <summary>
    /// How far the arms reach out from where the torso points, each way, radians: as in MechWarrior 5 the
    /// mouse sets where to aim, the arms get there first and the torso follows at its own pace. Weapons in
    /// the arms fire where the arms point (the HUD's second circle); the rest fire where the torso does.
    /// </summary>
    [Export] public float ArmYawReach = 0.55f;
    [Export] public float ArmPitchReach = 0.5f;
    /// <summary>How fast the arms swing to the aim, radians per second.</summary>
    [Export] public float ArmTrackRate = 8f;

    [ExportGroup("Heat")]
    [Export] public float HeatDissipation = 0.085f;  // per second
    /// <summary>
    /// How much faster the heat goes in water, standing in it up to the hips (<see cref="WaterCoolingDepth"/>)
    /// or deeper: 3 = four times as fast. Shallower water cools in proportion.
    /// </summary>
    [Export] public float WaterCooling = 3f;
    [Export] public float WaterCoolingDepth = 2f;
    [Export] public float RestartHeat { get; set; } = 0.4f;
    [Export] public float RestartTime = 1.5f;

    [ExportGroup("Damage")]
    [Export] public float ArmorScale = 1f;
    [Export] public float LeggedSpeedFactor = 0.35f; // top speed with one leg gone
    /// <summary>Radius within which the mech flattens trees, cars and buildings it walks into.</summary>
    [Export] public float TrampleRadius = 2.6f;
    [ExportGroup("Hand-to-hand")]
    /// <summary>How far the striking arm is raised in the wind-up, and how far it follows through, radians.</summary>
    [Export] public float SwingRaise = 1.25f;
    [Export] public float SwingFollow = 0.75f;
    /// <summary>How far the torso twists into a blow, and how far it leans into it, radians.</summary>
    [Export] public float SwingTwist = 0.45f;
    [Export] public float SwingLean = 0.16f;
    [ExportGroup("Recoil")]
    /// <summary>How much the machine gives to the kick of its own guns (1 = as the weapons say; a heavy mech, less).</summary>
    [Export] public float RecoilGive = 1f;
    /// <summary>Damage a foot takes when what it crushed blows up under it.</summary>
    [Export] public float CrushBackfire = 6f;
    [ExportGroup("")]
    [Export] public Material CharredMaterial;
    /// <summary>What a section looks like with its armour shot away: bare structure.</summary>
    [Export] public Material StrippedMaterial;
    /// <summary>Smoke attached to a section when it's destroyed.</summary>
    [Export] public PackedScene DamageSmokeScene;
    /// <summary>Seconds between the killing blow and the reactor going up.</summary>
    [Export] public float ExplosionDelay = 1.2f;
    [Export] public PackedScene ExplosionScene;
    /// <summary>Fire and smoke column attached to the wreck.</summary>
    [Export] public PackedScene WreckFireScene;

    [ExportGroup("Paint")]
    /// <summary>
    /// The shape this mech's foot leaves in the ground: a soft mask (textures/footprints, made by
    /// tools/footprint_textures.gd), the size in metres it covers (X across, Y along) and where on it
    /// the middle of the foot is.
    /// </summary>
    [Export] public Texture2D FootprintTexture;
    [Export] public Vector2 FootprintSize = new(2f, 4f);
    [Export] public Vector2 FootprintCentre;
    /// <summary>The material the repaintable armour uses in this scene; replaced by the chosen camouflage.</summary>
    [Export] public Material ArmorMaterial;
    /// <summary>The material of the stripe; recoloured.</summary>
    [Export] public Material AccentMaterial;

    // ── Set by whoever spawns the mech, before it enters the tree ──
    public Team Team = Team.Enemy;
    public bool Respawns = true;
    public float RespawnTime = 7f;
    /// <summary>Multiplier on the damage this mech's weapons deal.</summary>
    public float DamageDealtScale = 1f;

    // ── State visible to the HUD and the pilots ─────────────────
    public MechPilot Pilot { get; private set; }
    public float Speed { get; private set; }                  // signed forward speed, m/s
    public float SpeedKmh => Speed * 3.6f;
    public float Heat { get; private set; }                   // 1.0 = shutdown
    public bool IsShutdown { get; private set; }
    public float RestartProgress { get; private set; }        // 0..1 while restarting
    public float TorsoYaw => _yaw;                            // relative to legs
    public float TorsoPitch => _pitch;
    public float HeadingDeg => Mathf.PosMod(Mathf.RadToDeg(-(Rotation.Y + _yaw)), 360f);
    public WeaponSystem Weapons { get; private set; }
    public GodotObject AimCollider { get; private set; }
    public Vector3 AimPoint { get; private set; }
    /// <summary>Where the arms are pointing (see <see cref="ArmYawReach"/>).</summary>
    public Vector3 ArmAimPoint { get; private set; }
    /// <summary>Whether this mech's arms aim on their own (it has shoulders for them to swing from).</summary>
    public bool ArmsAim { get; private set; }
    /// <summary>
    /// Where the pilot is pointing the arms apart from the torso (held Alt), radians from where the torso
    /// aims; the arms reach as far as <see cref="ArmYawReach"/> allows. Zero = with the torso.
    /// </summary>
    public float ArmLookYaw { get; set; }
    public float ArmLookPitch { get; set; }
    public DamageModel Damage { get; private set; }
    public bool IsDestroyed => Damage != null && Damage.Dead;
    public float RespawnLeft { get; private set; }
    public Vector3 TorsoCenter => Torso.GlobalTransform * new Vector3(0, 1.1f, 0);
    public Vector3 AimCenter => TorsoCenter;
    Team ITarget.Team => Team;
    public float Integrity => Damage?.TotalFraction ?? 1f;
    public CharacterBody3D Body => this;
    public Node3D CockpitInterior => GetNodeOrNull<Node3D>("Hips/Torso/CockpitCam/Cockpit");
    public Vector3 ViewPivot => Torso.GlobalPosition;
    public float ChaseScale => HipHeight / 5.6f;
    public float HullYaw => Rotation.Y;
    public bool HasHeat => true;
    public bool Blocked => IsOnWall();
    /// <summary>Jump fuel, 0..1.</summary>
    public float JumpFuel { get; private set; } = 1f;
    public bool Airborne { get; private set; }
    public bool Thrusting { get; private set; }
    /// <summary>Depth of water the mech stands in, metres.</summary>
    public float WaterDepth { get; private set; }
    public bool Offline => IsShutdown || IsDestroyed || Repairing;
    public float Time { get; private set; }
    public uint FireMask => Units.FireMask(Team, Pilot is PlayerPilot);

    // ── Commands, set by the pilot each tick ────────────────────
    public float Throttle { get; set; }            // -ReverseFraction..1
    /// <summary>Leg steering, -1..1, positive = left.</summary>
    public float TurnInput { get; set; }
    /// <summary>Where the torso should point, relative to the legs / the horizon (radians).</summary>
    public float TargetYaw { get; set; }
    public float TargetPitch { get; set; }
    public int Fire { get; set; }
    /// <summary>Hold to burn the jump jets.</summary>
    public bool Jump { get; set; }
    public bool Descend { get; set; }
    /// <summary>What guided missiles home on, if anything.</summary>
    public ITarget LockTarget { get; set; }
    /// <summary>Camera shake accumulated from footfalls and hits; the pilot's view consumes it.</summary>
    public float Shake { get; set; }

    public Node3D Hips { get; private set; }
    public Node3D Torso { get; private set; }
    public Node3D AimAnchor { get; private set; }
    public MechLeg[] Legs { get; private set; }

    Node3D[] _arms;
    int _swingArm = -1;
    float _swing;
    readonly Gait _gait = new();
    readonly List<HitBox> _hitBoxes = new();
    readonly List<Node> _damageFx = new();
    readonly Debris.Detached[] _tornArms = new Debris.Detached[2];
    readonly Godot.Collections.Array<Rid> _exclude = new();
    MechRagdoll _ragdoll;
    AudioStreamPlayer3D _sFoot, _sReactor, _sHit, _sAlarm, _sJets;
    Node3D _jets;
    int _tick;
    float _twist, _nod, _nodVel;
    bool _legsInitialized, _exploded;
    Transform3D _spawn;
    uint _bodyLayer, _bodyMask;
    float _yaw, _pitch, _armYaw, _armPitch;
    float _hipY, _bob, _bobVel, _lean, _roll, _list, _listVel;
    float _restartTimer, _explodeTimer, _hitSoundCooldown;
    float _yawRate;
    Vector3 _lastHorizontalVel;

    public override void _EnterTree()
    {
        All.Add(this);
        Units.All.Add(this);
    }

    public override void _ExitTree()
    {
        All.Remove(this);
        Units.All.Remove(this);
        _grime?.Forget();
        if (Game.PlayerUnit == this) Game.PlayerUnit = null;
    }

    public override void _Ready()
    {
        Hips = GetNode<Node3D>("Hips");
        Torso = GetNode<Node3D>("Hips/Torso");
        AimAnchor = GetNode<Node3D>("Hips/Torso/CockpitCam");
        _arms = new[] { GetNode<Node3D>("Hips/Torso/ArmL"), GetNode<Node3D>("Hips/Torso/ArmR") };
        // Only arms that hang from shoulders swing to aim; pods bolted to the hull stay put.
        ArmsAim = GetNodeOrNull("Hips/Torso/UpperArmL") != null;
        _armRest = new[] { _arms[0].Position, _arms[1].Position };
        _armRestBasis = new[] { _arms[0].Basis, _arms[1].Basis };
        Legs = new[] { GetNode<MechLeg>("LegL"), GetNode<MechLeg>("LegR") };
        _ragdoll = GetNodeOrNull<MechRagdoll>("Ragdoll");
        _sFoot = GetNodeOrNull<AudioStreamPlayer3D>("Sounds/Footstep");
        _sReactor = GetNodeOrNull<AudioStreamPlayer3D>("Sounds/Reactor");
        _sHit = GetNodeOrNull<AudioStreamPlayer3D>("Sounds/Hit");
        _sAlarm = GetNodeOrNull<AudioStreamPlayer3D>("Sounds/Alarm");
        _jets = GetNodeOrNull<Node3D>("Hips/JumpJets");
        _sJets = GetNodeOrNull<AudioStreamPlayer3D>("Hips/JumpJets/Sound");
        SetJetsBurning(false);

        bool player = Team == Team.Player;
        CollisionLayer = _bodyLayer = player ? Phys.LayerMech : Phys.LayerTargets;
        // Vehicles are solid to a mech only if it isn't allowed to walk over them.
        CollisionMask = _bodyMask = Phys.LayerWorld | Phys.LayerMech | Phys.LayerTargets | (GameConfig.MechsCrush ? 0 : Phys.LayerVehicles);

        SnapToTerrain();
        _spawn = GlobalTransform;
        _hipY = HipHeight;
        _exclude.Add(GetRid());

        Damage = new DamageModel(ArmorScale);
        Damage.Hit += OnHit;
        Damage.ArmorLost += OnArmorLost;
        Damage.SectionLost += OnSectionLost;
        Damage.Killed += OnKilled;
        Geo.Collect(this, _hitBoxes);
        foreach (var box in _hitBoxes)
        {
            box.Init(this, Damage, player ? Phys.LayerHitPlayer : Phys.LayerHitEnemy);
            // Its own shots pass through its own plates.
            _exclude.Add(box.GetRid());
        }

        foreach (var leg in Legs) leg.Landed += OnFootfall;
        Weapons = new WeaponSystem(this, GetParent(), _exclude);
        Weapons.Fired += OnFired;
        _grime = new Grime(this);

        foreach (Node child in GetChildren())
            if (child is MechPilot pilot) Pilot = pilot;
        Pilot?.Attach(this);
    }

    void SnapToTerrain()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        Vector3 p = GlobalPosition;
        GlobalPosition = new Vector3(p.X, terrain.HeightAt(p.X, p.Z) + 0.2f, p.Z);
    }

    // ── Setup: paint and weapons ────────────────────────────────

    /// <summary>Repaints the armour (see <see cref="PaintJob"/>).</summary>
    public void ApplyPaint(PaintJob paint) => ArmorMaterial = paint.Apply(this, ArmorMaterial);

    /// <summary>Shows a picture on the plates named "Insignia…" in the scene, or hides them.</summary>
    public void ApplyInsignia(Texture2D picture) => Units.ApplyInsignia(this, picture);

    /// <summary>Shows or hides this mech in the HUD's target window (a dedicated render layer).</summary>
    public void SetPreview(bool on, int layer) => Units.SetPreview(this, on, layer);

    // ── Main loop ───────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        // Far off, a computer-driven machine is simulated every other tick (half of them on the odd
        // ticks, half on the even), over the time that has passed. See Lod.SimInterval.
        _simTime += delta;
        int every = Lod.SimInterval(this, Pilot is AiPilot);
        if (every > 1 && (Engine.GetPhysicsFrames() + GetInstanceId()) % (ulong)every != 0) return;
        double step = _simTime;
        _simTime = 0.0;
        _simScale = (float)(step / delta);
        long prof = Prof.Now();
        Tick(step);
        Prof.Add(0, prof);
    }

    double _simTime;
    float _simScale = 1f;

    /// <summary>MoveAndSlide over the time this tick stands for (it moves by one physics step's worth of the velocity).</summary>
    void Slide()
    {
        if (Mathf.IsEqualApprox(_simScale, 1f))
        {
            MoveAndSlide();
            return;
        }
        Velocity *= _simScale;
        MoveAndSlide();
        Velocity /= _simScale;
    }

    void Tick(double delta)
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
        if (!_legsInitialized) InitLegs();
        // What is only for show (the body's sway, the legs' joints) is brought up to date less often far from the camera.
        _showDt += dt;
        bool show = ++_showTick >= Lod.Interval(this, 6f);

        var terrain = MapTerrain.Instance;
        WaterDepth = terrain?.WaterDepthAt(GlobalPosition.X, GlobalPosition.Z) ?? 0f;
        // Crossing by the bridge: over the water, not in it.
        if (WaterDepth > 0f && GlobalPosition.Y > terrain.WaterLevel) WaterDepth = 0f;

        if (_repairLeft > 0f)
        {
            _repairLeft -= dt;
            Heat = 0f;
            if (_repairLeft <= 0f) Mend();
        }
        UpdateHeat(dt);
        TurnInput = 0f;
        Fire = 0;
        Jump = Descend = false;
        long profT = Prof.Now();
        Pilot?.Think(dt);
        Prof.Add(8, profT);
        Throttle = Mathf.Clamp(Throttle, -ReverseFraction, 1f);

        // Legs: steering and speed.
        float speedNorm = Mathf.Clamp(Mathf.Abs(Speed) / MaxForwardSpeed, 0f, 1f);
        _yawRate = Offline ? 0f : Mathf.Clamp(TurnInput, -1f, 1f) * Mathf.Lerp(TurnRateStanding, TurnRateFull, speedNorm);
        if (_yawRate != 0f)
            Rotation = new Vector3(0, Rotation.Y + _yawRate * dt, 0);

        bool legged = Damage.IsDestroyed(Section.LeftLeg) || Damage.IsDestroyed(Section.RightLeg);
        // Wading slows a mech down.
        float wade = Mathf.Lerp(1f, 0.6f, Mathf.Clamp((WaterDepth - 0.8f) / 2.5f, 0f, 1f));
        float targetSpeed = Offline ? 0f : Throttle * MaxForwardSpeed * wade * (legged ? LeggedSpeedFactor : 1f);
        bool speedingUp = Mathf.Abs(targetSpeed) > Mathf.Abs(Speed) && targetSpeed * Speed >= 0f;
        float rate = speedingUp ? Acceleration : Deceleration;
        Speed = Mathf.MoveToward(Speed, targetSpeed, rate * dt);

        // Jump jets: burn while held and there's fuel, refill on the ground.
        Thrusting = Jump && HasJumpJets && !Offline && !legged && JumpFuel > 0f;
        if (Thrusting)
        {
            JumpFuel = Mathf.Max(0f, JumpFuel - dt / JumpFuelTime);
            Heat += JumpHeat * dt;
        }
        else if (IsOnFloor())
        {
            JumpFuel = Mathf.Min(1f, JumpFuel + dt / JumpRecharge);
        }
        SetJetsBurning(Thrusting);

        Vector3 forward = -GlobalTransform.Basis.Z;
        Vector3 v = forward * Speed;
        if (Thrusting) v.Y = Mathf.Min(Velocity.Y + (JumpThrust - Gravity) * dt, JumpClimbSpeed);
        // (On the ground the floor snap keeps it down: pressing into the ground as well would cost a collision every tick.)
        else v.Y = IsOnFloor() ? 0f : Velocity.Y - Gravity * dt;
        Velocity = v;
        // The floor snap would glue a mech that is trying to lift off.
        FloorSnapLength = Thrusting || Airborne ? 0f : 1.2f;
        long profM = Prof.Now();
        Slide();
        Prof.Add(6, profM);
        KeepOnMap();
        if (Boundary.Track(this, GlobalPosition, dt) && Net.Authority) SelfDestruct();

        bool wasAirborne = Airborne;
        // (Falling fast counts too, wherever it is: off a bridge, into a crater. Otherwise the feet stay
        // planted where they were while the body drops away under them.)
        Airborne = !IsOnFloor() && (Thrusting || wasAirborne || HeightAboveGround() > 2f || Velocity.Y < -3.5f);
        if (wasAirborne && !Airborne) OnTouchdown();

        // Anything flimsy in the way gets flattened; buildings get a mech-sized hole.
        if (++_tick % 6 == 0 && (Mathf.Abs(Speed) > 0.5f || wasAirborne))
        {
            Destructible.TrampleAround(GlobalPosition, TrampleRadius, forward * Mathf.Sign(Speed == 0f ? 1f : Speed));
            ChunkedBuilding.TrampleAround(GlobalPosition, TrampleRadius, HipHeight + 4f);
            // A wake where it wades.
            if (WaterDepth > 0.3f && Mathf.Abs(Speed) > 1f) WaterSurface.AddRipple(GlobalPosition, 0.8f);
        }

        Vector3 hv = Velocity;
        hv.Y = 0f;
        // Walked into a wall: let the speed follow reality instead of "running in place".
        if (IsOnWall() && hv.Length() < Mathf.Abs(Speed) * 0.8f)
            Speed = Mathf.Sign(Speed) * hv.Length();

        UpdateTorso(dt);
        if (Airborne)
        {
            // Legs tucked under the body, trailing a little.
            // Rising: knees up, feet tucked back. Falling: legs reaching down and forward for the ground.
            float rising = Mathf.Clamp(Velocity.Y / 8f, -1f, 1f) * 0.5f + 0.5f;
            foreach (var leg in Legs)
            {
                Vector3 home = leg.HomeLocal;
                var foot = new Vector3(home.X * 0.85f, HipHeight * Mathf.Lerp(0.12f, 0.3f, rising), home.Z + HipHeight * Mathf.Lerp(-0.06f, 0.22f, rising));
                leg.Hang(GlobalTransform, foot, dt);
            }
        }
        else
        {
            _gait.MaxSpeed = MaxForwardSpeed;
            _gait.StepLength = StepLength;
            _gait.StepHeight = StepHeight;
            _gait.RunSwing = RunSwing;
            _gait.HipWidth = Mathf.Abs(Legs[0].HomeLocal.X);
            long profG = Prof.Now();
            _gait.Update(dt, GlobalTransform, hv, forward * targetSpeed, rate, _yawRate, Legs, GroundAt);
            Prof.Add(4, profG);
        }
        if (show)
        {
            long profH = Prof.Now();
            UpdateHips(_showDt, hv);
            Prof.Add(4, profH);
            _lastHorizontalVel = hv;
            _showTick = 0;
            _showDt = 0f;
        }
        Pilot?.UpdateView(dt);
        long profA = Prof.Now();
        UpdateAim(space, dt);
        Prof.Add(7, profA);
        long profW = Prof.Now();
        UpdateWeapons(dt, space);
        Prof.Add(5, profW);
        UpdateSounds(dt, speedNorm);
        _grime.Update(dt, speedNorm);
        for (int i = 0; i < 2; i++) _armRecoil[i] = Mathf.MoveToward(_armRecoil[i], 0f, _armReturn[i] * dt);
    }

    int _showTick;
    float _showDt;

    void KeepOnMap()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        Vector3 p = terrain.Contain(GlobalPosition, out bool outside);
        if (outside) GlobalPosition = p;
    }

    /// <summary>Stayed beyond the battlefield too long: the reactor is let go.</summary>
    void SelfDestruct()
    {
        Boundary.Forget(this);
        // (Nobody's kill: whoever fired last somewhere else isn't to be credited with it.)
        Score.Attacker = null;
        foreach (var box in _hitBoxes)
            if (box.Section == Section.CenterTorso)
            {
                box.TakeDamage(1e6f, box.Center);
                return;
            }
    }

    void InitLegs()
    {
        foreach (var leg in Legs)
        {
            Vector3 home = GlobalTransform * leg.HomeLocal;
            if (GroundAt(home, out var p, out var n)) leg.Teleport(p, n);
            else leg.Teleport(home, Vector3.Up);
        }
        _legsInitialized = true;
    }

    float HeightAboveGround()
    {
        var terrain = MapTerrain.Instance;
        return terrain == null ? 0f : GlobalPosition.Y - terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z);
    }

    void OnTouchdown()
    {
        // Feet straight back onto the ground, with the thump of the whole mech coming down.
        _legsInitialized = false;
        _bobVel -= 5f;
        Shake += 0.5f;
        if (_sFoot != null)
        {
            _sFoot.GlobalPosition = GlobalPosition;
            _sFoot.PitchScale = 0.7f;
            _sFoot.Play();
        }
    }

    // -1 = not known yet: the first call always goes through.
    int _jetsShown = -1;

    void SetJetsBurning(bool on)
    {
        if (_jets == null || _jetsShown == (on ? 1 : 0)) return;
        _jetsShown = on ? 1 : 0;
        foreach (Node child in _jets.GetChildren())
        {
            if (child is GpuParticles3D p) p.Emitting = on;
            else if (child is Light3D light) light.Visible = on;
        }
        if (_sJets != null && _sJets.Playing != on)
        {
            if (on) _sJets.Play();
            else _sJets.Stop();
        }
    }

    Grime _grime;
    Vector3[] _armRest;
    Basis[] _armRestBasis;
    readonly float[] _armRecoil = new float[2], _armReturn = new float[2];

    /// <summary>A gun has fired: its arm is driven back, and the body rocks back from the kick, more to that side.</summary>
    void OnFired(Weapon w)
    {
        int arm = w.Mount == Section.LeftArm ? 0 : w.Mount == Section.RightArm ? 1 : -1;
        if (arm >= 0 && w.RecoilTravel > 0f)
        {
            float travel = w.RecoilTravel * ChaseScale;
            _armRecoil[arm] = Mathf.Max(_armRecoil[arm], travel);
            _armReturn[arm] = travel / Mathf.Max(w.RecoilReturn, 0.05f);
        }
        if (w.Kick <= 0f) return;
        float k = w.Kick * RecoilGive / Mathf.Max(ChaseScale, 0.5f);
        _nodVel += k * 0.9f;
        _bobVel -= k * 0.6f;
        _listVel += (arm == 0 ? 1f : arm == 1 ? -1f : 0f) * k * 0.35f;
        Shake += k * 0.25f;
    }

    void UpdateHeat(float dt)
    {
        // Standing in water cools the mech faster.
        float dissipation = HeatDissipation * (IsShutdown ? 1.6f : 1f) * (1f + WaterCooling * Mathf.Clamp(WaterDepth / Mathf.Max(WaterCoolingDepth, 0.1f), 0f, 1f));
        Heat = Mathf.Max(0f, Heat - dissipation * dt);

        if (!IsShutdown && Heat >= 1f)
        {
            IsShutdown = true;
            RestartProgress = 0f;
            _restartTimer = 0f;
            Weapons.CancelBeams();
            Shake += 0.3f;
        }
        else if (IsShutdown && Heat <= RestartHeat)
        {
            _restartTimer += dt;
            RestartProgress = Mathf.Clamp(_restartTimer / RestartTime, 0f, 1f);
            if (_restartTimer >= RestartTime)
            {
                IsShutdown = false;
                RestartProgress = 0f;
            }
        }
    }

    /// <summary>Heat put into the machine from outside (a flamethrower playing on it).</summary>
    public void AddHeat(float amount)
    {
        if (IsDestroyed || amount <= 0f) return;
        // (A reactor that has already shut down can't be cooked much further.)
        Heat = Mathf.Min(Heat + amount, 1.25f);
    }

    void UpdateTorso(float dt)
    {
        if (!Offline)
        {
            _yaw = Mathf.MoveToward(_yaw, Mathf.Clamp(TargetYaw, -MaxTwist, MaxTwist), TwistRate * dt);
            _pitch = Mathf.MoveToward(_pitch, Mathf.Clamp(TargetPitch, MinPitch, MaxPitch), PitchRate * dt);
        }
        else
        {
            // Powerless torso slumps a little.
            _pitch = Mathf.MoveToward(_pitch, MinPitch * 0.6f, 0.3f * dt);
            TargetYaw = _yaw;
            TargetPitch = _pitch;
        }
        // A blow with a hand weapon is thrown with the whole body: the torso winds back from the striking
        // side and dips, then whips round into the blow.
        _swingArm = -1;
        float swingYaw = 0f, swingPitch = 0f;
        foreach (var w in Weapons.Weapons)
        {
            if (w.Kind != WeaponKind.Melee || w.SwingLeft <= 0f) continue;
            float k = w.SwingPhase;
            _swingArm = w.Mount == Section.LeftArm ? 0 : 1;
            float side = _swingArm == 0 ? 1f : -1f;
            // -1 at the top of the wind-up, +1 as the blow lands, back to 0.
            _swing = k < Weapon.WindUp ? -Mathf.SmoothStep(0f, 1f, k / Weapon.WindUp)
                   : k < Weapon.Strike ? Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, (k - Weapon.WindUp) / (Weapon.Strike - Weapon.WindUp)))
                   : 1f - Mathf.SmoothStep(0f, 1f, (k - Weapon.Strike) / (1f - Weapon.Strike));
            swingYaw = -_swing * side * SwingTwist;
            swingPitch = -Mathf.Max(_swing, 0f) * SwingLean + Mathf.Max(-_swing, 0f) * SwingLean * 0.4f;
            break;
        }
        // The pelvis swings with the stride (see UpdateHips); the torso cancels it to stay on aim.
        Torso.Rotation = new Vector3(_pitch + swingPitch, _yaw - _twist + swingYaw, 0f);
    }

    void OnFootfall(MechLeg leg)
    {
        float speedNorm = Mathf.Clamp(Mathf.Abs(Speed) / MaxForwardSpeed, 0f, 1f);
        // The whole machine comes down on that foot: the body drops and nods forward, then heaves back up.
        _bobVel -= 1.6f + 2.6f * speedNorm;
        _nodVel -= 0.12f + 0.22f * speedNorm;
        // …and settles onto the hip of that side, which gives under the load and pushes back.
        _listVel -= leg.Side * (0.1f + 0.2f * speedNorm) * HipGive;
        Shake += 0.06f + 0.14f * speedNorm;

        // A foot coming down on a tank is the end of the tank.
        if (GameConfig.MechsCrush)
        {
            // The foot is about this big; it only has to clip the hull.
            float footW = TrampleRadius * 0.3f, footL = TrampleRadius * 0.5f;
            foreach (var unit in Units.All)
            {
                if (unit is not Vehicle { IsAircraft: false } vehicle || vehicle.Kind == Vehicle.MoveKind.Ship || (vehicle.IsDestroyed && !vehicle.Sunk)) continue;
                Vector3 d = vehicle.GlobalTransform.AffineInverse() * leg.Contact;
                if (Mathf.Abs(d.Y) < 3.5f && Mathf.Abs(d.X) < vehicle.HalfExtents.X + footW && Mathf.Abs(d.Z) < vehicle.HalfExtents.Y + footL)
                {
                    // (One that drowned goes up now, under the foot.)
                    if (vehicle.Sunk)
                    {
                        vehicle.BlowSunk(leg.Contact);
                        Shake += 0.45f;
                        continue;
                    }
                    vehicle.TakeDamage(vehicle.MaxHp * 2f, leg.Contact);
                    // It goes up under the foot: the leg takes some of it, and the whole machine is shaken.
                    Shake += 0.45f;
                    _bobVel += 1.5f;
                    if (Net.Authority && CrushBackfire > 0f)
                    {
                        Section foot = leg.Side < 0f ? Section.LeftLeg : Section.RightLeg;
                        foreach (var box in _hitBoxes)
                            if (box.Section == foot)
                            {
                                box.TakeDamage(CrushBackfire, leg.Contact);
                                break;
                            }
                    }
                }
            }
        }

        // Prints and thuds only matter near the viewer.
        var cam = GetViewport().GetCamera3D();
        if (cam == null || cam.GlobalPosition.DistanceSquaredTo(leg.Contact) > 250f * 250f) return;

        if (_sFoot != null)
        {
            _sFoot.GlobalPosition = leg.Contact;
            _sFoot.PitchScale = (float)GD.RandRange(0.85, 1.1);
            _sFoot.Play();
        }
        var terrain = MapTerrain.Instance;
        bool wet = terrain != null && leg.Contact.Y < terrain.WaterLevel && terrain.WaterDepthAt(leg.Contact.X, leg.Contact.Z) > 0.3f;
        var scene = wet ? terrain.SplashScene : terrain?.FootprintScene;
        if (scene == null) return;
        if (wet)
        {
            var splash = scene.Instantiate<Node3D>();
            GetParent().AddChild(splash);
            splash.GlobalPosition = new Vector3(leg.Contact.X, terrain.WaterLevel, leg.Contact.Z);
            WaterSurface.AddRipple(leg.Contact, 1.2f);
            return;
        }
        var print = scene.Instantiate<Node3D>();
        // The map decides what a print looks like in its ground (packed sand, snow, asphalt); the mech, its shape.
        if (print is Footprint footprint) footprint.Shape(FootprintTexture, FootprintSize, FootprintCentre);
        GetParent().AddChild(print);
        Basis basis = Geo.BasisFromUpForward(leg.Normal, -GlobalTransform.Basis.Z);
        print.GlobalTransform = new Transform3D(basis, leg.Contact);
    }

    void UpdateHips(float dt, Vector3 hv)
    {
        // Height follows the average ground under the feet (slopes, rocks).
        float avgFoot = (Legs[0].Contact.Y + Legs[1].Contact.Y) * 0.5f;
        float targetY = HipHeight + Mathf.Clamp(avgFoot - GlobalPosition.Y, -1.2f, 0.6f) * 0.7f;
        _hipY = Mathf.Lerp(_hipY, targetY, 1f - Mathf.Exp(-8f * dt));

        // Damped spring for the footfall dip: the body sags under its own weight, then recovers.
        _bobVel += (-_bob * 42f - _bobVel * 8f) * dt;
        _bob += _bobVel * dt;
        _nodVel += (-_nod * 55f - _nodVel * 9f) * dt;
        _nod += _nodVel * dt;
        _listVel += (-_list * 60f - _listVel * 8f) * dt;
        _list += _listVel * dt;

        float lift = 0f, roll = 0f;
        foreach (var leg in Legs)
        {
            lift = Mathf.Max(lift, leg.SwingPhase);
            roll += leg.Side * leg.SwingPhase; // lean away from the swinging leg, onto the planted one
        }

        // Lean back on acceleration, forward on braking.
        float accel = (hv.Length() - _lastHorizontalVel.Length()) / Mathf.Max(dt, 1e-4f);
        // (Positive pitch is nose-up.) Braking is negative acceleration: the nose dips.
        _lean = Mathf.Lerp(_lean, Mathf.Clamp(accel * 0.008f, -0.07f, 0.06f) * Mathf.Sign(Speed == 0f ? 1f : Speed), 1f - Mathf.Exp(-4f * dt));
        _roll = Mathf.Lerp(_roll, roll * 0.035f, 1f - Mathf.Exp(-10f * dt));

        // The hip of the leading leg swings forward: twist the pelvis by how far apart the feet are fore and aft.
        Transform3D toLocal = GlobalTransform.AffineInverse();
        float spread = (toLocal * Legs[0].Contact).Z - (toLocal * Legs[1].Contact).Z;
        float twist = Airborne ? 0f : Mathf.Clamp(spread / (2f * Mathf.Max(_gait.CurrentStep, 1f)), -1f, 1f) * HipSwing;
        _twist = Mathf.Lerp(_twist, twist, 1f - Mathf.Exp(-9f * dt));

        float speedNorm = Mathf.Clamp(Speed / MaxForwardSpeed, -1f, 1f);
        float slump = IsShutdown ? -0.5f : 0f;
        // Longer strides sit lower; the body shifts over the standing leg and leans into the walk.
        float stride = -0.45f * Mathf.Abs(speedNorm);
        // "lift" peaks mid-step: the body vaults up over the standing leg and falls into the next footfall.
        Hips.Position = new Vector3(-roll * BodySway, _hipY + _bob + lift * 0.32f * (HipHeight / 5.6f) + slump + stride, 0);
        Hips.Rotation = new Vector3(_lean - 0.04f * speedNorm + _nod, _twist, _roll * 2f + _list);

        var hips = Hips.GlobalTransform;
        var basis = GlobalTransform.Basis;
        foreach (var leg in Legs) leg.Solve(hips, basis, hv, dt);
    }

    void UpdateSounds(float dt, float speedNorm)
    {
        _hitSoundCooldown -= dt;
        if (_sReactor != null)
        {
            if (Offline || Pilot == null)
            {
                if (_sReactor.Playing) _sReactor.Stop();
            }
            else
            {
                if (!_sReactor.Playing) _sReactor.Play();
                _sReactor.PitchScale = 0.85f + 0.5f * speedNorm;
            }
        }
        // Overheat warning, for the player's own cockpit only.
        if (_sAlarm != null && Team == Team.Player && Heat > 0.85f && !IsShutdown && !_sAlarm.Playing)
            _sAlarm.Play();
    }

    // ── Aim & weapons ───────────────────────────────────────────

    float _aimDt;

    void UpdateAim(PhysicsDirectSpaceState3D space, float dt)
    {
        _aimDt += dt;
        // The long sight-line ray is the costliest thing a mech does; the AI can do with every other tick.
        // (Unless it is only simulated every other tick anyway, see Lod.SimInterval: then it aims as often as ever.)
        if (Pilot is not PlayerPilot && _simScale < 1.5f && (_tick & 1) == 1) return;
        float step = _aimDt;
        _aimDt = 0f;
        Transform3D view = default;
        if (Pilot == null || !Pilot.TryGetAimView(out view))
            view = AimAnchor.GlobalTransform.Orthonormalized();
        Vector3 origin = view.Origin;
        Vector3 dir = -view.Basis.Z;
        // Start the ray level with the torso so a chase camera can't aim at things behind the mech.
        float start = Mathf.Max(0f, (Torso.GlobalPosition - origin).Dot(dir));
        Vector3 from = origin + dir * start;
        Vector3 to = origin + dir * (Pilot is PlayerPilot ? 1500f : 700f);

        if (Phys.Ray(space, from, to, FireMask, _exclude, out var hit))
        {
            AimPoint = hit.Position;
            AimCollider = hit.Collider;
        }
        else
        {
            AimPoint = to;
            AimCollider = null;
        }

        // The arms aim where the pilot wants, as far as they reach from where the torso is pointing
        // (and the torso is still turning to catch up). Their own ray finds what they are on.
        float wantYaw = Mathf.Clamp(Mathf.AngleDifference(_yaw, Mathf.Clamp(TargetYaw, -MaxTwist, MaxTwist)) + ArmLookYaw, -ArmYawReach, ArmYawReach);
        float wantPitch = Mathf.Clamp(Mathf.Clamp(TargetPitch, MinPitch - ArmPitchReach, MaxPitch + ArmPitchReach) - _pitch + ArmLookPitch, -ArmPitchReach, ArmPitchReach);
        if (!ArmsAim) wantYaw = wantPitch = 0f;
        _armYaw = Mathf.MoveToward(_armYaw, wantYaw, ArmTrackRate * step);
        _armPitch = Mathf.MoveToward(_armPitch, wantPitch, ArmTrackRate * step);
        if (Mathf.Abs(_armYaw) < 1e-3f && Mathf.Abs(_armPitch) < 1e-3f) ArmAimPoint = AimPoint;
        else
        {
            Vector3 right = dir.Cross(Vector3.Up);
            if (right.LengthSquared() < 1e-6f) right = view.Basis.X;
            Vector3 armDir = dir.Rotated(right.Normalized(), _armPitch).Rotated(Vector3.Up, _armYaw);
            Vector3 armTo = from + armDir * (Pilot is PlayerPilot ? 1500f : 700f);
            ArmAimPoint = Phys.Ray(space, from, armTo, FireMask, _exclude, out var armHit) ? armHit.Position : armTo;
        }
        Weapons.ArmAim = ArmsAim ? ArmAimPoint : null;

        // Arms converge on where they aim (the ones still attached).
        for (int i = 0; i < _arms.Length; i++)
        {
            var arm = _arms[i];
            if (_tornArms[i] != null) continue;
            if (!ArmsAim) arm.Basis = _armRestBasis[i];
            else if (arm.GlobalPosition.DistanceSquaredTo(ArmAimPoint) > 4f)
                arm.LookAt(ArmAimPoint, Vector3.Up);
            // Driven back along itself by the last shot, and running out again.
            arm.Position = _armRest[i] + arm.Basis.Z * _armRecoil[i];
            // The striking arm goes up and back, then comes down through the target.
            if (i == _swingArm)
                arm.RotateObjectLocal(Vector3.Right, _swing < 0f ? -_swing * SwingRaise : -_swing * SwingFollow);
        }
    }

    void UpdateWeapons(float dt, PhysicsDirectSpaceState3D space)
    {
        Weapons.DamageScale = DamageDealtScale;
        Weapons.Mask = FireMask;
        if (!Offline && (Pilot?.CanFire ?? false))
        {
            Heat += Weapons.FireGroups(Fire, AimPoint, space);
        }
        Weapons.Update(dt, AimPoint, space, LockTarget);
    }

    // ── Damage ──────────────────────────────────────────────────

    void OnHit(Section s)
    {
        Shake += 0.1f;
        // Now and then a hit knocks a fragment loose.
        if (GD.Randf() < 0.2f) Shrapnel(s, 1, 0.35f);
        if (_sHit != null && _hitSoundCooldown <= 0f)
        {
            _hitSoundCooldown = 0.12f;
            _sHit.PitchScale = (float)GD.RandRange(0.8, 1.2);
            _sHit.Play();
        }
        Pilot?.OnHit();
    }

    /// <summary>Throws pieces of a section's plating off it.</summary>
    void Shrapnel(Section s, int count, float size)
    {
        foreach (var box in _hitBoxes)
        {
            if (box.Section != s) continue;
            Vector3 away = (box.Center - TorsoCenter).Normalized() * 4f + Velocity;
            Debris.Burst(GetParent(), box.Center, count, size * (HipHeight / 5.6f), box.PlateMaterial ?? ArmorMaterial, away, 5f);
            return;
        }
    }

    /// <summary>The armour over a section is gone: the plates come off and the structure shows.</summary>
    void OnArmorLost(Section s)
    {
        Shrapnel(s, 5, 0.9f);
        foreach (var box in _hitBoxes)
            if (box.Section == s) box.SetCharred(StrippedMaterial ?? CharredMaterial);
    }

    void OnSectionLost(Section s)
    {
        Shake += 0.3f;
        Weapons.SetSectionDestroyed(s, true);
        Shrapnel(s, 4, 0.8f);

        // An arm that's destroyed comes off and falls.
        int armIndex = s == Section.LeftArm ? 0 : s == Section.RightArm ? 1 : -1;
        if (armIndex >= 0 && _tornArms[armIndex] == null)
        {
            foreach (var box in _hitBoxes)
                if (box.Section == s) box.SetEnabled(false);
            var toss = _arms[armIndex].GlobalTransform.Basis.X.Normalized() * (armIndex == 0 ? -3f : 3f) + Vector3.Up * 2f + Velocity;
            _tornArms[armIndex] = Debris.Detach(_arms[armIndex], GetParent(), toss, 2500f);
            // (Lying on the ground it is part of the map again.)
            SetInFront(_arms[armIndex], false);
        }

        bool smoking = false;
        foreach (var box in _hitBoxes)
        {
            if (box.Section != s) continue;
            box.SetCharred(CharredMaterial);
            if (smoking) continue;
            smoking = true;
            TimedFx.Flash(GetParent(), box.Center, new Color(1f, 0.55f, 0.1f), 2.5f, 0.35f);
            if (DamageSmokeScene == null) continue;
            // The smoke rides on the blackened part, wherever it ends up.
            var smoke = DamageSmokeScene.Instantiate<Node3D>();
            box.AddChild(smoke);
            smoke.GlobalPosition = box.Center;
            _damageFx.Add(smoke);
        }
    }

    void OnKilled()
    {
        RespawnLeft = RespawnTime;
        _explodeTimer = ExplosionDelay;
        _exploded = false;
        Throttle = 0f;
        Speed = 0f;
        Weapons.CancelBeams();
        _sReactor?.Stop();
        SetJetsBurning(false);
        Airborne = false;
        foreach (var box in _hitBoxes) box.SetEnabled(false);

        // The wreck takes over: the solid body goes away and the parts fall as a ragdoll.
        CollisionLayer = 0;
        CollisionMask = 0;
        _ragdoll?.Activate(this, Velocity);
        Pilot?.OnKilled();
        Score.Killed(this);
    }

    void UpdateDead(float dt)
    {
        if (!_exploded)
        {
            _explodeTimer -= dt;
            if (_explodeTimer <= 0f) Explode();
        }
        if (!Respawns) return;
        RespawnLeft -= dt;
        if (RespawnLeft <= 0f) Respawn();
    }

    void Explode()
    {
        _exploded = true;
        Vector3 center = TorsoCenter;
        if (ExplosionScene != null)
        {
            OneShotFx.Spawn(ExplosionScene, GetParent(), center);
        }
        _ragdoll?.Blast(center - Vector3.Up * 2f);
        // The reactor going up tears plating off in all directions and digs a hole under the mech.
        Debris.Burst(GetParent(), center, 10, 1.1f * (HipHeight / 5.6f), ArmorMaterial, Vector3.Up * 6f, 12f);
        MapTerrain.Instance?.AddCrater(GlobalPosition, 6f * (HipHeight / 5.6f), 1.4f);
        if (WreckFireScene != null)
        {
            var fire = WreckFireScene.Instantiate<Node3D>();
            Torso.AddChild(fire);
            fire.GlobalPosition = center;
            _damageFx.Add(fire);
        }
        Shake += 0.6f;
    }

    bool _drawOver;

    /// <summary>
    /// The torso — cab, arms, guns — drawn over everything else on the map, or not: on for the player's
    /// own machine seen from the cockpit, so that nothing of it is seen cut by a wall or a hill it pokes
    /// into (parts painted with materials/armor.gdshader or that have its in_front switch).
    /// </summary>
    public void DrawOver(bool on)
    {
        _drawOver = on;
        if (GetNodeOrNull("Hips/Torso") is { } torso) SetInFront(torso, on);
        for (int i = 0; i < _tornArms.Length; i++)
            if (_tornArms[i] != null) SetInFront(_arms[i], false);
    }

    static void SetInFront(Node root, bool on)
    {
        foreach (var part in Geo.Collect<GeometryInstance3D>(root))
            if (part is MeshInstance3D) part.SetInstanceShaderParameter("in_front", on ? 1f : 0f);
    }

    /// <summary>Puts back everything that was shot off, mends every section and refills the magazines.</summary>
    void Mend()
    {
        Impacts.CleanUnit(this);
        for (int i = 0; i < _tornArms.Length; i++)
        {
            _tornArms[i]?.Restore();
            _tornArms[i] = null;
        }
        DrawOver(_drawOver);
        foreach (Node fx in _damageFx)
            if (IsInstanceValid(fx)) fx.QueueFree();
        _damageFx.Clear();

        Damage.Reset();
        _grime.Clean();
        for (int i = 0; i < DamageModel.Count; i++) Weapons.SetSectionDestroyed((Section)i, false);
        Weapons.Rearm();
        Ams.Of(this)?.Rearm();
        foreach (var box in _hitBoxes) box.Restore();
        CollisionLayer = _bodyLayer;
        CollisionMask = _bodyMask;

    }

    /// <summary>Being worked on at a repair bay: powered down until it is done.</summary>
    public bool Repairing => _repairLeft > 0f;
    /// <summary>Seconds of repair still to go.</summary>
    public float RepairLeft => _repairLeft;
    float _repairLeft;
    /// <summary>Anything to mend or reload.</summary>
    public bool NeedsRepair => !IsDestroyed && (Damage.TotalFraction < 0.995f || Weapons.NeedsAmmo);

    /// <summary>Starts a repair: the machine is shut down for this long, then comes back whole and rearmed.</summary>
    public void BeginRepair(float seconds)
    {
        if (IsDestroyed || Repairing) return;
        _repairLeft = seconds;
        Throttle = 0f;
        Weapons.CancelBeams();
    }

    // ── Network ─────────────────────────────────────────────────

    /// <summary>Where the host says this machine is. Others' machines follow it closely; the player's own is only pulled back when it has strayed.</summary>
    public void NetCorrect(Vector3 position, float yaw, float speed, bool own)
    {
        if (IsDestroyed) return;
        float off = GlobalPosition.DistanceTo(position);
        if (off > (own ? 12f : 25f)) GlobalPosition = position;
        else if (off > (own ? 2.5f : 0.3f)) GlobalPosition = GlobalPosition.Lerp(position, own ? 0.08f : 0.25f);
        if (!own)
        {
            Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(Rotation.Y, yaw, 0.35f), Rotation.Z);
            Speed = Mathf.Lerp(Speed, speed, 0.5f);
        }
    }

    /// <summary>The host has put this machine back on the field.</summary>
    public void NetRespawn(Transform3D at)
    {
        _spawn = at;
        Respawn();
    }

    void Respawn()
    {
        _ragdoll?.Deactivate();
        _repairLeft = 0f;
        Mend();
        GlobalTransform = _spawn;
        Velocity = Vector3.Zero;
        Speed = 0f;
        Throttle = 0f;
        Heat = 0f;
        IsShutdown = false;
        RestartProgress = 0f;
        _yaw = _pitch = TargetYaw = TargetPitch = 0f;
        _hipY = HipHeight;
        _bob = _bobVel = _twist = _nod = _nodVel = _list = _listVel = 0f;
        JumpFuel = 1f;
        Hips.Position = new Vector3(0, HipHeight, 0);
        Hips.Rotation = Vector3.Zero;
        Torso.Rotation = Vector3.Zero;
        _legsInitialized = false;
        Pilot?.OnRespawn();
        NetLink.I?.HostRespawned(this);
    }

    // ── Helpers ─────────────────────────────────────────────────

    bool GroundAt(Vector3 point, out Vector3 position, out Vector3 normal)
    {
        var space = GetWorld3D().DirectSpaceState;
        float y = GlobalPosition.Y;
        Vector3 from = new Vector3(point.X, y + 6f, point.Z);
        Vector3 to = new Vector3(point.X, y - 8f, point.Z);
        if (Phys.Ray(space, from, to, Phys.MaskFeet, _exclude, out var hit))
        {
            position = hit.Position;
            normal = hit.Normal;
            return true;
        }
        position = point;
        normal = Vector3.Up;
        return false;
    }
}
