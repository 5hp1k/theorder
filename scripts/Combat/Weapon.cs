using System.Collections.Generic;
using Godot;

namespace MechSim;

public enum WeaponKind
{
    /// <summary>Duration beam: damage is dealt over BeamTime while the beam is held on target.</summary>
    Beam,
    /// <summary>Instant hitscan shot with a tracer, uses ammo.</summary>
    Ballistic,
    /// <summary>Ripple-fires a salvo of guided projectiles, uses ammo (one unit per salvo).</summary>
    Missile,
    /// <summary>Hand weapon: the weapon node itself swings and hits whatever is within Range.</summary>
    Melee,
}

/// <summary>What sort of mounting a weapon needs; a hardpoint takes weapons of its own sort (Omni: any gun).</summary>
public enum SlotType
{
    Beam,
    Ballistic,
    Missile,
    /// <summary>Any beam, ballistic or missile weapon.</summary>
    Omni,
    /// <summary>Hand weapons.</summary>
    Melee,
    /// <summary>Bomb bays.</summary>
    Bomb,
}

/// <summary>
/// One weapon, placed in the mech scene at its muzzle (it fires towards the mech's aim point).
/// Optional children: "Beam" (MeshInstance3D, unit-height cylinder, top-level, hidden) for beam weapons,
/// "FireSound" (AudioStreamPlayer3D) played on every shot.
/// </summary>
public partial class Weapon : Node3D
{
    [Export] public string DisplayName = "Оружие";
    [Export(PropertyHint.Range, "1,6")] public int Group = 1;
    [Export] public WeaponKind Kind;
    /// <summary>Which hardpoints it fits.</summary>
    [Export] public SlotType Slot;
    /// <summary>How many of a hardpoint's slots it fills.</summary>
    [Export(PropertyHint.Range, "1,6")] public int Slots = 1;
    /// <summary>Section the weapon is mounted in; it's lost together with the section.</summary>
    [Export] public Section Mount = Section.RightArm;
    /// <summary>Per beam, per shell or per missile.</summary>
    [Export] public float Damage = 10f;
    /// <summary>Heat per shot as a fraction of the shutdown threshold (0..1).</summary>
    [Export] public float Heat = 0.1f;
    [Export] public float Cooldown = 1f;
    [Export] public float Range = 450f;
    [Export] public float BeamTime = 0.5f;
    /// <summary>-1 = unlimited.</summary>
    [Export] public int MaxAmmo = -1;
    /// <summary>Muzzle flash, impact and tracer colour.</summary>
    [Export] public Color FxColor = new(1f, 0.2f, 0.15f);

    /// <summary>Random spread of ballistic shots, radians.</summary>
    [Export] public float Spread;
    /// <summary>Radius of the mark a hit leaves on the ground; 0 = none.</summary>
    [Export] public float CraterSize;

    /// <summary>
    /// A weapon of last resort (the nuclear bomb): a computer pilot never looses it with the rest of its
    /// group, only on its own decision — see <see cref="WeaponSystem.HoldStrategic"/>.
    /// </summary>
    [Export] public bool Strategic;
    /// <summary>An electromagnetic bomb: a computer pilot drops it only if the battle allows (see <see cref="WeaponSystem.HoldEmp"/>).</summary>
    [Export] public bool Emp;

    [ExportGroup("Recoil")]
    /// <summary>How hard each shot jolts the machine that fires it (a heavy gun rocks a tank on its springs).</summary>
    [Export] public float Kick;
    /// <summary>How far the gun (the arm, the barrel) is driven back by a shot, metres, and the seconds it takes to run out again.</summary>
    [Export] public float RecoilTravel;
    [Export] public float RecoilReturn = 0.35f;
    /// <summary>The cloud of smoke a shot leaves at the muzzle (scenes/fx/muzzle_smoke*.tscn); none for a beam.</summary>
    [Export] public PackedScene MuzzleSmoke;
    /// <summary>The flame out of the muzzle (scenes/fx/muzzle_flash*.tscn), in place of the plain glow.</summary>
    [Export] public PackedScene MuzzleFlash;
    /// <summary>Seconds the barrel goes on smoking after a shot (its "BarrelSmoke" particles, which stay where they were let out: a moving machine leaves a trail).</summary>
    [Export] public float SmokeTime = 3f;
    public GpuParticles3D BarrelSmoke { get; private set; }
    public float SmokeLeft { get; set; }

    [ExportGroup("Flame")]
    /// <summary>
    /// Heat a full burst pours into a mech it is held on, as a share of that mech's shutdown threshold
    /// (a flamethrower). Vehicles have no reactor heat: they only take the damage.
    /// </summary>
    [Export] public float TargetHeat;
    /// <summary>Sets fire to the ground and to whatever else it plays on (a flamethrower), not only to what it destroys.</summary>
    [Export] public bool Ignites;
    /// <summary>Seconds a machine it plays on goes on burning afterwards (see <see cref="UnitFire"/>); 0 = it doesn't catch.</summary>
    [Export] public float Afterburn;
    /// <summary>Seconds until it may light the next fire.</summary>
    public float KindleLeft { get; set; }

    [ExportGroup("Melee")]
    [Export] public float SwingTime = 0.5f;
    /// <summary>How far the weapon node pitches at the bottom of the swing, radians.</summary>
    [Export] public float SwingAngle = -1.8f;

    /// <summary>
    /// Runs on the machine's power, though it is not a beam (a PPC, a gauss rifle): an electromagnetic
    /// pulse knocks it out with the beams. Flamethrowers and plain guns keep working.
    /// </summary>
    [Export] public bool NeedsPower;
    /// <summary>For a ballistic gun, what the shot leaves in the air instead of a tracer (a <see cref="GaussTrail"/> scene).</summary>
    [Export] public PackedScene Trail;

    [ExportGroup("Missiles")]
    [Export] public PackedScene ProjectileScene;
    [Export] public int SalvoSize = 10;
    /// <summary>Seconds between missiles of one salvo.</summary>
    [Export] public float SalvoInterval = 0.07f;
    /// <summary>Size of the rack the salvo leaves from (each projectile from a random tube); zero for a single barrel.</summary>
    [Export] public Vector2 RackSize = new(1.1f, 0.9f);

    [ExportGroup("Shells")]
    /// <summary>
    /// Over 0, the projectile is a shell thrown on a ballistic arc at this speed, m/s (a howitzer): the
    /// gun is laid so that it comes down on the point aimed at, lobbed high if a hill is in the way.
    /// </summary>
    [Export] public float MuzzleSpeed;
    /// <summary>How fast the shell falls, m/s² (with the muzzle speed this sets the reach: speed² / gravity).</summary>
    [Export] public float ShellGravity = 20f;

    /// <summary>Thrown on an arc rather than flown or fired flat.</summary>
    public bool Lobbed => MuzzleSpeed > 0f && Kind == WeaponKind.Missile;

    public int Ammo { get; set; }
    public bool Destroyed { get; set; }
    public float CooldownLeft { get; set; }
    public float BeamLeft { get; set; }
    public int SalvoLeft { get; set; }
    public float SalvoTimer { get; set; }
    public float SwingLeft { get; set; }
    public bool SwingHit { get; set; }
    /// <summary>Shares of a swing: up to here the wind-up, at <see cref="Strike"/> the blow lands, then the recovery.</summary>
    public const float WindUp = 0.38f, Strike = 0.56f;
    /// <summary>How far through its swing a hand weapon is, 0..1 (0 when at rest).</summary>
    public float SwingPhase => SwingLeft > 0f && SwingTime > 0f ? Mathf.Clamp(1f - SwingLeft / SwingTime, 0f, 1f) : 0f;
    public Vector3 RestRotation { get; private set; }
    public MeshInstance3D BeamMesh { get; private set; }
    /// <summary>The jet of a flamethrower: particles thrown along the node's -Z while it burns.</summary>
    public GpuParticles3D Flame { get; private set; }
    public AudioStreamPlayer3D FireSound { get; private set; }

    public bool HasAmmo => Ammo != 0;
    /// <summary>An energy weapon: a laser, or anything that <see cref="NeedsPower"/>.</summary>
    public bool Energy => NeedsPower || (Kind == WeaponKind.Beam && Flame == null);
    /// <summary>Out of action for now: an energy weapon on a machine caught by an electromagnetic pulse.</summary>
    public bool Jammed { get; set; }
    public bool IsReady => !Destroyed && !Jammed && CooldownLeft <= 0f && BeamLeft <= 0f && SalvoLeft <= 0 && SwingLeft <= 0f && HasAmmo;
    public float ReadyFraction => Cooldown > 0f ? Mathf.Clamp(1f - CooldownLeft / Cooldown, 0f, 1f) : 1f;

    public override void _Ready()
    {
        Ammo = MaxAmmo;
        RestRotation = Rotation;
        BeamMesh = GetNodeOrNull<MeshInstance3D>("Beam");
        Flame = GetNodeOrNull<GpuParticles3D>("Flame");
        BarrelSmoke = GetNodeOrNull<GpuParticles3D>("BarrelSmoke");
        if (Flame != null) SetFlame(false);
        FireSound = GetNodeOrNull<AudioStreamPlayer3D>("FireSound");
    }

    /// <summary>Lights or puts out the jet: the flame and whatever rides with it (its smoke).</summary>
    public void SetFlame(bool on)
    {
        if (Flame == null || Flame.Emitting == on) return;
        Flame.Emitting = on;
        foreach (Node child in Flame.GetChildren())
            if (child is GpuParticles3D p) p.Emitting = on;
    }

    public void StopFiring()
    {
        SwingLeft = 0f;
        Rotation = RestRotation;
        BeamLeft = 0f;
        SalvoLeft = 0;
        if (BeamMesh != null) BeamMesh.Visible = false;
        if (Flame != null)
        {
            SetFlame(false);
            FireSound?.Stop();
        }
    }
}

/// <summary>Fires a mech's weapon groups, drives beams and salvos, applies damage.</summary>
public sealed class WeaponSystem
{
    public readonly List<Weapon> Weapons = new();
    /// <summary>Multiplier on all damage dealt.</summary>
    public float DamageScale = 1f;
    /// <summary>What the shots can hit.</summary>
    public uint Mask;
    /// <summary>Velocity of the machine carrying the weapons (bombs inherit it).</summary>
    public Vector3 CarrierVelocity;
    /// <summary>
    /// Set while the player looks out of the cockpit: the eye, the way it looks, and how far ahead of it
    /// the cab ends. Beams, tracers and flashes are only drawn beyond that, so a gun mounted behind or
    /// beside the pilot doesn't appear to fire through the cab. The shots themselves are unaffected.
    /// </summary>
    public (Vector3 eye, Vector3 forward, float depth)? CabClip;
    /// <summary>Hand weapons are only swung, and flamethrowers only lit, when the aim point is within their reach.</summary>
    public bool MeleeInReachOnly;
    /// <summary>Electromagnetic bombs stay on the rack (a computer pilot, when the battle doesn't allow them).</summary>
    public bool HoldEmp;
    /// <summary>A shot has left the weapon (each missile of a salvo, each burst of a beam): the machine rocks, the gun recoils.</summary>
    public System.Action<Weapon> Fired;
    /// <summary>Strategic weapons stay on the rack whatever group is fired (a computer pilot's safety catch)…</summary>
    public bool HoldStrategic;
    /// <summary>…until the pilot clears them, for the tick it is set in.</summary>
    public bool ReleaseStrategic;

    /// <summary>A strategic weapon that could be dropped right now, or null.</summary>
    public Weapon ReadyStrategic
    {
        get
        {
            foreach (var w in Weapons)
                if (w.Strategic && w.IsReady) return w;
            return null;
        }
    }

    /// <summary>Where a shot from one point to another first shows.</summary>
    Vector3 Seen(Vector3 from, Vector3 to, out bool muzzleInView)
    {
        muzzleInView = true;
        if (CabClip is not { } clip) return from;
        float at = (from - clip.eye).Dot(clip.forward);
        if (at >= clip.depth) return from;
        muzzleInView = false;
        Vector3 dir = (to - from).Normalized();
        float along = dir.Dot(clip.forward);
        return along > 0.05f ? from + dir * ((clip.depth - at) / along) : from;
    }

    readonly ITarget _owner;

    /// <summary>Deals damage in the owner's name (the score needs to know whose shot it was).</summary>
    void Hurt(in Phys.Hit hit, float amount)
    {
        Score.Attacker = _owner;
        Phys.Damage(hit, amount);
        Score.Attacker = null;
    }

    readonly Node _fxRoot;
    readonly Godot.Collections.Array<Rid> _exclude;

    public WeaponSystem(Node owner, Node fxRoot, Godot.Collections.Array<Rid> exclude)
    {
        _fxRoot = fxRoot;
        _exclude = exclude;
        _owner = owner as ITarget;
        Geo.Collect(owner, Weapons);
    }

    /// <summary>
    /// Where the arms are pointing, when the machine aims its arms apart from its torso (a mech, as in
    /// MechWarrior 5: the arms reach for the mouse faster than the torso can turn). Weapons in the arms
    /// fire there; the rest fire at the aim point they are given. Null = everything at the one point.
    /// </summary>
    public Vector3? ArmAim;

    /// <summary>The point this weapon fires at: the arms' own, for a weapon in an arm.</summary>
    Vector3 AimFor(Weapon w, Vector3 aimPoint)
        => ArmAim is { } arms && w.Mount is Section.LeftArm or Section.RightArm ? arms : aimPoint;

    /// <summary>How many weapon groups there are (as in MechWarrior 4: six).</summary>
    public const int GroupCount = 6;

    /// <summary>The bit of a group in a pilot's fire command (<see cref="IDrivable.Fire"/>).</summary>
    public static int Bit(int group) => 1 << (group - 1);

    /// <summary>Fires every group whose bit is set in <paramref name="groups"/>. Returns heat generated.</summary>
    public float FireGroups(int groups, Vector3 aimPoint, PhysicsDirectSpaceState3D space)
    {
        float heat = 0f;
        for (int g = 1; g <= GroupCount; g++)
            if ((groups & Bit(g)) != 0) heat += FireGroup(g, aimPoint, space);
        return heat;
    }

    /// <summary>Fires every ready weapon in the group. Returns heat generated.</summary>
    public float FireGroup(int group, Vector3 aimPoint, PhysicsDirectSpaceState3D space)
    {
        float heat = 0f;
        foreach (var w in Weapons)
        {
            if (w.Group != group || w.Kind == WeaponKind.Melee || !w.IsReady) continue;
            if (w.Strategic && HoldStrategic && !ReleaseStrategic) continue;
            if (w.Emp && HoldEmp) continue;
            // A computer pilot doesn't burn fuel (and its own heat) at something the flame can't reach.
            if (MeleeInReachOnly && w.TargetHeat > 0f && w.GlobalPosition.DistanceTo(aimPoint) > w.Range) continue;
            heat += Fire(w, aimPoint, space);
        }
        // Hand weapons strike one at a time (a machine throws its whole body into a blow), taking turns.
        if (NextMelee(group, aimPoint) is { } melee)
        {
            _lastMelee = Weapons.IndexOf(melee);
            heat += Fire(melee, aimPoint, space);
        }
        return heat;
    }

    int _lastMelee = -1;

    /// <summary>The hand weapon of the group whose turn it is, if no blow is being struck right now.</summary>
    Weapon NextMelee(int group, Vector3 aimPoint)
    {
        foreach (var w in Weapons)
            if (w.Kind == WeaponKind.Melee && w.SwingLeft > 0f) return null;
        for (int i = 1; i <= Weapons.Count; i++)
        {
            var w = Weapons[(_lastMelee + i + Weapons.Count) % Weapons.Count];
            if (w.Kind != WeaponKind.Melee || w.Group != group || !w.IsReady) continue;
            // A computer pilot doesn't flail at something far out of reach (a player may swing at will).
            if (MeleeInReachOnly && w.GlobalPosition.DistanceTo(aimPoint) > w.Range * 1.25f) continue;
            return w;
        }
        return null;
    }

    public void CancelBeams()
    {
        foreach (var w in Weapons) w.StopFiring();
    }

    /// <summary>Knocks out (or restores) every weapon mounted in the section.</summary>
    public void SetSectionDestroyed(Section section, bool destroyed)
    {
        foreach (var w in Weapons)
        {
            if (w.Mount != section) continue;
            w.Destroyed = destroyed;
            if (destroyed) w.StopFiring();
        }
    }

    /// <summary>Some weapon has fired off part of its ammunition.</summary>
    public bool NeedsAmmo
    {
        get
        {
            foreach (var w in Weapons)
                if (w.MaxAmmo >= 0 && w.Ammo < w.MaxAmmo) return true;
            return false;
        }
    }

    public void Rearm()
    {
        foreach (var w in Weapons)
        {
            w.Ammo = w.MaxAmmo;
            w.CooldownLeft = 0f;
        }
    }

    float Fire(Weapon w, Vector3 aimPoint, PhysicsDirectSpaceState3D space)
    {
        aimPoint = AimFor(w, aimPoint);
        w.CooldownLeft = w.Cooldown;
        if (w.Ammo > 0) w.Ammo--;

        Vector3 from = w.GlobalPosition;
        switch (w.Kind)
        {
            case WeaponKind.Beam:
                Seen(from, aimPoint, out bool beamMuzzle);
                Shot(w, from, beamMuzzle ? 0.35f : 0f);
                w.BeamLeft = w.BeamTime;
                UpdateBeam(w, aimPoint, space, 0f);
                break;

            case WeaponKind.Ballistic:
                Vector3 end = Trace(w, from, aimPoint, space, out var hit, out bool didHit);
                Vector3 shown = Seen(from, end, out bool muzzle);
                Shot(w, from, muzzle ? 0.5f : 0f, end - from);
                if (w.Trail != null) GaussTrail.Spawn(w.Trail, _fxRoot, shown, end, w.FxColor);
                else TimedFx.Tracer(_fxRoot, shown, end, w.FxColor, 0.06f);
                Impacts.Water(_fxRoot, from, end, 0.5f + w.CraterSize);
                if (didHit)
                {
                    TimedFx.Flash(_fxRoot, end, w.FxColor, 0.6f, 0.12f);
                    Hurt(hit, w.Damage * DamageScale);
                    Impacts.Ground(_fxRoot, hit, w.CraterSize, w.CraterSize < 0.5f ? Mark.Bullet : Mark.Blast);
                    Impacts.Unit(hit, w.CraterSize, w.CraterSize < 0.5f ? Mark.Bullet : Mark.Blast);
                    Impacts.Sparks(_fxRoot, hit);
                }
                break;

            case WeaponKind.Missile:
                // The missiles leave one by one from Update().
                w.SalvoLeft = Mathf.Max(1, w.SalvoSize);
                w.SalvoTimer = 0f;
                break;

            case WeaponKind.Melee:
                // The blow lands part-way through the swing, in Update().
                w.SwingLeft = w.SwingTime;
                w.SwingHit = false;
                PlaySound(w);
                break;
        }
        return w.Heat;
    }

    void Shot(Weapon w, Vector3 from, float flash, Vector3 way = default)
    {
        Basis facing = Basis.Identity;
        if (way.LengthSquared() > 1e-4f)
        {
            Vector3 dir = way.Normalized();
            facing = Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up);
        }
        if (flash > 0f && w.MuzzleFlash != null && way.LengthSquared() > 1e-4f)
        {
            // (Only for the eye, the shot's sound is the weapon's own: not made where nobody would see it.)
            if (OneShotFx.Spawn(w.MuzzleFlash, _fxRoot, from, 1f) is { } fire) fire.GlobalBasis = facing;
        }
        else if (flash > 0f) TimedFx.Flash(_fxRoot, from, w.FxColor, flash, 0.07f);
        // Smoke at the muzzle (not inside the player's own cab, where it would only blind the pilot).
        if (flash > 0f && w.MuzzleSmoke != null && way.LengthSquared() > 1e-4f)
        {
            if (OneShotFx.Spawn(w.MuzzleSmoke, _fxRoot, from, 1.5f) is { } smoke) smoke.GlobalBasis = facing;
        }
        Fired?.Invoke(w);
        w.SmokeLeft = w.SmokeTime;
        PlaySound(w);
    }

    static void PlaySound(Weapon w)
    {
        if (w.FireSound == null) return;
        // A flamethrower roars for as long as it burns: its sound is not started afresh with every burst.
        if (w.Flame != null && w.FireSound.Playing) return;
        w.FireSound.PitchScale = (float)GD.RandRange(0.94, 1.06);
        w.FireSound.Play();
    }

    public void Update(float dt, Vector3 torsoAim, PhysicsDirectSpaceState3D space, ITarget lockTarget)
    {
        // An electromagnetic pulse knocks out the energy weapons for as long as it does the electronics.
        bool pulsed = Emp.Jammed(_owner);
        foreach (var w in Weapons)
        {
            bool jammed = pulsed && w.Energy;
            if (jammed && !w.Jammed) w.StopFiring();
            w.Jammed = jammed;
        }
        foreach (var w in Weapons)
        {
            Vector3 aimPoint = AimFor(w, torsoAim);
            if (w.BarrelSmoke != null)
            {
                // The barrel smokes after a shot, thinning out; it is the smoke left behind as the machine moves on.
                w.SmokeLeft = Mathf.Max(0f, w.SmokeLeft - dt);
                bool smoking = w.SmokeLeft > 0f && !w.Destroyed;
                if (w.BarrelSmoke.Emitting != smoking) w.BarrelSmoke.Emitting = smoking;
                if (smoking) w.BarrelSmoke.AmountRatio = Mathf.Clamp(w.SmokeLeft / Mathf.Max(w.SmokeTime, 0.01f) * 1.3f, 0.05f, 1f);
            }
            if (w.CooldownLeft > 0f) w.CooldownLeft -= dt;
            if (w.BeamLeft > 0f)
            {
                w.BeamLeft -= dt;
                if (w.BeamLeft <= 0f) w.StopFiring();
                else UpdateBeam(w, aimPoint, space, dt);
            }
            if (w.SwingLeft > 0f) UpdateSwing(w, dt, aimPoint, space);
            if (w.SalvoLeft > 0)
            {
                w.SalvoTimer -= dt;
                if (w.SalvoTimer > 0f) continue;
                w.SalvoTimer = w.SalvoInterval;
                w.SalvoLeft--;
                Launch(w, aimPoint, lockTarget);
            }
        }
    }

    /// <summary>
    /// A hand weapon's blow. The arm and the torso are swung by the machine itself (see Mech); here the
    /// weapon turns in the hand, and at the moment of the strike whatever is within reach in an arc in
    /// front of the shoulder is hit — there is no need to stand nose to nose with the target or to have
    /// the sight exactly on it.
    /// </summary>
    void UpdateSwing(Weapon w, float dt, Vector3 aimPoint, PhysicsDirectSpaceState3D space)
    {
        w.SwingLeft -= dt;
        float k = w.SwingPhase;
        // Back with the wind-up, through with the blow, slowly home.
        float f = k < Weapon.WindUp ? -0.35f * Mathf.SmoothStep(0f, 1f, k / Weapon.WindUp)
                : k < Weapon.Strike ? Mathf.Lerp(-0.35f, 1f, Mathf.SmoothStep(0f, 1f, (k - Weapon.WindUp) / (Weapon.Strike - Weapon.WindUp)))
                : 1f - Mathf.SmoothStep(0f, 1f, (k - Weapon.Strike) / (1f - Weapon.Strike));
        w.Rotation = w.RestRotation + new Vector3(f * w.SwingAngle, 0f, 0f);
        if (w.SwingLeft <= 0f) w.Rotation = w.RestRotation;

        if (w.SwingHit || k < Weapon.Strike - 0.06f) return;
        w.SwingHit = true;
        // From the shoulder, a fan of reaches towards where the pilot is looking.
        Node3D shoulder = (w.GetParent() as Node3D)?.GetParent() as Node3D ?? w;
        Vector3 from = shoulder.GlobalPosition;
        Vector3 dir = (aimPoint - from).Normalized();
        Phys.Hit best = default;
        float bestDist = float.MaxValue;
        foreach (float turn in new[] { 0f, -0.28f, 0.28f, -0.56f, 0.56f })
        foreach (float dip in new[] { 0f, -0.3f })
        {
            Vector3 reach = dir.Rotated(Vector3.Up, turn);
            reach = (reach + Vector3.Up * dip).Normalized();
            if (!Phys.Ray(space, from, from + reach * w.Range, Mask, _exclude, out var hit)) continue;
            // What stands in the middle of the arc is hit before what is at its edge.
            float d = from.DistanceTo(hit.Position) * (1f + Mathf.Abs(turn));
            if (d >= bestDist) continue;
            best = hit;
            bestDist = d;
        }
        if (bestDist == float.MaxValue) return;
        TimedFx.Flash(_fxRoot, best.Position, w.FxColor, 1.6f, 0.18f);
        Hurt(best, w.Damage * DamageScale);
        Impacts.Ground(_fxRoot, best, w.CraterSize, Mark.Bullet);
        Impacts.Unit(best, w.CraterSize * 0.6f, Mark.Bullet);
        Impacts.Sparks(_fxRoot, best);
    }

    void Launch(Weapon w, Vector3 aimPoint, ITarget lockTarget)
    {
        if (w.ProjectileScene == null) return;
        Vector3 from = w.GlobalPosition;
        // Each missile leaves from a different tube of the rack.
        Basis rack = w.GlobalTransform.Basis.Orthonormalized();
        from += rack.X * (float)GD.RandRange(-0.5, 0.5) * w.RackSize.X + rack.Y * (float)GD.RandRange(-0.5, 0.5) * w.RackSize.Y;
        Seen(from, aimPoint, out bool rackInView);
        Shot(w, from, rackInView ? 0.4f : 0f, aimPoint - from);

        var projectile = w.ProjectileScene.Instantiate<Projectile>();
        projectile.Shooter = _owner;
        Vector3 velocity = CarrierVelocity;
        if (w.Lobbed)
        {
            // A shell: thrown so as to come down on the point aimed at (as near as the gun can reach).
            velocity = Ballistics.Lay(from, aimPoint, w.MuzzleSpeed, w.ShellGravity, out _);
            if (w.Spread > 0f)
                velocity = velocity.Rotated(Vector3.Up, (float)GD.RandRange(-1.0, 1.0) * w.Spread) * (1f + (float)GD.RandRange(-1.0, 1.0) * w.Spread);
            if (projectile is Bomb shell) shell.Gravity = w.ShellGravity;
        }
        _fxRoot.AddChild(projectile);
        projectile.Launch(from, aimPoint, lockTarget, w.Damage * DamageScale, w.Range, Mask, _exclude, velocity);
    }

    void UpdateBeam(Weapon w, Vector3 aimPoint, PhysicsDirectSpaceState3D space, float dt)
    {
        Vector3 from = w.GlobalPosition;
        Vector3 end = Trace(w, from, aimPoint, space, out var hit, out bool didHit);
        if (w.BeamMesh != null)
        {
            w.BeamMesh.Visible = true;
            Geo.PlaceSegment(w.BeamMesh, Seen(from, end, out _), end, Vector3.Up);
        }
        if (w.Flame != null)
        {
            // The jet is thrown at the point aimed at (and not drawn inside the player's own cab).
            Vector3 nozzle = Seen(from, end, out _);
            if (nozzle.DistanceSquaredTo(end) > 0.25f)
                w.Flame.LookAtFromPosition(nozzle, end, Mathf.Abs((end - nozzle).Normalized().Y) > 0.95f ? Vector3.Right : Vector3.Up);
            w.SetFlame(true);
        }
        if (didHit && dt > 0f)
        {
            // What a beam destroys, it sets alight.
            Burning.Heat = true;
            Hurt(hit, w.Damage * DamageScale * dt / w.BeamTime);
            Burning.Heat = false;
            // A flamethrower cooks the machine it plays on: its reactor has that much more heat to get rid of.
            if (w.Ignites && hit.Collider is not HitBox and not Vehicle)
            {
                // The ground, a tree, a wall: it is left burning (a row of fires where the jet is swept along).
                w.KindleLeft -= dt;
                if (w.KindleLeft <= 0f)
                {
                    w.KindleLeft = 0.25f;
                    Burning.Kindle(_fxRoot, hit.Position + hit.Normal * 0.2f, 0.9f);
                }
            }
            // A vehicle caught in the jet catches fire itself, and goes on burning for a while. (Mechs don't:
            // the jet already cooks their reactor while it plays on them, see TargetHeat.)
            if (w.Afterburn > 0f && hit.Collider is Vehicle body)
                UnitFire.Ignite(body, hit.Position + hit.Normal * 0.3f, w.Afterburn);
            if (w.TargetHeat > 0f && hit.Collider is HitBox { Unit: Mech scorched })
                scorched.AddHeat(w.TargetHeat * DamageScale * dt / w.BeamTime);
            if (GD.Randf() < 0.35f) TimedFx.Flash(_fxRoot, end, w.FxColor, 0.45f, 0.08f);
            // A beam held on the ground scorches a line of marks; on water it boils the surface now and then.
            if (GD.Randf() < 0.06f) Impacts.Ground(_fxRoot, hit, w.CraterSize, Mark.Burn);
            if (GD.Randf() < 0.08f) Impacts.Unit(hit, w.CraterSize + 0.25f, Mark.Burn);
        }
        if (dt > 0f && GD.Randf() < 0.05f) Impacts.Water(_fxRoot, from, end, 0.6f);
    }

    Vector3 Trace(Weapon w, Vector3 from, Vector3 aimPoint, PhysicsDirectSpaceState3D space, out Phys.Hit hit, out bool didHit)
    {
        Vector3 dir = (aimPoint - from).Normalized();
        if (w.Spread > 0f)
            dir = (dir + new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * w.Spread).Normalized();
        Vector3 to = from + dir * w.Range;
        didHit = Phys.Ray(space, from, to, Mask, _exclude, out hit);
        return didHit ? hit.Position : to;
    }
}
