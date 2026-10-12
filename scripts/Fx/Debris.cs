using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Physical wreckage: loose chunks thrown off by hits, and whole parts (an arm, a turret, a tree's crown)
/// torn off and left to fall. Chunks come from scenes/fx/debris_chunk.tscn.
/// </summary>
public static class Debris
{
    /// <summary>Most loose chunks alive at once; the oldest goes when a new one arrives.</summary>
    public const int MaxChunks = 110;

    static PackedScene _chunk;
    static readonly Queue<DebrisChunk> Chunks = new();

    /// <summary>Throws out a loose block of the given size and material.</summary>
    public static void Chunk(Node parent, Vector3 position, Vector3 size, Material material, Vector3 velocity)
    {
        _chunk ??= GD.Load<PackedScene>("res://scenes/fx/debris_chunk.tscn");
        var chunk = _chunk.Instantiate<DebrisChunk>();
        chunk.Setup(size, material);
        chunk.Transform = new Transform3D(new Basis(new Vector3(GD.Randf(), GD.Randf(), GD.Randf()).Normalized(), GD.Randf() * Mathf.Tau), position);
        chunk.LinearVelocity = velocity;
        chunk.AngularVelocity = new Vector3((float)GD.RandRange(-4.0, 4.0), (float)GD.RandRange(-4.0, 4.0), (float)GD.RandRange(-4.0, 4.0));
        parent.AddChild(chunk);

        Chunks.Enqueue(chunk);
        while (Chunks.Count > MaxChunks)
        {
            var old = Chunks.Dequeue();
            if (GodotObject.IsInstanceValid(old)) old.QueueFree();
        }
    }

    /// <summary>A handful of chunks flying away from a point (armour plates, masonry).</summary>
    public static void Burst(Node parent, Vector3 position, int count, float size, Material material, Vector3 push, float spread = 6f)
    {
        for (int i = 0; i < count; i++)
        {
            var dims = new Vector3((float)GD.RandRange(0.5, 1.0), (float)GD.RandRange(0.25, 0.7), (float)GD.RandRange(0.5, 1.0)) * size;
            var scatter = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(0.2, 1.2), (float)GD.RandRange(-1.0, 1.0)) * spread;
            Chunk(parent, position + scatter * 0.05f, dims, material, push + scatter);
        }
    }

    /// <summary>A part torn off its owner, now a body of its own. Keep it to put the part back later.</summary>
    public sealed class Detached
    {
        public RigidBody3D Body;
        public Node3D Part;
        Node _parent;
        int _index;
        Transform3D _local;

        internal Detached(Node3D part)
        {
            Part = part;
            _parent = part.GetParent();
            _index = part.GetIndex();
            _local = part.Transform;
        }

        /// <summary>Puts the part back where it was and removes the body.</summary>
        public void Restore()
        {
            if (!GodotObject.IsInstanceValid(Part)) return;
            Part.GetParent()?.RemoveChild(Part);
            _parent.AddChild(Part);
            _parent.MoveChild(Part, Mathf.Min(_index, _parent.GetChildCount() - 1));
            Part.Transform = _local;
            if (GodotObject.IsInstanceValid(Body)) Body.QueueFree();
        }
    }

    /// <summary>
    /// Tears a part off: it becomes a rigid body (sized from its meshes) flying with the given velocity.
    /// With a lifetime it removes itself — part and all — after that many seconds.
    /// </summary>
    public static Detached Detach(Node3D part, Node world, Vector3 velocity, float mass, float lifetime = -1f)
    {
        Aabb bounds = Geo.LocalBounds(part);
        var handle = new Detached(part);
        var body = new RigidBody3D
        {
            Name = "Torn" + part.Name,
            Mass = Mathf.Max(mass, 1f),
            GravityScale = 1.6f,
            AngularDamp = 0.4f,
            CollisionLayer = Phys.LayerWreck,
            CollisionMask = Phys.LayerWorld,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.9f, Bounce = 0.05f },
            LinearVelocity = velocity,
            AngularVelocity = new Vector3((float)GD.RandRange(-2.5, 2.5), (float)GD.RandRange(-1.5, 1.5), (float)GD.RandRange(-2.5, 2.5)),
        };
        world.AddChild(body);
        body.GlobalTransform = part.GlobalTransform.Orthonormalized();
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = (bounds.Size * 0.9f).Max(Vector3.One * 0.2f) },
            Position = bounds.GetCenter(),
        });
        part.Reparent(body, true);
        handle.Body = body;
        if (lifetime > 0f)
            body.GetTree().CreateTimer(lifetime, false).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(body)) body.QueueFree();
            };
        return handle;
    }

    /// <summary>Forget everything (a new battle).</summary>
    public static void Reset() => Chunks.Clear();
}
