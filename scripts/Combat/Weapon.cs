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
    [Export(PropertyHint.Range, "1,3")] public int Group = 1;
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

    [ExportGroup("Melee")]
    [Export] public float SwingTime = 0.5f;
    /// <summary>How far the weapon node pitches at the bottom of the swing, radians.</summary>
    [Export] public float SwingAngle = -1.8f;

    [ExportGroup("Missiles")]
    [Export] public PackedScene ProjectileScene;
    [Export] public int SalvoSize = 10;
    /// <summary>Seconds between missiles of one salvo.</summary>
    [Export] public float SalvoInterval = 0.07f;
    /// <summary>Size of the rack the salvo leaves from (each projectile from a random tube); zero for a single barrel.</summary>
    [Export] public Vector2 RackSize = new(1.1f, 0.9f);

    public int Ammo { get; set; }
    public bool Destroyed { get; set; }
    public float CooldownLeft { get; set; }
    public float BeamLeft { get; set; }
    public int SalvoLeft { get; set; }
    public float SalvoTimer { get; set; }
    public float SwingLeft { get; set; }
    public bool SwingHit { get; set; }
    public Vector3 RestRotation { get; private set; }
    public MeshInstance3D BeamMesh { get; private set; }
    public AudioStreamPlayer3D FireSound { get; private set; }

    public bool HasAmmo => Ammo != 0;
    public bool IsReady => !Destroyed && CooldownLeft <= 0f && BeamLeft <= 0f && SalvoLeft <= 0 && SwingLeft <= 0f && HasAmmo;
    public float ReadyFraction => Cooldown > 0f ? Mathf.Clamp(1f - CooldownLeft / Cooldown, 0f, 1f) : 1f;

    public override void _Ready()
    {
        Ammo = MaxAmmo;
        RestRotation = Rotation;
        BeamMesh = GetNodeOrNull<MeshInstance3D>("Beam");
        FireSound = GetNodeOrNull<AudioStreamPlayer3D>("FireSound");
    }

    public void StopFiring()
    {
        SwingLeft = 0f;
        Rotation = RestRotation;
        BeamLeft = 0f;
        SalvoLeft = 0;
        if (BeamMesh != null) BeamMesh.Visible = false;
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

    readonly Node _fxRoot;
    readonly Godot.Collections.Array<Rid> _exclude;

    public WeaponSystem(Node owner, Node fxRoot, Godot.Collections.Array<Rid> exclude)
    {
        _fxRoot = fxRoot;
        _exclude = exclude;
        Geo.Collect(owner, Weapons);
    }

    /// <summary>Fires every ready weapon in the group. Returns heat generated.</summary>
    public float FireGroup(int group, Vector3 aimPoint, PhysicsDirectSpaceState3D space)
    {
        float heat = 0f;
        foreach (var w in Weapons)
        {
            if (w.Group != group || !w.IsReady) continue;
            // No point swinging at something out of reach.
            if (w.Kind == WeaponKind.Melee && w.GlobalPosition.DistanceTo(aimPoint) > w.Range + 2f) continue;
            heat += Fire(w, aimPoint, space);
        }
        return heat;
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
                Shot(w, from, muzzle ? 0.5f : 0f);
                TimedFx.Tracer(_fxRoot, shown, end, w.FxColor, 0.06f);
                Impacts.Water(_fxRoot, from, end, 0.5f + w.CraterSize);
                if (didHit)
                {
                    TimedFx.Flash(_fxRoot, end, w.FxColor, 0.6f, 0.12f);
                    Phys.Damage(hit, w.Damage * DamageScale);
                    Impacts.Ground(_fxRoot, hit, w.CraterSize, w.CraterSize < 0.5f ? Mark.Bullet : Mark.Blast);
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

    void Shot(Weapon w, Vector3 from, float flash)
    {
        if (flash > 0f) TimedFx.Flash(_fxRoot, from, w.FxColor, flash, 0.07f);
        PlaySound(w);
    }

    static void PlaySound(Weapon w)
    {
        if (w.FireSound == null) return;
        w.FireSound.PitchScale = (float)GD.RandRange(0.94, 1.06);
        w.FireSound.Play();
    }

    public void Update(float dt, Vector3 aimPoint, PhysicsDirectSpaceState3D space, ITarget lockTarget)
    {
        foreach (var w in Weapons)
        {
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

    void UpdateSwing(Weapon w, float dt, Vector3 aimPoint, PhysicsDirectSpaceState3D space)
    {
        w.SwingLeft -= dt;
        float k = Mathf.Clamp(1f - w.SwingLeft / w.SwingTime, 0f, 1f);
        // Fast chop down, slower recovery.
        float f = k < 0.4f ? Mathf.SmoothStep(0f, 1f, k / 0.4f) : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.4f) / 0.6f);
        w.Rotation = w.RestRotation + new Vector3(f * w.SwingAngle, 0f, 0f);
        if (w.SwingLeft <= 0f) w.Rotation = w.RestRotation;

        if (w.SwingHit || k < 0.35f) return;
        w.SwingHit = true;
        Vector3 end = Trace(w, w.GlobalPosition, aimPoint, space, out var hit, out bool didHit);
        if (!didHit) return;
        TimedFx.Flash(_fxRoot, end, w.FxColor, 1.6f, 0.18f);
        Phys.Damage(hit, w.Damage * DamageScale);
        Impacts.Ground(_fxRoot, hit, w.CraterSize, Mark.Bullet);
    }

    void Launch(Weapon w, Vector3 aimPoint, ITarget lockTarget)
    {
        if (w.ProjectileScene == null) return;
        Vector3 from = w.GlobalPosition;
        // Each missile leaves from a different tube of the rack.
        Basis rack = w.GlobalTransform.Basis.Orthonormalized();
        from += rack.X * (float)GD.RandRange(-0.5, 0.5) * w.RackSize.X + rack.Y * (float)GD.RandRange(-0.5, 0.5) * w.RackSize.Y;
        Seen(from, aimPoint, out bool rackInView);
        Shot(w, from, rackInView ? 0.4f : 0f);

        var projectile = w.ProjectileScene.Instantiate<Projectile>();
        _fxRoot.AddChild(projectile);
        projectile.Launch(from, aimPoint, lockTarget, w.Damage * DamageScale, w.Range, Mask, _exclude, CarrierVelocity);
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
        if (didHit && dt > 0f)
        {
            // What a beam destroys, it sets alight.
            Burning.Heat = true;
            Phys.Damage(hit, w.Damage * DamageScale * dt / w.BeamTime);
            Burning.Heat = false;
            if (GD.Randf() < 0.35f) TimedFx.Flash(_fxRoot, end, w.FxColor, 0.45f, 0.08f);
            // A beam held on the ground scorches a line of marks; on water it boils the surface now and then.
            if (GD.Randf() < 0.06f) Impacts.Ground(_fxRoot, hit, w.CraterSize, Mark.Burn);
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
