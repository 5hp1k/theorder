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
    /// <summary>Seconds the sight must stay on the selected target for a missile lock.</summary>
    [Export] public float LockTime = 1.3f;
    /// <summary>How close to the target the sight must be kept, radians.</summary>
    [Export] public float LockCone = 0.07f;
    [Export] public float LockRange = 800f;

    public bool CockpitView { get; private set; } = true;
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
    public bool CanLock => true;
    public float LockProgress { get; private set; }
    public string Prompt => null;

    float BaseFov => GameConfig.Fov;

    Node3D _cockpit;
    AudioStreamPlayer _lockSound;
    Transform3D _aimView = Transform3D.Identity;
    float _lookYaw, _lookPitch;
    float _throttleDetent;   // blocks throttle from crossing zero until W/S is released
    bool _suppressFire;

    public override void _ExitTree()
    {
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
        if (Unit == null) return;
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Finer control when zoomed in.
            float sens = MouseSensitivity * GameConfig.MouseSensitivity * Camera.Fov / BaseFov;
            if (Input.IsActionPressed("free_look"))
            {
                // Head only: the turret and the weapons stay where they were pointing.
                float limit = Mathf.Min(Unit.MaxTwist, 1.75f);
                _lookYaw = Mathf.Clamp(_lookYaw - motion.Relative.X * sens, -limit, limit);
                _lookPitch = Mathf.Clamp(_lookPitch - motion.Relative.Y * sens, -MaxLookPitch, MaxLookPitch);
            }
            else
            {
                float yaw = Unit.TargetYaw - motion.Relative.X * sens;
                // A turret that goes all the way round has no stops.
                Unit.TargetYaw = Unit.MaxTwist >= Mathf.Pi - 0.01f ? Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi) : Mathf.Clamp(yaw, -Unit.MaxTwist, Unit.MaxTwist);
                Unit.TargetPitch = Mathf.Clamp(Unit.TargetPitch - motion.Relative.Y * sens, Unit.MinPitch, Unit.MaxPitch);
            }
        }
        else if (@event.IsActionPressed("toggle_view"))
        {
            CockpitView = !CockpitView;
            if (_cockpit != null) _cockpit.Visible = CockpitView;
        }
        else if (@event.IsActionPressed("center_torso")) Unit.TargetYaw = 0f;
        else if (@event.IsActionPressed("zoom")) Zoomed = !Zoomed;
        else if (@event.IsActionPressed("zoom_in")) Zoomed = true;
        else if (@event.IsActionPressed("zoom_out")) Zoomed = false;
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
        else if (@event.IsActionPressed("target_reticle"))
        {
            var aimed = Units.FromCollider(Unit.AimCollider);
            if (aimed != null && !aimed.IsDestroyed) SetTarget(aimed);
        }
    }

    /// <summary>Called when a click was used for something else (leaving a menu), so it doesn't also fire.</summary>
    public void SuppressFireUntilRelease() => _suppressFire = true;

    public override void Think(float dt)
    {
        if (Target != null && (Target is not Node node || !IsInstanceValid(node) || Target.IsDestroyed)) SetTarget(null);

        bool g1 = Input.IsActionPressed("fire_group_1");
        bool g2 = Input.IsActionPressed("fire_group_2");
        bool g3 = Input.IsActionPressed("fire_group_3");
        if (_suppressFire && !g1 && !g2 && !g3) _suppressFire = false;
        Unit.Fire1 = g1;
        Unit.Fire2 = g2;
        Unit.Fire3 = g3;
        Unit.Jump = Input.IsActionPressed("jump");
        Unit.Descend = Input.IsActionPressed("descend");

        UpdateLock(dt);
        if (Unit.Offline) return;
        Unit.TurnInput = Input.GetAxis("turn_right", "turn_left");
        UpdateThrottle(dt);
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
        if (Target != null && !Unit.Offline)
        {
            Vector3 to = Target.AimCenter - _aimView.Origin;
            onTarget = to.Length() < LockRange && (-_aimView.Basis.Z).AngleTo(to) < LockCone;
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

    public override void UpdateView(float dt)
    {
        // Free look springs back as soon as the key is released.
        if (!Input.IsActionPressed("free_look") || Input.MouseMode != Input.MouseModeEnum.Captured)
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
        }

        Unit.Shake = Mathf.Min(Unit.Shake, 0.6f) * Mathf.Exp(-5f * dt);
        // Less shake when zoomed, or aimed shots would be impossible on the move.
        float s = Unit.Shake * (CockpitView ? 1f : 0.5f) * Camera.Fov / BaseFov;
        Vector3 offset = new Vector3(Mathf.Sin(Unit.Time * 41f), Mathf.Sin(Unit.Time * 33f + 1.7f), 0f) * s;
        Camera.GlobalPosition += Camera.GlobalTransform.Basis * offset;
    }

    Transform3D ChaseView(Vector3 pivot, float yaw, float pitch)
    {
        Basis b = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, pitch);
        return new Transform3D(b, pivot + b * ChaseOffset * Unit.ChaseScale);
    }
}
