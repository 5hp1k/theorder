using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// The gunner's view of a howitzer shot (scenes/fx/shell_arc.tscn, in game.tscn): while the player
/// drives something with a shell-throwing gun, the arc the shell will fly is drawn from the muzzle to
/// where it lands, with a ring the size of the blast round that spot. Computer gunners get no such
/// picture, and guided rockets need none. The range and time of flight go to the HUD's sight.
/// </summary>
public partial class ShellArc : Node3D
{
    /// <summary>Width of the drawn line, as a share of its distance from the camera (so it looks the same near and far).</summary>
    [Export] public float Width = 0.0035f;
    /// <summary>Thinnest the line gets close to the camera, metres.</summary>
    [Export] public float MinWidth = 0.06f;
    /// <summary>The shell will reach the point aimed at / it will fall short or strike something on the way.</summary>
    [Export] public Color ReachColor = new(1f, 0.75f, 0.2f, 0.9f);
    [Export] public Color ShortColor = new(1f, 0.25f, 0.2f, 0.9f);
    /// <summary>How far the ring floats above the ground it is drawn on.</summary>
    [Export] public float RingLift = 0.35f;

    // ── What the sight shows (see Reticle) ──────────────────────
    /// <summary>The player has a shell gun to aim right now.</summary>
    public static bool Active { get; private set; }
    /// <summary>Distance to the point the sight is on, metres: the rangefinder.</summary>
    public static float Range { get; private set; }
    /// <summary>Seconds the shell will be in the air.</summary>
    public static float FlightTime { get; private set; }
    /// <summary>Whether the shell lands where the sight is.</summary>
    public static bool OnTarget { get; private set; }
    public static string GunName { get; private set; }
    public static bool Loaded { get; private set; }

    const float Step = 0.1f;

    MeshInstance3D _line;
    ImmediateMesh _mesh;
    ShaderMaterial _material;
    readonly List<Vector3> _points = new();
    static readonly Dictionary<PackedScene, float> BlastRadii = new();

    public override void _Ready()
    {
        _line = GetNode<MeshInstance3D>("Line");
        _mesh = new ImmediateMesh();
        _line.Mesh = _mesh;
        _material = _line.MaterialOverride as ShaderMaterial;
        Active = false;
    }

    public override void _ExitTree() => Active = false;

    public override void _Process(double delta)
    {
        _mesh.ClearSurfaces();
        Active = false;
        if (Game.Cockpit is not PlayerPilot { Unit: { Offline: false } unit } pilot || pilot.Camera == null) return;
        Weapon gun = null;
        foreach (var w in unit.Weapons.Weapons)
        {
            if (!w.Lobbed || w.Destroyed || !w.HasAmmo) continue;
            gun = w;
            break;
        }
        if (gun == null) return;

        Vector3 muzzle = gun.GlobalPosition, aim = unit.AimPoint;
        Vector3 velocity = Ballistics.Lay(muzzle, aim, gun.MuzzleSpeed, gun.ShellGravity, out bool reachable);
        Ballistics.Path(muzzle, velocity, gun.ShellGravity, _points, Step);
        if (_points.Count < 2) return;
        Vector3 landing = _points[^1];

        Active = true;
        GunName = gun.DisplayName;
        Loaded = gun.IsReady;
        Range = muzzle.DistanceTo(aim);
        FlightTime = (_points.Count - 1) * Step;
        OnTarget = reachable && landing.DistanceTo(aim) < Mathf.Max(6f, Range * 0.04f);
        _material?.SetShaderParameter("tint", OnTarget ? ReachColor : ShortColor);

        Vector3 eye = pilot.Camera.GlobalPosition;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        float run = 0f;
        for (int i = 0; i < _points.Count; i++)
        {
            Vector3 p = _points[i];
            Vector3 along = (i + 1 < _points.Count ? _points[i + 1] - p : p - _points[i - 1]).Normalized();
            if (i > 0) run += p.DistanceTo(_points[i - 1]);
            Ribbon(p, along.Cross(eye - p), eye, run);
        }
        _mesh.SurfaceEnd();

        // The ring: where it lands and how far the blast reaches, laid over the ground.
        float radius = BlastOf(gun);
        var terrain = MapTerrain.Instance;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        const int Segments = 48;
        for (int i = 0; i <= Segments; i++)
        {
            float a = i * Mathf.Tau / Segments;
            Vector3 p = landing + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            if (terrain != null)
            {
                float floor = terrain.HeightAt(p.X, p.Z);
                if (terrain.HasWater) floor = Mathf.Max(floor, terrain.WaterLevel);
                p.Y = floor + RingLift;
            }
            Ribbon(p, Vector3.Up, eye, a * radius);
        }
        _mesh.SurfaceEnd();
    }

    void Ribbon(Vector3 p, Vector3 side, Vector3 eye, float run)
    {
        side = side.LengthSquared() > 1e-8f ? side.Normalized() : Vector3.Up;
        side *= Mathf.Max(MinWidth, eye.DistanceTo(p) * Width);
        _mesh.SurfaceSetUV(new Vector2(run, 0f));
        _mesh.SurfaceAddVertex(p - side);
        _mesh.SurfaceSetUV(new Vector2(run, 1f));
        _mesh.SurfaceAddVertex(p + side);
    }

    /// <summary>How far the gun's shell reaches when it bursts (read once from the shell's prefab).</summary>
    static float BlastOf(Weapon gun)
    {
        if (gun.ProjectileScene == null) return 10f;
        if (BlastRadii.TryGetValue(gun.ProjectileScene, out float radius)) return radius;
        var shell = gun.ProjectileScene.Instantiate();
        radius = shell is Bomb { BlastRadius: > 0f } bomb ? bomb.BlastRadius : 10f;
        shell.Free();
        return BlastRadii[gun.ProjectileScene] = radius;
    }
}
