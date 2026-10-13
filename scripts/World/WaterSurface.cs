using Godot;

namespace MechSim;

/// <summary>
/// The water plane of a map. Its look is the shader in materials/water.gdshader; this feeds the shader
/// two things: the ripples (rings spreading from wherever something hit or waded through the water),
/// and the mirror image of the world, rendered each frame by the "Reflection" viewport's camera placed
/// where the player's camera would be if it were under the water looking up.
/// </summary>
public partial class WaterSurface : MeshInstance3D
{
    const int Max = 24;

    /// <summary>Size of the mirror image relative to the screen: smaller is cheaper and blurrier.</summary>
    [Export(PropertyHint.Range, "0.2,1")] public float ReflectionScale = 0.5f;

    static WaterSurface _instance;

    readonly Vector3[] _spots = new Vector3[Max]; // x, z, time it started
    readonly float[] _strength = new float[Max];
    int _next, _count;
    float _clock;
    ShaderMaterial _material;
    SubViewport _mirror;
    Camera3D _mirrorCamera;

    public override void _EnterTree() => _instance = this;

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    public override void _Ready()
    {
        _material = MaterialOverride as ShaderMaterial;
        _mirror = GetNodeOrNull<SubViewport>("Reflection");
        _mirrorCamera = GetNodeOrNull<Camera3D>("Reflection/Camera");
        if (_mirror != null) _material?.SetShaderParameter("reflection_tex", _mirror.GetTexture());
    }

    /// <summary>Starts a ring of ripples at a point on the water. Strength ~1 for a footfall, more for explosions.</summary>
    public static void AddRipple(Vector3 at, float strength)
    {
        var w = _instance;
        if (w == null || !IsInstanceValid(w)) return;
        w._spots[w._next] = new Vector3(at.X, at.Z, w._clock);
        w._strength[w._next] = strength;
        w._next = (w._next + 1) % Max;
        w._count = Mathf.Min(w._count + 1, Max);
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        if (_material == null) return;
        Color day = MapLighting.Daylight;
        _material.SetShaderParameter("daylight", new Vector3(day.R, day.G, day.B));
        _material.SetShaderParameter("clock", _clock);
        _material.SetShaderParameter("ripple_count", _count);
        _material.SetShaderParameter("ripple_pos", _spots);
        _material.SetShaderParameter("ripple_amp", _strength);
        UpdateMirror();
    }

    void UpdateMirror()
    {
        if (_mirror == null || _mirrorCamera == null) return;
        var eye = GetViewport().GetCamera3D();
        float level = GlobalPosition.Y;
        // No mirror on the lowest effects setting, with the water hidden, or from under it.
        bool on = Visible && eye != null && GameConfig.WaterMirror && eye.GlobalPosition.Y > level;
        _mirror.RenderTargetUpdateMode = on ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _material.SetShaderParameter("use_reflection", on);
        if (!on) return;

        Vector2 screen = GetViewport().GetVisibleRect().Size * ReflectionScale;
        var size = new Vector2I(Mathf.Max(64, (int)screen.X), Mathf.Max(64, (int)screen.Y));
        if (_mirror.Size != size) _mirror.Size = size;

        // The eye, flipped through the water's surface. Flipping all three axes would turn the camera
        // inside out, so one of them is flipped back: the picture comes out mirrored left to right,
        // which the shader undoes.
        Transform3D t = eye.GlobalTransform;
        static Vector3 Flip(Vector3 v) => new(v.X, -v.Y, v.Z);
        var basis = new Basis(-Flip(t.Basis.X), Flip(t.Basis.Y), Flip(t.Basis.Z));
        _mirrorCamera.GlobalTransform = new Transform3D(basis, new Vector3(t.Origin.X, 2f * level - t.Origin.Y, t.Origin.Z));
        _mirrorCamera.Fov = eye.Fov;
        _mirrorCamera.Near = eye.Near;
        _mirrorCamera.Far = eye.Far;
    }
}
