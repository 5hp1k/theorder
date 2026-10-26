using Godot;

namespace MechSim;

/// <summary>
/// The edge of the battlefield (<see cref="MapTerrain.PlayHalf"/>) shown as a glowing curtain standing on
/// the ground all round it: always in the mission editor, in a battle only while the console's free
/// camera flies (<see cref="OnlyFreeCamera"/>). Put it under the map; it builds itself once the map's
/// terrain is there.
/// </summary>
public partial class BoundsWall : MeshInstance3D
{
    /// <summary>Shown only while the console's free camera is on.</summary>
    [Export] public bool OnlyFreeCamera;
    /// <summary>How high it rises over the ground, metres.</summary>
    [Export] public float Height = 45f;
    /// <summary>Distance between its columns along the edge, metres.</summary>
    [Export] public float Step = 6f;

    public override void _Ready()
    {
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://materials/bounds_wall.gdshader") };
        Visible = !OnlyFreeCamera;
        Callable.From(Build).CallDeferred();
    }

    void Build()
    {
        if (MapTerrain.Instance is not { } terrain || !IsInstanceValid(terrain)) return;
        float h = terrain.PlayHalf;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector2[] corners = { new(-h, -h), new(h, -h), new(h, h), new(-h, h) };
        float along = 0f;
        for (int e = 0; e < 4; e++)
        {
            Vector2 a = corners[e], b = corners[(e + 1) % 4];
            int n = Mathf.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / Step));
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = a.Lerp(b, (float)i / n), p1 = a.Lerp(b, (float)(i + 1) / n);
                float u0 = along + a.DistanceTo(b) * i / n, u1 = along + a.DistanceTo(b) * (i + 1) / n;
                Vector3 g0 = new(p0.X, terrain.HeightAt(p0.X, p0.Y) - 1f, p0.Y), g1 = new(p1.X, terrain.HeightAt(p1.X, p1.Y) - 1f, p1.Y);
                Vector3 t0 = g0 + Vector3.Up * Height, t1 = g1 + Vector3.Up * Height;
                void V(Vector3 v, float u, float up) { st.SetUV(new Vector2(u, up)); st.AddVertex(v); }
                V(g0, u0, 0f); V(t0, u0, 1f); V(t1, u1, 1f);
                V(g0, u0, 0f); V(t1, u1, 1f); V(g1, u1, 0f);
            }
            along += a.DistanceTo(b);
        }
        Mesh = st.Commit();
        // (it stands where the map is, whatever its parent's place)
        GlobalTransform = Transform3D.Identity;
    }

    public override void _Process(double delta)
    {
        if (OnlyFreeCamera) Visible = FreeCamera.Current != null;
    }
}
