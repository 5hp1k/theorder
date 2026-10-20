using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The human at the controls of a mech or a vehicle: keyboard/mouse commands, first-person and chase
/// camera with free look and zoom, target lock. Lives in player_pilot.tscn (with its Camera3D child)
/// and is added to whatever the player picked. Also the seat the HUD reads from.
/// </summary>
public partial class PlayerPilot : MechPilot, ICockpit
{
    /// <summary>Render layer seen only by the HUD's target camera.</summary>
    public const int PreviewLayer = 10;

    [Export] public float MouseSensitivity = 0.0025f;
    [Export] public float MaxLookPitch = 0.7f;       // free look up/down, rad
    [Export] public float ZoomFov = 22f;
    /// <summary>Chase camera offset from the pivot, in the camera's own space (scaled to the machine).</summary>
    [Export] public Vector3 ChaseOffset = new(0f, 2.5f, 16f);
    /// <summary>How much of a mech's rocking and bouncing the first-person view cancels out (0 = none, 1 = all).</summary>
    [Export(PropertyHint.Range, "0,1")] public float HeadSteadiness = 0.8f;
    /// <summary>How much of the jolts from footfalls and hits reaches the first-person view.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CockpitShake = 0.2f;
    /// <summary>How far the pilot's head may move from the seat, metres: keeps the eye inside the cab.</summary>
    [Export] public float HeadTravel = 0.12f;
    [ExportGroup("Aircraft")]
    /// <summary>How fast W/S lower and raise the nose, rad/s (it comes back level at the same rate when they are let go).</summary>
    [Export] public float StickPitchRate = 0.7f;
    /// <summary>W raises the nose instead of lowering it.</summary>
    [Export] public bool WClimbs;
    /// <summary>How far off its heading the mouse can ask the aircraft to turn at once, radians.</summary>
    [Export] public float MaxLead = 1.2f;
    /// <summary>How hard it banks for a given turn asked of it: full bank at 1/LeadGain radians off.</summary>
    [Export] public float LeadGain = 3f;
    [ExportGroup("")]
    /// <summary>How far ahead of the eye the cab ends (for a machine of the Ostrich's size): shots are drawn from there on.</summary>
    [Export] public float CabDepth = 2.4f;
    /// <summary>Seconds the sight must stay on the selected target for a missile lock.</summary>
    [Export] public float LockTime = 1.3f;
    /// <summary>How close to the target the sight must be kept, radians.</summary>
    [Export] public float LockCone = 0.07f;
    [Export] public float LockRange = 800f;

    public bool CockpitView { get; private set; } = true;
    /// <summary>
    /// Counter-rotation (Caps Lock): while the legs turn, the torso turns back the same amount, so the
    /// guns stay on the bearing the player is looking along — as far as the torso's twist allows.
    /// </summary>
    public bool CounterRotate { get; private set; }
    /// <summary>Locked target shown in the HUD's target window; guided missiles home on it.</summary>
    public ITarget Target { get; private set; }
    public Camera3D Camera { get; private set; }
    /// <summary>True while the camera is turned away from where the weapons point.</summary>
    public bool FreeLook => Mathf.Abs(_lookYaw) > 1e-3f || Mathf.Abs(_lookPitch) > 1e-3f;
    public bool Zoomed { get; private set; }
    /// <summary>Current magnification, 1 = normal view.</summary>
    public float Magnification => BaseFov / Camera.Fov;

    public override bool CanFire => !_suppressFire && Input.MouseMode == Input.MouseModeEnum.Captured;

    // ── The seat, as the HUD sees it ────────────────────────────
    public string UnitName => Unit.DisplayName;
    public Vector3 WorldPosition => Unit.GlobalPosition;
    public float HullYaw => Unit.HullYaw;
    public float Throttle => Unit.Throttle;
    public float ReverseFraction => Unit.ReverseFraction;
    public float SpeedFraction => Unit.Speed / Unit.MaxForwardSpeed;
    public float SpeedKmh => Unit.Speed * 3.6f;
    public bool HasHeat => Unit.HasHeat;
    public float Heat => Unit.Heat;
    public bool IsShutdown => Unit.IsShutdown;
    public float RestartHeat => Unit.RestartHeat;
    public float RestartProgress => Unit.RestartProgress;
    public float Twist => Unit.TorsoYaw;
    public float MaxTwist => Unit.MaxTwist;
    public float Pitch => Unit.TorsoPitch;
    public float MinPitch => Unit.MinPitch;
    public float MaxPitch => Unit.MaxPitch;
    public float HeadingDeg => Unit.HeadingDeg;
    public WeaponSystem Weapons => Unit.Weapons;
    public DamageModel Damage => Unit.Damage;
    public float Integrity => Unit.Integrity;
    public bool HasJump => Unit.HasJumpJets;
    public float JumpFuel => Unit.JumpFuel;
    public bool Thrusting => Unit.Thrusting;
    public bool IsDestroyed => Unit.IsDestroyed;
    public bool Offline => Unit.Offline;
    public float RespawnLeft => Unit.RespawnLeft;
    public bool FirstPerson => CockpitView;
    public Vector3 AimPoint => Unit.AimPoint;
    public GodotObject AimCollider => Unit.AimCollider;

    public Vector3? LeadAim
    {
        get
        {
            if (Unit is Mech mech) return mech.ArmAimPoint.DistanceSquaredTo(mech.AimPoint) > 0.25f ? mech.ArmAimPoint : null;
            // A turret: where it is turning to, along the sight line it will have when it gets there.
            float yaw = Mathf.AngleDifference(Unit.TorsoYaw, Unit.TargetYaw), pitch = Unit.TargetPitch - Unit.TorsoPitch;
            if (IsPlane || (Mathf.Abs(yaw) < 0.01f && Mathf.Abs(pitch) < 0.01f)) return null;
            Vector3 dir = -_aimView.Basis.Z;
            Vector3 right = dir.Cross(Vector3.Up);
            if (right.LengthSquared() < 1e-6f) return null;
            return _aimView.Origin + dir.Rotated(right.Normalized(), pitch).Rotated(Vector3.Up, yaw) * 300f;
        }
    }
    public bool CanLock => true;
    /// <summary>Whether an electromagnetic pulse has knocked the machine's electronics out.</summary>
    public bool Jammed => Emp.Jammed(Unit);
    public float LockProgress { get; private set; }
    public string Prompt => Spectating ? $"НАБЛЮДЕНИЕ: {_watched?.DisplayName ?? "—"}   ·   огонь — следующий" : Unit switch
    {
        Mech { Repairing: true } m => $"РЕМОНТ И ПЕРЕЗАРЯДКА… {Mathf.CeilToInt(m.RepairLeft)} с",
        Vehicle { Repairing: true } v => $"РЕМОНТ И ПЕРЕЗАРЯДКА… {Mathf.CeilToInt(v.RepairLeft)} с",
        _ => null,
    };

    float BaseFov => GameConfig.Fov;

    Node3D _cockpit;
    AudioStreamPlayer _lockSound, _acquireSound;
    Transform3D _aimView = Transform3D.Identity;
    float _lookYaw, _lookPitch;
    // Aircraft: how far left of the nose the mouse has asked it to turn; whether W/S hold the nose off level.
    float _lead, _lastYaw;
    bool _keyPitch;

    bool IsPlane => Unit is Vehicle { Kind: Vehicle.MoveKind.Plane };
    /// <summary>The key held to look around. An aircraft has its throttle on Shift/Ctrl, so there it is the jump key.</summary>
    string LookAction => IsPlane ? "jump" : "free_look";
    float _throttleDetent;   // blocks throttle from crossing zero until W/S is released
    bool _suppressFire, _eyeSet;
    float _eyeHeight;
    // The hull's heading at the last tick, for counter-rotation.
    float _lastHull;
    bool _hullSet;
    // Groups 4–6 sit on the number keys, which also give lance orders: a key pressed for an order fires nothing until let go.
    bool _holdNumberGroups;

    AudioStreamPlayer _friendlyFire;
    float _lastWarning;

    /// <summary>A shot of ours has hit one of our own: the warning tone (not more than twice a second).</summary>
    void OnFriendlyFire()
    {
        if (_friendlyFire == null || Unit == null || Unit.Time - _lastWarning < 0.5f) return;
        _lastWarning = Unit.Time;
        _friendlyFire.Play();
    }

    bool _drawnOver;

    public override void _ExitTree()
    {
        if (_drawnOver && Unit is Mech own && IsInstanceValid(own)) own.DrawOver(false);
        Score.FriendlyFire -= OnFriendlyFire;
        if (Game.Cockpit == this) Game.Cockpit = null;
    }

    public override void Attach(IDrivable unit)
    {
        base.Attach(unit);
        Game.Cockpit = this;
        Game.PlayerUnit = unit;
        Camera = GetNode<Camera3D>("Camera");
        Camera.Fov = BaseFov;
        Camera.MakeCurrent();
        _lockSound = GetNodeOrNull<AudioStreamPlayer>("LockSound");
        _acquireSound = GetNodeOrNull<AudioStreamPlayer>("AcquireSound");
        _friendlyFire = GetNodeOrNull<AudioStreamPlayer>("FriendlyFireSound");
        Score.FriendlyFire += OnFriendlyFire;
        _cockpit = unit.CockpitInterior;
        if (_cockpit != null) _cockpit.Visible = CockpitView;
        _aimView = unit.AimAnchor.GlobalTransform.Orthonormalized();
    }

    public override bool TryGetAimView(out Transform3D view)
    {
        view = _aimView;
        return true;
    }

    // ── Input ───────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Unit == null || StoryOverlay.InCutscene || FreeCamera.Current != null) return;
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Finer control when zoomed in.
            float sens = MouseSensitivity * GameConfig.MouseSensitivity * Camera.Fov / BaseFov;
            if (Unit is Mech { ArmsAim: true } armed && Input.IsActionPressed("arm_look"))
            {
                // The arms alone: the torso stays where it is.
                armed.ArmLookYaw = Mathf.Clamp(armed.ArmLookYaw - motion.Relative.X * sens, -armed.ArmYawReach * 2f, armed.ArmYawReach * 2f);
                armed.ArmLookPitch = Mathf.Clamp(armed.ArmLookPitch - motion.Relative.Y * sens, -armed.ArmPitchReach * 2f, armed.ArmPitchReach * 2f);
            }
            else if (Input.IsActionPressed(LookAction))
            {
                // Head only: the turret and the weapons stay where they were pointing.
                float limit = Mathf.Min(Unit.MaxTwist, 1.75f);
                _lookYaw = Mathf.Clamp(_lookYaw - motion.Relative.X * sens, -limit, limit);
                _lookPitch = Mathf.Clamp(_lookPitch - motion.Relative.Y * sens, -MaxLookPitch, MaxLookPitch);
            }
            else if (IsPlane)
            {
                // The mouse points where to fly: the aircraft banks round to that heading and holds that climb.
                _lead = Mathf.Clamp(_lead - motion.Relative.X * sens, -MaxLead, MaxLead);
                Unit.TargetPitch = Mathf.Clamp(Unit.TargetPitch - motion.Relative.Y * sens, Unit.MinPitch, Unit.MaxPitch);
                if (motion.Relative.Y != 0f) _keyPitch = false;
            }
            else
            {
                SetTwist(Unit.TargetYaw - motion.Relative.X * sens);
                Unit.TargetPitch = Mathf.Clamp(Unit.TargetPitch - motion.Relative.Y * sens, Unit.MinPitch, Unit.MaxPitch);
            }
        }
        else if (@event.IsActionPressed("toggle_view"))
        {
            CockpitView = !CockpitView;
            if (_cockpit != null) _cockpit.Visible = CockpitView;
        }
        else if (@event.IsActionPressed("center_torso")) Unit.TargetYaw = 0f;
        else if (@event.IsActionPressed("counter_rotate")) CounterRotate = !CounterRotate;
        else if (@event.IsActionPressed("toggle_lights")) Headlight.Toggle(Unit.Body);
        else if (@event.IsActionPressed("zoom")) Zoomed = !Zoomed;
        else if (@event.IsActionPressed("zoom_in")) Zoomed = true;
        else if (@event.IsActionPressed("zoom_out")) Zoomed = false;
        else if (Jammed) return;
        else if (@event.IsActionPressed("target_nearest"))
        {
            var list = TargetsByDistance();
            SetTarget(list.Count > 0 ? list[0] : null);
        }
        else if (@event.IsActionPressed("target_next"))
        {
            var list = TargetsByDistance();
            SetTarget(list.Count > 0 ? list[(list.IndexOf(Target) + 1) % list.Count] : null);
        }
        else if (@event.IsActionPressed("target_friendly"))
        {
            // One's own side, nearest first: to see how a lancemate is holding up.
            var list = Units.All.FindAll(t => t != Unit && t.Team == Unit.Team && !t.IsDestroyed);
            Vector3 me = Unit.GlobalPosition;
            list.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(me).CompareTo(b.GlobalPosition.DistanceSquaredTo(me)));
            SetTarget(list.Count > 0 ? list[(list.IndexOf(Target) + 1) % list.Count] : null);
        }
        else if (@event.IsActionPressed("target_reticle"))
        {
            var aimed = Units.FromCollider(Unit.AimCollider);
            if (aimed != null && !aimed.IsDestroyed) SetTarget(aimed);
        }
    }

    void SetTwist(float yaw)
    {
        // A turret that goes all the way round has no stops.
        Unit.TargetYaw = Unit.MaxTwist >= Mathf.Pi - 0.01f ? Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi) : Mathf.Clamp(yaw, -Unit.MaxTwist, Unit.MaxTwist);
    }

    /// <summary>Called when a click was used for something else (leaving a menu), so it doesn't also fire.</summary>
    public void SuppressFireUntilRelease() => _suppressFire = true;

    public override void Think(float dt)
    {
        if (Game.Typing) return;
        // In a cutscene (or flying the console's free camera) the player has no controls: the machine
        // stands, its guns quiet.
        if (StoryOverlay.InCutscene || FreeCamera.Current != null)
        {
            Unit.Fire = 0;
            Unit.Jump = false;
            Unit.TurnInput = 0f;
            if (Cutscene.Holding) Unit.Throttle = 0f;
            return;
        }
        if (Target != null && (Target is not Node node || !IsInstanceValid(node) || Target.IsDestroyed))
        {
            // The one we had in the sight is down: the computer says so.
            if (Target is Node gone && IsInstanceValid(gone) && Target.IsDestroyed && Units.Hostile(Unit, Target))
                CockpitVoice.Say("TargetDestroyed");
            SetTarget(null);
        }

        int fire = 0;
        for (int g = 1; g <= WeaponSystem.GroupCount; g++)
            if (Input.IsActionPressed(FireActions[g - 1])) fire |= WeaponSystem.Bit(g);
        int numbered = WeaponSystem.Bit(4) | WeaponSystem.Bit(5) | WeaponSystem.Bit(6);
        if (SquadCommand.Listening) _holdNumberGroups = true;
        else if ((fire & numbered) == 0) _holdNumberGroups = false;
        if (_holdNumberGroups) fire &= ~numbered;
        if (_suppressFire && fire == 0) _suppressFire = false;
        Unit.Fire = fire;
        Unit.Jump = Input.IsActionPressed("jump");
        Unit.Descend = Input.IsActionPressed("descend");
        if (Unit is Mech mech) Kneeling(mech);

        UpdateLock(dt);
        CounterTurn();
        if (Unit.Offline) return;
        Unit.TurnInput = Input.GetAxis("turn_right", "turn_left");
        if (IsPlane)
        {
            FlyPlane(dt);
            return;
        }
        UpdateThrottle(dt);
    }

    bool _kneel;

    /// <summary>
    /// A mech goes down on one knee and up again on a key (Shift); opening the throttle or turning on the
    /// legs gets it up too, and it goes on its way.
    /// </summary>
    void Kneeling(Mech mech)
    {
        if (Input.IsActionJustPressed("kneel"))
        {
            _kneel = !_kneel;
            if (_kneel) Unit.Throttle = 0f;
        }
        else if (_kneel && (Input.IsActionPressed("throttle_up") || Input.IsActionPressed("throttle_down")
                            || Input.IsActionPressed("turn_left") || Input.IsActionPressed("turn_right")))
            _kneel = false;
        if (mech.IsDestroyed) _kneel = false;
        mech.Kneel = _kneel;
    }

    static readonly string[] FireActions = { "fire_group_1", "fire_group_2", "fire_group_3", "fire_group_4", "fire_group_5", "fire_group_6" };

    /// <summary>With counter-rotation on, whatever the legs turned since the last tick the torso turns back.</summary>
    void CounterTurn()
    {
        float hull = Unit.HullYaw;
        float turned = _hullSet ? Mathf.AngleDifference(_lastHull, hull) : 0f;
        _lastHull = hull;
        _hullSet = true;
        // (A jump in heading is no turn: the machine has just been put somewhere.)
        if (!CounterRotate || IsPlane || Unit.MaxTwist <= 0.01f || Mathf.Abs(turned) > 0.3f) return;
        SetTwist(Unit.TargetYaw - turned);
    }

    /// <summary>
    /// An aircraft: A/D bank it into a turn, W/S lower and raise the nose (let go and it levels out),
    /// Shift/Ctrl are the throttle. Or the mouse alone: sweep it to where the aircraft should go.
    /// </summary>
    void FlyPlane(float dt)
    {
        // What it has turned since the last tick is that much less left to turn.
        float yaw = Unit.HullYaw, turned = Mathf.AngleDifference(_lastYaw, yaw);
        _lastYaw = yaw;
        // (A jump in heading is no turn: the aircraft has just been put somewhere — at the start, or back in the fight.)
        if (Mathf.Abs(turned) > 0.3f) _lead = turned = 0f;
        // (Used up, not overshot: once round to the heading asked for, it levels out and stays there.)
        float left = _lead - turned;
        _lead = _lead == 0f || Mathf.Sign(left) != Mathf.Sign(_lead) ? 0f : left;
        float keys = Input.GetAxis("turn_right", "turn_left");
        if (keys != 0f) _lead = 0f;
        Unit.TurnInput = Mathf.Clamp(keys + _lead * LeadGain, -1f, 1f);

        float nose = Input.GetAxis("throttle_up", "throttle_down") * (WClimbs ? -1f : 1f);
        if (nose != 0f)
        {
            Unit.TargetPitch = Mathf.Clamp(Unit.TargetPitch + nose * StickPitchRate * dt, Unit.MinPitch, Unit.MaxPitch);
            _keyPitch = true;
        }
        else if (_keyPitch) Unit.TargetPitch = Mathf.MoveToward(Unit.TargetPitch, 0f, StickPitchRate * dt);

        // A plain lever, idle to full: no reverse and no stop at zero for something that can't stand still.
        float lever = (Input.IsActionPressed("descend") ? 1f : 0f) - (Input.IsActionPressed("free_look") ? 1f : 0f);
        Unit.Throttle = Mathf.Clamp(Unit.Throttle + lever * Unit.ThrottleRate * dt, 0f, 1f);
    }

    void UpdateThrottle(float dt)
    {
        bool up = Input.IsActionPressed("throttle_up");
        bool down = Input.IsActionPressed("throttle_down");
        if (Input.IsActionJustPressed("full_stop")) Unit.Throttle = 0f;

        if (!up && !down) _throttleDetent = 0f;
        float input = (up ? 1f : 0f) - (down ? 1f : 0f);
        if (input == 0f) return;

        float throttle = Unit.Throttle;
        float next = Mathf.Clamp(throttle + input * Unit.ThrottleRate * dt, -Unit.ReverseFraction, 1f);
        // Detent at zero, like a real throttle: stop there until the key is pressed again.
        if (_throttleDetent == 0f && throttle != 0f && Mathf.Sign(next) != Mathf.Sign(throttle))
        {
            next = 0f;
            _throttleDetent = 1f;
        }
        else if (_throttleDetent != 0f && throttle == 0f)
        {
            next = 0f;
        }
        Unit.Throttle = next;
    }

    // ── Targeting ───────────────────────────────────────────────

    /// <summary>
    /// Missiles only home once locked: the sight has to be kept on the selected target for LockTime.
    /// Looking away loses the lock faster than it was gained.
    /// </summary>
    void UpdateLock(float dt)
    {
        bool onTarget = false;
        // Electronics burnt out by a pulse: the target is lost and none can be picked until they recover.
        if (Jammed) SetTarget(null);
        if (Target != null && !Unit.Offline)
        {
            Vector3 to = Target.AimCenter - _aimView.Origin;
            // (No missile lock on one's own side.)
            onTarget = Target.Team != Unit.Team && to.Length() < LockRange && (-_aimView.Basis.Z).AngleTo(to) < LockCone;
        }
        bool was = LockProgress >= 1f;
        LockProgress = Mathf.Clamp(LockProgress + (onTarget ? dt / LockTime : -dt * 1.5f), 0f, 1f);
        bool locked = LockProgress >= 1f;
        Unit.LockTarget = locked ? Target : null;
        if (locked && !was) _lockSound?.Play();
    }

    void SetTarget(ITarget target)
    {
        if (target == Target) return;
        if (Target is Node old && IsInstanceValid(old)) Target.SetPreview(false, PreviewLayer);
        Target = target;
        Unit.LockTarget = null;
        LockProgress = 0f;
        if (target != null) _acquireSound?.Play();
        target?.SetPreview(true, PreviewLayer);
    }

    List<ITarget> TargetsByDistance()
    {
        var list = Units.All.FindAll(t => Units.Hostile(Unit, t) && !t.IsDestroyed);
        Vector3 me = Unit.GlobalPosition;
        list.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(me).CompareTo(b.GlobalPosition.DistanceSquaredTo(me)));
        return list;
    }

    public override void OnKilled()
    {
        SetTarget(null);
        Zoomed = false;
    }

    // ── Camera ──────────────────────────────────────────────────

    ITarget _watched;

    /// <summary>Out of a network battle without respawns: the camera follows those still fighting (fire to change whom).</summary>
    bool Spectating => Unit.IsDestroyed && Net.Online && !Net.Respawns;

    void Spectate()
    {
        bool gone = _watched is not Node node || !IsInstanceValid(node) || _watched.IsDestroyed;
        if (gone || Input.IsActionJustPressed("fire_group_1"))
        {
            var alive = Units.All.FindAll(t => !t.IsDestroyed && t != Unit);
            _watched = alive.Count == 0 ? null : alive[(alive.IndexOf(_watched) + 1) % alive.Count];
        }
        if (_watched == null) return;
        float yaw = _watched is IDrivable machine ? machine.HullYaw : 0f;
        Vector3 centre = _watched.AimCenter;
        Camera.GlobalPosition = centre + new Basis(Vector3.Up, yaw) * new Vector3(0f, 7f, 22f);
        Camera.LookAt(centre, Vector3.Up);
    }

    public override void UpdateView(float dt)
    {
        if (Spectating)
        {
            if (_cockpit != null) _cockpit.Visible = false;
            Spectate();
            return;
        }
        // Free look springs back as soon as the key is released.
        // So do the arms, once Alt is let go: back to where the torso aims.
        if (Unit is Mech mech && (!Input.IsActionPressed("arm_look") || Input.MouseMode != Input.MouseModeEnum.Captured))
        {
            float back = Mathf.Exp(-8f * dt);
            mech.ArmLookYaw *= back;
            mech.ArmLookPitch *= back;
        }
        if (!Input.IsActionPressed(LookAction) || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            float k = Mathf.Exp(-12f * dt);
            _lookYaw *= k;
            _lookPitch *= k;
        }

        Camera.Fov = Mathf.Lerp(Camera.Fov, Zoomed ? ZoomFov : BaseFov, 1f - Mathf.Exp(-14f * dt));

        // _aimView is where the weapons look; the camera may be turned away from it by free look.
        if (CockpitView)
        {
            _aimView = Unit.AimAnchor.GlobalTransform.Orthonormalized();
            if (Unit is Mech)
            {
                // The pilot's head doesn't ride every lurch of a walking mech: the view keeps to where the
                // torso is turned, taking only a share of the body's rocking and of its bounce. The cockpit
                // around it still moves with the machine.
                Basis level = new Basis(Vector3.Up, Unit.HullYaw + Unit.TorsoYaw) * new Basis(Vector3.Right, Unit.TorsoPitch);
                Basis steady = level.Slerp(_aimView.Basis, 1f - HeadSteadiness).Orthonormalized();
                Vector3 seat = _aimView.Origin;
                _eyeHeight = _eyeSet ? Mathf.Lerp(_eyeHeight, seat.Y, 1f - Mathf.Exp(-5f * dt)) : seat.Y;
                _eyeSet = true;
                // …but the head stays inside the cab: never more than a hand's breadth from the seat.
                seat.Y = Mathf.Clamp(Mathf.Lerp(seat.Y, _eyeHeight, HeadSteadiness), seat.Y - HeadTravel, seat.Y + HeadTravel);
                _aimView = new Transform3D(steady, seat);
            }
            Basis look = new Basis(Vector3.Up, _lookYaw) * new Basis(Vector3.Right, _lookPitch);
            Camera.GlobalTransform = new Transform3D(_aimView.Basis * look, _aimView.Origin);
        }
        else
        {
            float worldYaw = Unit.HullYaw + Unit.TorsoYaw;
            float pitch = Unit.TorsoPitch * 0.7f - 0.1f;
            Vector3 pivot = Unit.ViewPivot + Vector3.Up * 1.5f * Unit.ChaseScale;
            _aimView = ChaseView(pivot, worldYaw, pitch);
            // Upper limit keeps the orbiting camera above the ground.
            Camera.GlobalTransform = ChaseView(pivot, worldYaw + _lookYaw, Mathf.Clamp(pitch + _lookPitch, -1.2f, 0.4f));
            _eyeSet = false;
        }

        Unit.Shake = Mathf.Min(Unit.Shake, 0.6f) * Mathf.Exp(-5f * dt);
        // Less shake when zoomed, or aimed shots would be impossible on the move.
        float s = Unit.Shake * (CockpitView ? CockpitShake : 0.5f) * Camera.Fov / BaseFov;
        Vector3 offset = new Vector3(Mathf.Sin(Unit.Time * 41f), Mathf.Sin(Unit.Time * 33f + 1.7f), 0f) * s;
        if (CockpitView)
        {
            // In the cab the jolt mostly rocks the view; the eye itself barely leaves its place, so it
            // can't end up outside the canopy.
            Camera.GlobalPosition += Camera.GlobalTransform.Basis * offset.LimitLength(HeadTravel * 0.5f);
            Camera.RotateObjectLocal(Vector3.Forward, offset.X * 0.12f);
            Camera.RotateObjectLocal(Vector3.Right, offset.Y * 0.08f);
        }
        else Camera.GlobalPosition += Camera.GlobalTransform.Basis * offset;

        // From the cockpit the machine's own torso is drawn over the map (not once it is a wreck).
        bool over = CockpitView && !Unit.IsDestroyed;
        if (over != _drawnOver && Unit is Mech own)
        {
            own.DrawOver(over);
            _drawnOver = over;
        }

        // Shots are drawn from beyond the cab while looking out of it.
        Unit.Weapons.CabClip = CockpitView ? (Camera.GlobalPosition, -_aimView.Basis.Z, CabDepth * Unit.ChaseScale) : null;
    }

    Transform3D ChaseView(Vector3 pivot, float yaw, float pitch)
    {
        Basis b = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
        return new Transform3D(b, pivot + b * ChaseOffset * Unit.ChaseScale);
    }
}
