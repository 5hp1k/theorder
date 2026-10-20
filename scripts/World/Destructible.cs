using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// A prop that can be destroyed as a whole: trees topple, cacti shatter, cars blow up, an oil rig goes
/// down. Put it on the root StaticBody3D of the prop scene and set how it goes. A mech walking into it
/// destroys it outright. Its pieces may be <see cref="PropPart"/> children, each breaking off on its own
/// when shot; then the root needs no collision shape of its own.
/// Optional children: "Rubble" (hidden; left behind after a collapse or explosion).
/// </summary>
public partial class Destructible : StaticBody3D, IDamageable
{
    public enum Mode
    {
        /// <summary>Falls over away from whatever hit it, then sinks away (trees).</summary>
        Topple,
        /// <summary>Drops into the ground and leaves its Rubble (buildings).</summary>
        Collapse,
        /// <summary>Vanishes in its effect, leaving its Rubble if any (cacti, rocks).</summary>
        Burst,
        /// <summary>Blown into the air as a burnt-out wreck that comes down and lies there (cars).</summary>
        Wreck,
    }

    [Export] public float Hp = 20f;
    [Export] public Mode How = Mode.Topple;
    /// <summary>Ground area it covers (X, Z), for being walked through.</summary>
    [Export] public Vector2 Footprint = new(1.5f, 1.5f);
    /// <summary>Height of the thing: how far a collapse drops it.</summary>
    [Export] public float Height = 6f;
    /// <summary>Effect spawned when it goes (dust, splinters, explosion).</summary>
    [Export] public PackedScene DestroyFx;
    /// <summary>Whether a mech walking into it destroys it.</summary>
    [Export] public bool Trample = true;
    /// <summary>What a wreck is painted with.</summary>
    [Export] public Material CharredMaterial;

    // Coarse grid over the map so a mech only looks at the props around it.
    const float Cell = 40f;
    static readonly Dictionary<long, List<Destructible>> Grid = new();
    static readonly List<Destructible> Found = new();

    /// <summary>Set while something is flattening props by the hundred, so that they go without their effects.</summary>
    public static bool Quiet;

    Rect2 _area;
    bool _destroyed;
    /// <summary>Every destructible thing on the map, in the order they were made (the same on every computer of a network game).</summary>
    public static readonly List<Destructible> Everyone = new();

    static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    public override void _Ready()
    {
        Everyone.Add(this);
        // Props stand at right angles (or are round enough), so an axis-aligned rectangle will do.
        Vector3 p = GlobalPosition;
        bool turned = Mathf.Abs(GlobalTransform.Basis.X.Z) > 0.7f;
        Vector2 half = (turned ? new Vector2(Footprint.Y, Footprint.X) : Footprint) * 0.5f;
        _area = new Rect2(new Vector2(p.X, p.Z) - half, half * 2f);
        if (Trample) ForEachCell(_area, list => list.Add(this));
    }

    public override void _ExitTree()
    {
        Everyone.Remove(this);
        if (Trample) ForEachCell(_area, list => list.Remove(this));
    }

    static void ForEachCell(Rect2 area, System.Action<List<Destructible>> action)
    {
        int x0 = Mathf.FloorToInt(area.Position.X / Cell), x1 = Mathf.FloorToInt(area.End.X / Cell);
        int z0 = Mathf.FloorToInt(area.Position.Y / Cell), z1 = Mathf.FloorToInt(area.End.Y / Cell);
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            long key = Key(x, z);
            if (!Grid.TryGetValue(key, out var list)) Grid[key] = list = new List<Destructible>();
            action(list);
        }
    }

    /// <summary>Destroys every trample-able prop whose footprint a body of the given radius overlaps.</summary>
    /// <returns>How many went down.</returns>
    public static int TrampleAround(Vector3 position, float radius, Vector3 direction)
    {
        var body = new Rect2(position.X - radius, position.Z - radius, radius * 2f, radius * 2f);
        Found.Clear();
        ForEachCell(body, list =>
        {
            foreach (var d in list)
                if (!d._destroyed && d._area.Intersects(body) && !Found.Contains(d)) Found.Add(d);
        });
        foreach (var d in Found) d.Destroy(direction);
        // (and the trees of the woods, which are not props until they are touched)
        return Found.Count + ForestField.TrampleAround(position, radius, direction);
    }

    public void TakeDamage(float amount, Vector3 point)
    {
        if (_destroyed) return;
        Hp -= amount;
        if (Hp > 0f) return;
        Vector3 away = GlobalPosition - point;
        away.Y = 0f;
        Destroy(away);
    }

    public void DestroyWhole(Vector3 direction) => Destroy(direction);

    void Destroy(Vector3 direction)
    {
        if (_destroyed) return;
        NetLink.I?.World(1, Everyone.IndexOf(this), 0, direction, 0f);
        _destroyed = true;
        CollisionLayer = 0;
        foreach (Node child in GetChildren())
            if (child is PropPart part) part.Disable();

        // Burnt down by a beam: what's left of it goes on burning.
        Burning.Ignite(GetParent(), GlobalPosition + Vector3.Up * 0.3f, Mathf.Clamp(Mathf.Max(Footprint.X, Footprint.Y) * 0.6f, 0.7f, 1.6f));
        if (DestroyFx != null && !Quiet)
        {
            OneShotFx.Spawn(DestroyFx, GetParent(), GlobalPosition + Vector3.Up * Mathf.Min(Height * 0.3f, 4f));
        }

        var rubble = GetNodeOrNull<Node3D>("Rubble");
        var tween = CreateTween();
        switch (How)
        {
            case Mode.Topple:
                direction.Y = 0f;
                if (direction.LengthSquared() < 1e-4f) direction = Vector3.Forward;
                Vector3 axis = Vector3.Up.Cross(direction.Normalized()).Normalized();
                Basis start = GlobalBasis;
                tween.TweenMethod(Callable.From<float>(a => GlobalBasis = new Basis(axis, a) * start), 0f, 1.5f, 1.1f)
                     .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                tween.TweenInterval(8f);
                tween.TweenProperty(this, "position:y", Position.Y - 3f, 3f);
                tween.TweenCallback(Callable.From(QueueFree));
                break;

            case Mode.Collapse:
                if (rubble != null)
                {
                    // The rubble stays put while the building drops through it.
                    rubble.TopLevel = true;
                    rubble.GlobalPosition = GlobalPosition;
                    rubble.Visible = true;
                }
                tween.TweenProperty(this, "position:y", Position.Y - Height - 1f, 0.7f + Height * 0.035f)
                     .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                tween.TweenCallback(Callable.From(HideAllButRubble));
                break;

            case Mode.Wreck:
                tween.Kill();
                foreach (var mesh in Geo.Collect<MeshInstance3D>(this)) mesh.MaterialOverride = CharredMaterial;
                // Tossed by its own fuel tank.
                var toss = new Vector3((float)GD.RandRange(-3.0, 3.0), (float)GD.RandRange(6.0, 10.0), (float)GD.RandRange(-3.0, 3.0));
                foreach (Node child in GetChildren())
                    if (child is CollisionShape3D shape) shape.Disabled = true;
                Debris.Detach(this, GetParent(), toss, 1200f, 60f);
                break;

            case Mode.Burst:
                tween.Kill();
                if (rubble == null)
                {
                    QueueFree();
                    break;
                }
                rubble.Visible = true;
                HideAllButRubble();
                break;
        }
    }

    void HideAllButRubble()
    {
        foreach (Node child in GetChildren())
            if (child is Node3D n && child.Name != "Rubble") n.Visible = false;
    }
}
