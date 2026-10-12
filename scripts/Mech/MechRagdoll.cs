using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Turns a destroyed mech into a physical wreck. On <see cref="Activate"/> the body and each leg bone
/// become rigid bodies (sized from the meshes they carry) linked by joints that bend the way the leg
/// does: a limited ball at the hip, hinges across the knee, heel and ankle. <see cref="Deactivate"/>
/// puts every part back where the scene had it, ready for the IK again.
/// </summary>
public partial class MechRagdoll : Node
{
    /// <summary>Mass of the whole mech, kg.</summary>
    [Export] public float TotalMass = 50000f;
    /// <summary>Share of the mass in the hull (the rest is split over the legs).</summary>
    [Export(PropertyHint.Range, "0.3,0.9")] public float HullMassShare = 0.55f;
    /// <summary>Makes the fall feel heavy rather than floaty at this scale.</summary>
    [Export] public float GravityScale = 1.8f;
    [Export] public float AngularDamp = 0.35f;
    /// <summary>How hard the hull is tipped over at the moment of death, rad/s.</summary>
    [Export] public float ToppleSpeed = 1.3f;

    [ExportGroup("Joint limits (radians, each way from the pose at death)")]
    [Export] public float HipSwing = 1.0f;
    [Export] public float HipTwist = 0.25f;
    [Export] public float KneeBend = 1.3f;
    [Export] public float HeelBend = 1.1f;
    [Export] public float AnkleBend = 0.8f;

    [ExportGroup("Reactor blast")]
    /// <summary>Speed the blast adds to the hull (up) and the limbs (outward), m/s.</summary>
    [Export] public float BlastHullSpeed = 3.5f;
    [Export] public float BlastLimbSpeed = 6f;

    public bool Active => _bodies.Count > 0;

    struct Moved
    {
        public Node3D Node;
        public Node Parent;
        public int Index;
        public Transform3D Local;
    }

    readonly List<RigidBody3D> _bodies = new();
    readonly List<Joint3D> _joints = new();
    readonly List<Moved> _moved = new();
    RigidBody3D _hull;

    public void Activate(Mech mech, Vector3 velocity)
    {
        if (Active) return;
        Node world = mech.GetParent();
        Vector3 right = mech.GlobalTransform.Basis.X.Normalized();
        float limbMass = TotalMass * (1f - HullMassShare) / 2f;

        _hull = MakeBody(world, "WreckHull", mech.Hips, TotalMass * HullMassShare, 0.85f, velocity);
        // A shove so it keels over instead of balancing on dead legs: mostly forward or back, a little sideways.
        Vector3 tip = right * (GD.Randf() < 0.5f ? -1f : 1f) + mech.GlobalTransform.Basis.Z * (float)GD.RandRange(-0.5, 0.5);
        _hull.AngularVelocity = tip.Normalized() * ToppleSpeed;

        foreach (var leg in mech.Legs)
        {
            var pose = leg.Pose;
            var thigh = MakeBody(world, "WreckThigh", leg.Thigh, limbMass * 0.36f, 1f, velocity, leg.HipJoint);
            var shin = MakeBody(world, "WreckShin", leg.Shin, limbMass * 0.28f, 1f, velocity, leg.KneeJoint);
            var meta = MakeBody(world, "WreckMeta", leg.Meta, limbMass * 0.14f, 1f, velocity, leg.HeelJoint);
            var foot = MakeBody(world, "WreckFoot", leg.Foot, limbMass * 0.22f, 1f, velocity);

            var hip = new ConeTwistJoint3D();
            Link(world, hip, pose.Hip, right, _hull, thigh);
            hip.SetParam(ConeTwistJoint3D.Param.SwingSpan, HipSwing);
            hip.SetParam(ConeTwistJoint3D.Param.TwistSpan, HipTwist);

            Hinge(world, pose.Knee, right, thigh, shin, KneeBend);
            Hinge(world, pose.Heel, right, shin, meta, HeelBend);
            Hinge(world, pose.Ankle, right, meta, foot, AnkleBend);
        }
    }

    /// <summary>The reactor goes: throws the hull up and the limbs outward.</summary>
    public void Blast(Vector3 center)
    {
        foreach (var body in _bodies)
        {
            body.Sleeping = false;
            if (body == _hull)
            {
                body.LinearVelocity += Vector3.Up * BlastHullSpeed;
                continue;
            }
            Vector3 away = body.GlobalPosition - center;
            away.Y = Mathf.Abs(away.Y) * 0.5f + 0.5f;
            body.LinearVelocity += away.Normalized() * BlastLimbSpeed;
        }
    }

    public void Deactivate()
    {
        // Parts first (in reverse, so sibling order comes back), then the physics scaffolding.
        for (int i = _moved.Count - 1; i >= 0; i--)
        {
            var m = _moved[i];
            m.Node.GetParent().RemoveChild(m.Node);
            m.Parent.AddChild(m.Node);
            m.Parent.MoveChild(m.Node, Mathf.Min(m.Index, m.Parent.GetChildCount() - 1));
            m.Node.Transform = m.Local;
        }
        _moved.Clear();
        foreach (var joint in _joints) joint.QueueFree();
        foreach (var body in _bodies) body.QueueFree();
        _joints.Clear();
        _bodies.Clear();
        _hull = null;
    }

    RigidBody3D MakeBody(Node world, string name, Node3D part, float mass, float shrink, Vector3 velocity, Node3D extra = null)
    {
        Aabb bounds = Geo.LocalBounds(part);
        var body = new RigidBody3D
        {
            Name = name,
            Mass = mass,
            GravityScale = GravityScale,
            AngularDamp = AngularDamp,
            LinearDamp = 0.05f,
            CollisionLayer = Phys.LayerWreck,
            CollisionMask = Phys.LayerWorld | Phys.LayerWreck,
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.9f, Bounce = 0f },
            LinearVelocity = velocity,
        };
        world.AddChild(body);
        body.GlobalTransform = part.GlobalTransform.Orthonormalized();
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = bounds.Size * shrink },
            Position = bounds.GetCenter(),
        });
        _bodies.Add(body);

        Adopt(part, body);
        if (extra != null) Adopt(extra, body);
        return body;
    }

    void Adopt(Node3D node, Node3D body)
    {
        _moved.Add(new Moved { Node = node, Parent = node.GetParent(), Index = node.GetIndex(), Local = node.Transform });
        node.Reparent(body, true);
    }

    void Hinge(Node world, Vector3 at, Vector3 axis, RigidBody3D a, RigidBody3D b, float bend)
    {
        var hinge = new HingeJoint3D();
        Link(world, hinge, at, axis, a, b);
        hinge.SetFlag(HingeJoint3D.Flag.UseLimit, true);
        hinge.SetParam(HingeJoint3D.Param.LimitLower, -bend);
        hinge.SetParam(HingeJoint3D.Param.LimitUpper, bend);
    }

    /// <summary>Places a joint with its local Z along <paramref name="axis"/> and connects two bodies.</summary>
    void Link(Node world, Joint3D joint, Vector3 at, Vector3 axis, RigidBody3D a, RigidBody3D b)
    {
        world.AddChild(joint);
        Vector3 z = axis.Normalized();
        Vector3 y = Vector3.Up - z * Vector3.Up.Dot(z);
        y = y.Normalized();
        joint.GlobalTransform = new Transform3D(new Basis(y.Cross(z), y, z), at);
        joint.NodeA = joint.GetPathTo(a);
        joint.NodeB = joint.GetPathTo(b);
        _joints.Add(joint);
    }
}
