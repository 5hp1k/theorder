using Godot;

namespace MechSim;

/// <summary>
/// Keeps the number of things drawn in check. A map is thousands of separate small meshes — street
/// furniture, air conditioners, parked cars, branches — and each costs a draw call in the picture, in
/// every shadow cascade and again in the water's mirror, however far away and however few pixels it
/// covers. So every prop is given a distance beyond which it is not drawn, by its size (a hydrant
/// vanishes long before a bus does), and the smallest are left out of the mirror and cast no shadow.
/// </summary>
public static class Lod
{
    /// <summary>Render layer of small detail: the main camera sees it, the water's mirror does not.</summary>
    public const uint DetailLayer = 2;

    /// <summary>
    /// Sets the draw distance of everything under a prop. metresPerMetre: how many metres away it stays
    /// visible per metre of its own size; nearest / farthest clamp the result.
    /// </summary>
    public static void Apply(Node root, float metresPerMetre = 45f, float nearest = 90f, float farthest = 700f)
    {
        var meshes = Geo.Collect<GeometryInstance3D>(root);
        float size = 0f;
        foreach (var g in meshes)
            if (g is MeshInstance3D { Mesh: { } mesh }) size = Mathf.Max(size, mesh.GetAabb().GetLongestAxisSize());
        if (size <= 0f) return;
        float range = Mathf.Clamp(size * metresPerMetre, nearest, farthest) * Mathf.Clamp(GameConfig.DrawDistance, 0.4f, 3f);
        bool detail = size < 5.5f;
        foreach (var g in meshes)
        {
            g.VisibilityRangeEnd = range;
            g.VisibilityRangeEndMargin = range * 0.08f;
            if (detail) g.Layers = DetailLayer;
            if (size < 1.3f) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }
}
