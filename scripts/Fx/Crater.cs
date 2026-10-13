using Godot;

namespace MechSim;

/// <summary>
/// A mark on the ground (scenes/fx/mark_*.tscn): the "Mark" plane is laid over the terrain so it follows
/// slopes and the bowl of a crater, glows while hot, stays for a while, then weathers away and frees itself.
/// </summary>
public partial class Crater : Node3D
{
    [Export] public float Lifetime = 90f;
    [Export] public float FadeTime = 10f;
    /// <summary>Seconds the embers take to die (0: none).</summary>
    [Export] public float GlowTime;
    /// <summary>How far above the ground the mark lies.</summary>
    [Export] public float Lift = 0.09f;

    MeshInstance3D _mark;
    Godot.Collections.Array _flat;
    Vector3[] _rest;

    public override void _Ready()
    {
        _mark = GetNodeOrNull<MeshInstance3D>("Mark");
        if (_mark == null) return;
        _mark.SetInstanceShaderParameter("seed", GD.Randf() * 100f);
        _mark.SetInstanceShaderParameter("fade", 1f);
        _mark.SetInstanceShaderParameter("heat", GlowTime > 0f ? 1f : 0f);
        if (_mark.Mesh != null)
        {
            _flat = _mark.Mesh.SurfaceGetArrays(0);
            _rest = _flat[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Drape();
            MapTerrain.Reshaped += Drape;
        }

        if (GlowTime > 0f)
            CreateTween().TweenMethod(Callable.From<float>(v => _mark.SetInstanceShaderParameter("heat", v)), 1f, 0f, GlowTime)
                         .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        var tween = CreateTween();
        tween.TweenInterval(Mathf.Max(0.1f, Lifetime - FadeTime));
        tween.TweenMethod(Callable.From<float>(v => _mark.SetInstanceShaderParameter("fade", v)), 1f, 0f, FadeTime);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    public override void _ExitTree() => MapTerrain.Reshaped -= Drape;

    /// <summary>Drops every vertex of the plane onto the ground under it.</summary>
    void Drape()
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null || _rest == null || !IsInsideTree()) return;
        Transform3D t = _mark.GlobalTransform;
        float scale = Mathf.Max(t.Basis.Y.Length(), 1e-3f);
        var verts = new Vector3[_rest.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 world = t * new Vector3(_rest[i].X, 0f, _rest[i].Z);
            verts[i] = new Vector3(_rest[i].X, (terrain.MeshHeightAt(world.X, world.Z) + Lift - t.Origin.Y) / scale, _rest[i].Z);
        }
        var arrays = _flat.Duplicate();
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _mark.Mesh = mesh;
    }
}
