using Godot;

namespace MechSim;

/// <summary>
/// Someone on foot, controlled first-person: a soldier, or a suit of power armour (IsSuit) that
/// a soldier can climb into with the interact key. Both use this script; the difference is in the
/// numbers and the weapons set up in the scene (soldier.tscn, power_armor.tscn).
/// Scene layout: "Head" (pitches with the view) holding "Camera" and the Weapon nodes;
/// "Body" holding the figure: either an animated model (anything with an AnimationPlayer under Body —
/// the clips to play are named in the Animation group) or plain "LegL", "LegR", "ArmL", "ArmR" pivots
/// that are swung while walking;
/// optional "Jets" (particles + "Sound") for the jetpack.
/// </summary>
public partial class Trooper : CharacterBody3D, IDamageable, ITarget, ICockpit
{
    /// <summary>The one the player is controlling right now.</summary>
    public static Trooper Active { get; private set; }

    [Export] public string DisplayName { get; set; } = "Пехотинец";
    [Export] public float MaxHp = 10f;
    [Export] public float WalkSpeed = 6f;
    [Export] public float Acceleration = 40f;
    [Export] public float JumpSpeed = 7f;
    [Export] public float Gravity = 20f;
    [Export] public float MouseSensitivity = 0.0025f;
    /// <summary>Battle armour: can be entered and left with the interact key.</summary>
    [Export] public bool IsSuit;
    /// <summary>How close a soldier must be to climb in.</summary>
    [Export] public float EnterRange = 5f;
    /// <summary>Third-person camera offset from the head.</summary>
    [Export] public Vector3 ChaseOffset = new(0f, 0.8f, 5f);
    /// <summary>Spawned when it's destroyed.</summary>
    [Export] public PackedScene DeathFx;

    [ExportGroup("Animation")]
    /// <summary>Clips of the model's AnimationPlayer. Leave a name empty to skip that clip.</summary>
    [Export] public string IdleClip = "CharacterArmature|Idle";
    [Export] public string RunClip = "CharacterArmature|Run_Gun";
    [Export] public string AirClip = "CharacterArmature|Jump_Idle";
    [Export] public string DeathClip = "CharacterArmature|Death";
    /// <summary>Ground speed the run clip was animated for, m/s: it is played faster or slower to match.</summary>
    [Export] public float RunClipSpeed = 6f;
    /// <summary>Nodes of the model to switch off (spare weapons that come with it).</summary>
    [Export] public Godot.Collections.Array<string> HiddenParts = new();

    [ExportGroup("Jetpack")]
    [Export] public bool HasJetpack;
    [Export] public float JetThrust = 42f;
    [Export] public float JetClimbSpeed = 14f;
    [Export] public float JetFuelTime = 4f;
    [Export] public float JetRecharge = 6f;

    public float Hp { get; private set; }
    public float JetFuel { get; private set; } = 1f;
    public float Integrity => Mathf.Clamp(Hp / MaxHp, 0f, 1f);
    public DamageModel Damage => null;
    public void SetPreview(bool on, int layer) => Units.SetPreview(this, on, layer);
    public bool Controlled { get; private set; }
    public bool IsDestroyed => Hp <= 0f;
    public Team Team => Team.Player;
    public Vector3 AimCenter => GlobalPosition + Vector3.Up * (_eyeHeight * 0.6f);
    public WeaponSystem Weapons { get; private set; }
    public Camera3D Camera { get; private set; }
    public Vector3 AimPoint { get; private set; }
    /// <summary>The soldier inside a suit.</summary>
    public Trooper Rider { get; private set; }
    /// <summary>A suit close enough to climb into, if any (for the HUD prompt).</summary>
    public Trooper SuitInReach { get; private set; }
    public bool ThirdPerson { get; private set; }

    // ── The seat, as the HUD sees it ────────────────────────────
    public string UnitName => DisplayName;
    public Vector3 WorldPosition => GlobalPosition;
    public float HullYaw => Rotation.Y;
    public float Throttle => SpeedFraction;
    public float ReverseFraction => 1f;
    public float SpeedFraction => _forwardSpeed / WalkSpeed;
    public float SpeedKmh => new Vector3(Velocity.X, 0f, Velocity.Z).Length() * 3.6f;
    public bool HasHeat => false;
    public float Heat => 0f;
    public bool IsShutdown => false;
    public float RestartHeat => 0f;
    public float RestartProgress => 0f;
    public float Twist => 0f;
    public float MaxTwist => 0f;
    public float Pitch => 0f;
    public float MinPitch => 0f;
    public float MaxPitch => 0f;
    public bool CounterRotate => false;
    public float HeadingDeg => Mathf.PosMod(Mathf.RadToDeg(-Rotation.Y), 360f);
    public bool HasJump => HasJetpack;
    public float JumpFuel => JetFuel;
    public bool Thrusting { get; private set; }
    public bool Offline => IsDestroyed;
    public float RespawnLeft { get; private set; }
    public bool FirstPerson => !ThirdPerson;
    public bool FreeLook => false;
    public float Magnification => 1f;
    public GodotObject AimCollider { get; private set; }
    public Vector3? LeadAim => null;
    public bool CanLock => false;
    public ITarget Target => _lock;
    public float LockProgress => Mathf.Clamp(_lockTime / LockTime, 0f, 1f);
    /// <summary>Seconds the crosshair must stay on a unit before missiles will follow it.</summary>
    [Export] public float LockTime = 1.0f;
    public string Prompt => IsDestroyed ? null
        : IsSuit ? (Rider != null ? "F — выйти из брони" : null)
        : SuitInReach != null ? "F — надеть броню" : null;

    Node3D _head, _body, _jets;
    Node3D[] _limbs;
    AudioStreamPlayer3D _jetSound;
    readonly Godot.Collections.Array<Rid> _exclude = new();
    Transform3D _spawn;
    float _eyeHeight, _pitch, _walkPhase, _forwardSpeed;
    bool _suppressFire;
    AnimationPlayer _anim;
    string _clip;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _body = GetNode<Node3D>("Body");
        Camera = GetNode<Camera3D>("Head/Camera");
        _limbs = new[]
        {
            GetNodeOrNull<Node3D>("Body/LegL"), GetNodeOrNull<Node3D>("Body/LegR"),
            GetNodeOrNull<Node3D>("Body/ArmL"), GetNodeOrNull<Node3D>("Body/ArmR"),
        };
        var players = Geo.Collect<AnimationPlayer>(_body);
        if (players.Count > 0)
        {
            _anim = players[0];
            // Standing and running go round and round.
            foreach (string clip in new[] { IdleClip, RunClip, AirClip })
                if (!string.IsNullOrEmpty(clip) && _anim.HasAnimation(clip)) _anim.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
        }
        foreach (string hidden in HiddenParts)
            if (_body.FindChild(hidden, true, false) is Node3D part) part.Visible = false;
        _jets = GetNodeOrNull<Node3D>("Jets");
        _jetSound = GetNodeOrNull<AudioStreamPlayer3D>("Jets/Sound");
        _eyeHeight = _head.Position.Y;

        var terrain = MapTerrain.Instance;
        if (terrain != null)
            GlobalPosition = new Vector3(GlobalPosition.X, terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z) + 0.1f, GlobalPosition.Z);
        _spawn = GlobalTransform;

        Hp = MaxHp;
        _exclude.Add(GetRid());
        Weapons = new WeaponSystem(this, GetParent(), _exclude) { Mask = Phys.MaskPlayerFire };
        // Solid against the world, props and mechs; mechs themselves walk straight over infantry.
        CollisionMask = Phys.LayerWorld | Phys.LayerProps | Phys.LayerTargets | Phys.LayerVehicles;
        SetControlled(false);
        SetJets(false);
    }

    public override void _ExitTree()
    {
        Units.All.Remove(this);
        if (Active == this) Active = null;
        if (Game.PlayerUnit == this) Game.PlayerUnit = null;
        if (Game.Cockpit == this) Game.Cockpit = null;
    }

    // ── Control hand-over ───────────────────────────────────────

    public void TakeControl()
    {
        SetControlled(true);
        Active = this;
        Game.PlayerUnit = this;
        Game.Cockpit = this;
        Camera.Fov = GameConfig.Fov;
        Camera.MakeCurrent();
        _suppressFire = true;
    }

    void SetControlled(bool on)
    {
        Controlled = on;
        // Only the one being played is on the battlefield as far as the enemy is concerned.
        Units.All.Remove(this);
        if (on) Units.All.Add(this);
        // An empty suit is scenery: nothing shoots at it.
        CollisionLayer = on ? Phys.LayerHitPlayer : 0;
        // The player's own model would block the first-person view.
        _body.Visible = !on || ThirdPerson;
    }

    void Enter(Trooper suit)
    {
        SetControlled(false);
        SuitInReach = null;
        Visible = false;
        CollisionMask = 0;
        suit.Rider = this;
        suit.Rotation = Rotation;
        suit.TakeControl();
    }

    void Exit()
    {
        Trooper rider = Rider;
        Rider = null;
        SetControlled(false);
        rider.GlobalPosition = GlobalPosition + GlobalTransform.Basis.X * 2.5f + Vector3.Up * 0.5f;
        rider.Rotation = Rotation;
        rider.Visible = true;
        rider.CollisionMask = CollisionMask;
        rider.TakeControl();
    }

    /// <summary>Back to how the battle started: on the spawn, whole, nobody inside.</summary>
    public void ResetToSpawn()
    {
        Rider = null;
        Hp = MaxHp;
        JetFuel = 1f;
        Velocity = Vector3.Zero;
        GlobalTransform = _spawn;
        _pitch = 0f;
        _head.Rotation = Vector3.Zero;
        _body.Rotation = Vector3.Zero;
        Visible = true;
        CollisionMask = Phys.LayerWorld | Phys.LayerProps | Phys.LayerTargets;
        Weapons.Rearm();
        SetControlled(false);
    }

    // ── Input ───────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Controlled || IsDestroyed) return;
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sens = MouseSensitivity * GameConfig.MouseSensitivity;
            RotateY(-motion.Relative.X * sens);
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * sens, -1.35f, 1.35f);
            _head.Rotation = new Vector3(_pitch, 0f, 0f);
        }
        else if (@event.IsActionPressed("toggle_view"))
        {
            ThirdPerson = !ThirdPerson;
            _body.Visible = ThirdPerson;
        }
        else if (@event.IsActionPressed("interact"))
        {
            // One press does one thing: without this the soldier stepping out would see the same key
            // press and climb straight back in.
            GetViewport().SetInputAsHandled();
            if (IsSuit && Rider != null) Exit();
            else if (!IsSuit && SuitInReach != null) Enter(SuitInReach);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var space = GetWorld3D().DirectSpaceState;
        bool driving = Controlled && !IsDestroyed;

        // Walk: forward/back and strafe, relative to where the body faces.
        Vector3 want = Vector3.Zero;
        if (driving)
        {
            float forward = Input.GetAxis("throttle_down", "throttle_up");
            float strafe = Input.GetAxis("turn_left", "turn_right");
            want = GlobalTransform.Basis * new Vector3(strafe, 0f, -forward);
            if (want.LengthSquared() > 1f) want = want.Normalized();
            float depth = MapTerrain.Instance?.WaterDepthAt(GlobalPosition.X, GlobalPosition.Z) ?? 0f;
            want *= WalkSpeed * (depth > _eyeHeight * 0.5f ? 0.45f : 1f);
        }
        Vector3 v = Velocity;
        Vector3 flat = new Vector3(v.X, 0f, v.Z).MoveToward(want, Acceleration * (IsOnFloor() ? 1f : 0.35f) * dt);
        _forwardSpeed = flat.Dot(-GlobalTransform.Basis.Z);

        // Jump, or burn the jetpack while the key is held.
        bool jump = driving && Input.IsActionPressed("jump");
        bool burning = jump && HasJetpack && JetFuel > 0f;
        if (burning)
        {
            v.Y = Mathf.Min(v.Y + (JetThrust - Gravity) * dt, JetClimbSpeed);
            JetFuel = Mathf.Max(0f, JetFuel - dt / JetFuelTime);
        }
        else
        {
            if (IsOnFloor())
            {
                JetFuel = Mathf.Min(1f, JetFuel + dt / JetRecharge);
                v.Y = jump && !HasJetpack ? JumpSpeed : -0.5f;
            }
            else v.Y -= Gravity * dt;
        }
        SetJets(burning);
        Thrusting = burning;
        Velocity = new Vector3(flat.X, v.Y, flat.Z);
        MoveAndSlide();
        if (MapTerrain.Instance is { } land)
        {
            Vector3 kept = land.Contain(GlobalPosition, out bool outside);
            if (outside) GlobalPosition = kept;
        }

        Animate(dt, flat.Length());
        if (!Controlled) return;
        if (IsDestroyed) RespawnLeft = Mathf.Max(0f, RespawnLeft - dt);

        UpdateCamera();
        UpdateAim(space);
        FindSuit();

        int fire = 0;
        for (int g = 1; g <= WeaponSystem.GroupCount; g++)
            if (Input.IsActionPressed($"fire_group_{g}")) fire |= WeaponSystem.Bit(g);
        if (_suppressFire && fire == 0) _suppressFire = false;
        if (driving && !_suppressFire && Input.MouseMode == Input.MouseModeEnum.Captured)
            Weapons.FireGroups(fire, AimPoint, space);
        Weapons.Update(dt, AimPoint, space, _lock);
        Wade(dt, flat.Length());
    }

    ITarget _lock, _aimedAt;
    float _lockTime;

    void UpdateCamera()
    {
        Camera.Position = ThirdPerson ? ChaseOffset : Vector3.Zero;
    }

    void UpdateAim(PhysicsDirectSpaceState3D space)
    {
        // Along the view from the head (not from a third-person camera, or it could aim at one's own back).
        Vector3 from = _head.GlobalPosition;
        Vector3 dir = -_head.GlobalTransform.Basis.Z;
        Vector3 to = from + dir * 1200f;
        AimCollider = null;
        ITarget seen = null;
        if (Phys.Ray(space, from, to, Phys.MaskPlayerFire, _exclude, out var hit))
        {
            AimPoint = hit.Position;
            AimCollider = hit.Collider;
            var aimed = Units.FromCollider(hit.Collider);
            if (aimed != null && !aimed.IsDestroyed) seen = aimed;
        }
        else AimPoint = to;

        // Missiles follow a unit only after the crosshair has been held on it for a moment.
        float dt = (float)GetPhysicsProcessDeltaTime();
        if (seen != null && seen == _aimedAt) _lockTime = Mathf.Min(LockTime, _lockTime + dt);
        else _lockTime = Mathf.Max(0f, _lockTime - dt * 1.5f);
        if (seen != null) _aimedAt = seen;
        _lock = _lockTime >= LockTime ? _aimedAt : null;
    }

    void FindSuit()
    {
        SuitInReach = null;
        if (IsSuit) return;
        foreach (Node node in GetParent().GetChildren())
        {
            if (node is Trooper { IsSuit: true, IsDestroyed: false, Rider: null } suit
                && suit.GlobalPosition.DistanceTo(GlobalPosition) < EnterRange)
                SuitInReach = suit;
        }
    }

    float _wake;

    void Wade(float dt, float speed)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null || speed < 0.5f || terrain.WaterDepthAt(GlobalPosition.X, GlobalPosition.Z) < 0.2f) return;
        _wake -= dt;
        if (_wake > 0f) return;
        _wake = 0.4f;
        WaterSurface.AddRipple(GlobalPosition, 0.5f);
    }

    void Play(string clip, float speed = 1f)
    {
        if (string.IsNullOrEmpty(clip) || !_anim.HasAnimation(clip)) return;
        if (_clip != clip)
        {
            _clip = clip;
            _anim.Play(clip, 0.18);
        }
        _anim.SpeedScale = speed;
    }

    void Animate(float dt, float speed)
    {
        if (_anim != null)
        {
            if (IsDestroyed) Play(DeathClip);
            else if (!IsOnFloor()) Play(AirClip);
            else if (speed > 0.6f) Play(RunClip, Mathf.Clamp(speed / RunClipSpeed, 0.5f, 1.6f));
            else Play(IdleClip);
            return;
        }

        // Arms and legs swing in step with the ground covered.
        _walkPhase += speed * dt * 2.2f / Mathf.Max(_eyeHeight, 0.5f);
        float swing = Mathf.Sin(_walkPhase) * Mathf.Clamp(speed / WalkSpeed, 0f, 1f) * 0.6f;
        if (!IsOnFloor()) swing = 0.25f;
        float[] signs = { 1f, -1f, -0.6f, 0.6f };
        for (int i = 0; i < _limbs.Length; i++)
            if (_limbs[i] != null) _limbs[i].Rotation = new Vector3(swing * signs[i], 0f, 0f);
    }

    void SetJets(bool on)
    {
        if (_jets == null) return;
        foreach (Node child in _jets.GetChildren())
        {
            if (child is GpuParticles3D p) p.Emitting = on;
            else if (child is Light3D light) light.Visible = on;
        }
        if (_jetSound != null && _jetSound.Playing != on)
        {
            if (on) _jetSound.Play();
            else _jetSound.Stop();
        }
    }

    // ── Damage ──────────────────────────────────────────────────

    public void TakeDamage(float amount, Vector3 point)
    {
        if (IsDestroyed || !Controlled || Cheats.Spares(this)) return;
        Hp -= amount;
        if (Hp > 0f) return;

        Hp = 0f;
        Weapons.CancelBeams();
        SetJets(false);
        CollisionLayer = 0;
        _body.Visible = true;
        // A figure with no death animation of its own is simply laid flat on its back.
        if (_anim == null || !_anim.HasAnimation(DeathClip)) _body.Rotation = new Vector3(-1.45f, 0f, 0f);
        ThirdPerson = true;
        if (DeathFx != null)
        {
            var fx = DeathFx.Instantiate<Node3D>();
            GetParent().AddChild(fx);
            fx.GlobalPosition = AimCenter;
        }
        RespawnLeft = Game.Instance?.PlayerRespawnTime ?? 0f;
        Game.Instance?.OnTrooperDied();
    }
}
