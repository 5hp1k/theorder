using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// What a gauss slug leaves behind (scenes/fx/gauss_trail.tscn): a streak along the shot that is gone
/// at once and a spiral round it that widens and breaks up (the looks are in the shaders,
/// materials/fx/gauss_*.gdshader). Finished ones are kept and used again.
/// </summary>
public partial class GaussTrail : Node3D
{
    /// <summary>Seconds it lasts (as long as the spiral's own life in its shader).</summary>
    [Export] public float Lifetime = 1.8f;

    float _age;
    GeometryInstance3D[] _parts;
    PackedScene _source;
    static readonly Dictionary<PackedScene, Stack<GaussTrail>> Shelves = new();
    const int ShelfSize = 16;

    /// <summary>Forget the kept ones (a new battle: they went with the old one).</summary>
    public static void Reset() => Shelves.Clear();

    public static void Spawn(PackedScene scene, Node parent, Vector3 from, Vector3 to, Color color)
    {
        if (!Shelves.TryGetValue(scene, out var shelf)) Shelves[scene] = shelf = new Stack<GaussTrail>();
        GaussTrail trail = null;
        while (shelf.Count > 0 && trail == null)
        {
            trail = shelf.Pop();
            if (!IsInstanceValid(trail) || trail.IsQueuedForDeletion()) trail = null;
        }
        if (trail != null)
        {
            if (trail.GetParent() != parent) trail.Reparent(parent, false);
            trail.Visible = true;
            trail.SetProcess(true);
        }
        else
        {
            trail = scene.Instantiate<GaussTrail>();
            trail._source = scene;
            parent.AddChild(trail);
        }
        trail._age = 0f;
        Geo.PlaceSegment(trail, from, to, Vector3.Up);
        foreach (var part in trail.Parts())
        {
            part.SetInstanceShaderParameter("age", 0f);
            part.SetInstanceShaderParameter("length", from.DistanceTo(to));
            part.SetInstanceShaderParameter("tint", color);
        }
    }

    GeometryInstance3D[] Parts()
    {
        if (_parts == null)
        {
            var list = new List<GeometryInstance3D>();
            foreach (Node child in GetChildren())
                if (child is GeometryInstance3D g) list.Add(g);
            _parts = list.ToArray();
        }
        return _parts;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age < Lifetime)
        {
            foreach (var part in Parts()) part.SetInstanceShaderParameter("age", _age);
            return;
        }
        if (_source == null || !Shelves.TryGetValue(_source, out var shelf) || shelf.Count >= ShelfSize)
        {
            QueueFree();
            return;
        }
        Visible = false;
        SetProcess(false);
        shelf.Push(this);
    }
}
