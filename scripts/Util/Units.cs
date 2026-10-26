using System.Collections.Generic;
using Godot;

namespace MechSim;

public enum Team { Player, Enemy }

/// <summary>Something that can be hunted: the AI aims at it, guided missiles home on it, the radar shows it.</summary>
public interface ITarget
{
    string DisplayName { get; }
    Vector3 GlobalPosition { get; }
    /// <summary>Point to aim at, world space.</summary>
    Vector3 AimCenter { get; }
    bool IsDestroyed { get; }
    Team Team { get; }
    /// <summary>What's left of it overall, 0..1.</summary>
    float Integrity { get; }
    /// <summary>Per-section damage, for units that have sections (mechs); otherwise null.</summary>
    DamageModel Damage { get; }
    /// <summary>Shows or hides it on a dedicated render layer (the HUD's target window).</summary>
    void SetPreview(bool on, int layer);
}

/// <summary>
/// A machine a pilot can drive: a mech or a vehicle. The pilot (player or AI) sets the commands each
/// tick and reads the state back; the machine does the rest.
/// </summary>
public interface IDrivable : ITarget
{
    // ── The machine ─────────────────────────────────────────────
    CharacterBody3D Body { get; }
    /// <summary>Where the gunner sits: origin and -Z are the line the weapons converge along.</summary>
    Node3D AimAnchor { get; }
    /// <summary>Cockpit interior shown only in first-person view; may be null.</summary>
    Node3D CockpitInterior { get; }
    /// <summary>Point the chase camera orbits.</summary>
    Vector3 ViewPivot { get; }
    /// <summary>Scales the chase camera's distance to the size of the machine.</summary>
    float ChaseScale { get; }
    /// <summary>World yaw of the hull (legs).</summary>
    float HullYaw { get; }

    // ── State ───────────────────────────────────────────────────
    float Speed { get; }
    float MaxForwardSpeed { get; }
    float ReverseFraction { get; }
    float ThrottleRate { get; }
    bool HasHeat { get; }
    float Heat { get; }
    bool IsShutdown { get; }
    float RestartHeat { get; }
    float RestartProgress { get; }
    bool Offline { get; }
    float RespawnLeft { get; }
    /// <summary>Turret or torso angle relative to the hull.</summary>
    float TorsoYaw { get; }
    float TorsoPitch { get; }
    /// <summary>Turret travel each way; π or more means it turns all the way round.</summary>
    float MaxTwist { get; }
    float MinPitch { get; }
    float MaxPitch { get; }
    float HeadingDeg { get; }
    bool HasJumpJets { get; }
    float JumpFuel { get; }
    bool Thrusting { get; }
    /// <summary>Something is in the way (a wall, a shore).</summary>
    bool Blocked { get; }
    float Time { get; }
    WeaponSystem Weapons { get; }
    GodotObject AimCollider { get; }
    Vector3 AimPoint { get; }

    // ── Commands ────────────────────────────────────────────────
    float Throttle { get; set; }
    /// <summary>Steering, -1..1, positive = left.</summary>
    float TurnInput { get; set; }
    /// <summary>Where the turret should point, relative to the hull / the horizon (radians).</summary>
    float TargetYaw { get; set; }
    float TargetPitch { get; set; }
    /// <summary>Weapon groups fired this tick, one bit per group (<see cref="WeaponSystem.Bit"/>).</summary>
    int Fire { get; set; }
    /// <summary>Jump jets; climb, for aircraft.</summary>
    bool Jump { get; set; }
    /// <summary>Descend, for aircraft.</summary>
    bool Descend { get; set; }
    ITarget LockTarget { get; set; }
    /// <summary>Camera shake accumulated from footfalls and hits; the pilot's view consumes it.</summary>
    float Shake { get; set; }
}

/// <summary>
/// What the HUD shows: whichever seat the player is in right now (a mech's or vehicle's pilot, or a
/// soldier's own eyes). <see cref="Game.Cockpit"/> is the current one.
/// </summary>
public interface ICockpit
{
    string UnitName { get; }
    Vector3 WorldPosition { get; }
    float HullYaw { get; }
    float Throttle { get; }
    float ReverseFraction { get; }
    /// <summary>Signed speed as a fraction of top speed.</summary>
    float SpeedFraction { get; }
    float SpeedKmh { get; }
    bool HasHeat { get; }
    float Heat { get; }
    bool IsShutdown { get; }
    /// <summary>The pilot has switched the reactor off (or, in the simulator, has yet to start it again after a shutdown).</summary>
    bool ReactorOff => false;
    /// <summary>The reactor has been switched on again and is starting up.</summary>
    bool ReactorStarting => false;
    /// <summary>The engine (the drive of the legs) is switched off, or spinning up again (and how far it is).</summary>
    bool EngineOff => false;
    bool EngineStarting => false;
    float EngineProgress => 1f;
    /// <summary>Coolant flushes left and carried (0 carried: the machine has none).</summary>
    int Coolant => 0;
    int CoolantMax => 0;
    float RestartHeat { get; }
    float RestartProgress { get; }
    float Twist { get; }
    float MaxTwist { get; }
    /// <summary>Where the torso or the guns are tipped, up or down, and how far they can go (radians).</summary>
    float Pitch { get; }
    float MinPitch { get; }
    float MaxPitch { get; }
    /// <summary>The torso turns back against the legs' turning (see <see cref="PlayerPilot.CounterRotate"/>).</summary>
    bool CounterRotate { get; }
    float HeadingDeg { get; }
    WeaponSystem Weapons { get; }
    DamageModel Damage { get; }
    float Integrity { get; }
    bool HasJump { get; }
    float JumpFuel { get; }
    bool Thrusting { get; }
    bool IsDestroyed { get; }
    bool Offline { get; }
    float RespawnLeft { get; }
    Camera3D Camera { get; }
    bool FirstPerson { get; }
    bool FreeLook { get; }
    float Magnification { get; }
    Vector3 AimPoint { get; }
    GodotObject AimCollider { get; }
    /// <summary>
    /// Where the guns are getting to, when it isn't where the sight is: a mech's arms reaching ahead of its
    /// torso, a turret still turning to where the mouse sent it. Null when it's the same.
    /// </summary>
    Vector3? LeadAim { get; }
    /// <summary>Whether this seat has a target lock (E/T/Q).</summary>
    bool CanLock { get; }
    ITarget Target { get; }
    /// <summary>Missile lock on the target: builds from 0 to 1 while the sight is held on it.</summary>
    float LockProgress { get; }
    /// <summary>A line of advice to show under the crosshair, or null.</summary>
    string Prompt { get; }
}

/// <summary>Everyone on the battlefield, and who shoots whom.</summary>
public static class Units
{
    public static readonly List<ITarget> All = new();

    public static bool Hostile(ITarget a, ITarget b)
        => a != b && (Net.FreeForAll || a.Team != b.Team || (a.Team == Team.Enemy && BotsFight));

    /// <summary>The enemy's machines fight one another too (a battle set up so; never in a mission).</summary>
    static bool BotsFight => GameConfig.BotsFight && CampaignRun.Mission == null;

    /// <summary>What a unit's shots can hit. Its own hit boxes must be on its exclude list.</summary>
    /// <param name="human">The player's own guns can hit their own side too (computer pilots of the squad never do).</param>
    public static uint FireMask(Team team, bool human = false)
    {
        if (Net.FreeForAll) return Phys.MaskPlayerFire | Phys.MaskEnemyFire | Phys.LayerHitPlayer | Phys.LayerHitEnemy;
        if (team == Team.Player) return human ? Phys.MaskPlayerFire | Phys.LayerHitPlayer : Phys.MaskPlayerFire;
        return BotsFight ? Phys.MaskEnemyFire | Phys.LayerHitEnemy : Phys.MaskEnemyFire;
    }

    /// <summary>The unit a shot landed on, whether it hit a section box or the hull itself.</summary>
    public static ITarget FromCollider(GodotObject collider)
        => collider is HitBox box ? box.Unit as ITarget : collider as ITarget;

    /// <summary>Shows a picture on every plate named "Insignia…" under the unit, or hides the plates.</summary>
    /// <summary>The emblem on the machine's own emblem plates (those of its scene; <paramref name="plates"/> off — none).</summary>
    public static void ApplyInsignia(Node root, Texture2D picture, bool plates = true)
    {
        foreach (var mesh in Geo.Collect<MeshInstance3D>(root))
        {
            if (!mesh.Name.ToString().StartsWith("Insignia")) continue;
            mesh.Visible = picture != null && plates;
            if (picture == null || mesh.MaterialOverride is not StandardMaterial3D std) continue;
            var copy = (StandardMaterial3D)std.Duplicate();
            copy.AlbedoTexture = picture;
            mesh.MaterialOverride = copy;
        }
    }

    public static void SetPreview(Node root, bool on, int layer)
    {
        foreach (var mesh in Geo.Collect<MeshInstance3D>(root))
            mesh.SetLayerMaskValue(layer, on);
    }

}
