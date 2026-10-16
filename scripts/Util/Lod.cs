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

    // ── Attention ───────────────────────────────────────────────
    // Much of what a machine does every tick is only for the eye: wheels riding the bumps, tracks laid
    // round them link by link, legs bending through a stride. Nobody can tell whether that is done 60
    // times a second for a tank 400 m off or behind the camera, but the processor can.

    /// <summary>Within this distance of the camera (metres, at the normal field of view) everything in view is animated every tick.</summary>
    public const float NearRange = 90f;
    /// <summary>Within this, every other tick; beyond it, or out of view, every fourth.</summary>
    public const float MidRange = 260f;

    static ulong _viewKey = ulong.MaxValue;
    static Vector3 _eye;
    static float _zoom = 1f;
    static bool _hasView;
    static readonly Plane[] Frustum = new Plane[6];

    static void LookAround()
    {
        // Once a tick (and once a frame): where the camera is and what it sees.
        ulong key = Engine.GetPhysicsFrames() * 1_000_003UL + Engine.GetProcessFrames();
        if (key == _viewKey) return;
        _viewKey = key;
        _hasView = false;
        if (Engine.GetMainLoop() is not SceneTree { Root: { } root } || root.GetCamera3D() is not { } camera) return;
        _eye = camera.GlobalPosition;
        // Zoomed in, far things are big on the screen.
        _zoom = Mathf.Max(1f, Mathf.Tan(Mathf.DegToRad(37.5f)) / Mathf.Tan(Mathf.DegToRad(Mathf.Max(camera.Fov, 1f) * 0.5f)));
        var planes = camera.GetFrustum();
        if (planes.Count < 6) return;
        for (int i = 0; i < 6; i++) Frustum[i] = planes[i];
        _hasView = true;
    }

    /// <summary>Whether a sphere is (at least partly) in the camera's view.</summary>
    public static bool InView(Vector3 centre, float radius)
    {
        LookAround();
        if (!_hasView) return true;
        // (The frustum's planes face outwards.)
        foreach (var plane in Frustum)
            if (plane.DistanceTo(centre) > radius) return false;
        return true;
    }

    /// <summary>Whether something of this size here can be seen: in view and within the range, or close by whichever way the camera looks.</summary>
    public static bool Visible(Vector3 centre, float radius, float range)
    {
        LookAround();
        if (!_hasView) return true;
        float d = (centre - _eye).Length();
        if (d < radius + 25f) return true;
        return d / _zoom < range && InView(centre, radius);
    }

    /// <summary>
    /// How often a machine driven by the computer is simulated: every tick, or every other tick (over
    /// twice the time) when it is far from the camera or out of view a little way off. The player's own,
    /// the one in the target window and everything close by in view always get every tick.
    /// </summary>
    public static int SimInterval(ITarget unit, bool computerDriven)
    {
        if (!computerDriven || unit == Game.PlayerUnit || (Game.Cockpit is { } seat && seat.Target == unit)) return 1;
        LookAround();
        if (!_hasView) return 1;
        Vector3 c = unit.AimCenter;
        float d = (c - _eye).Length();
        if (d < SimNear) return 1;
        return !InView(c, 8f) || d / _zoom > SimFar ? 2 : 1;
    }

    /// <summary>Within this distance of the camera machines are simulated every tick whichever way it looks.</summary>
    public const float SimNear = 70f;
    /// <summary>Beyond this (in view, at the normal field of view), every other tick.</summary>
    public const float SimFar = 220f;

    /// <summary>The same for a machine: the player's own and the one in the target window are always looked at closely.</summary>
    public static int Interval(ITarget unit, float radius)
    {
        if (unit == Game.PlayerUnit || (Game.Cockpit is { } seat && seat.Target == unit)) return 1;
        return Interval(unit.AimCenter, radius);
    }

    /// <summary>
    /// How many ticks may pass between two updates of what is only for show on a thing of this size
    /// here: 1 near and in view, 2 a little further, 4 far away or out of view.
    /// </summary>
    public static int Interval(Vector3 centre, float radius)
    {
        LookAround();
        if (!_hasView) return 1;
        float d = (centre - _eye).Length() / _zoom;
        if (d < radius * 2f) return 1;
        if (!InView(centre, radius)) return 4;
        return d < NearRange ? 1 : d < MidRange ? 2 : 4;
    }

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
