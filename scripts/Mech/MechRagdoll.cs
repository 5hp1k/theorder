using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Turns a destroyed mech into a physical wreck. On <see cref="Activate"/> the body and each leg bone
/// become rigid bodies (sized from the meshes they carry) linked by joints that bend the way the leg
/// does: a limited ball at the hip, hinges across the knee, heel and ankle. A dead machine is no rag
/// doll: each joint folds only the way it was already bent (a knee does not bend backwards), and its
/// locked actuators hold against the weight until it forces them — the legs buckle under the hull
/// rather than flop. Where the wreck hits the ground it throws up dust and shakes the earth.
/// <see cref="Deactivate"/> puts every part back where the scene had it, ready for the IK again.
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
    /// <summary>Angular damping of the leg parts: dead steel doesn't swing freely.</summary>
    [Export] public float LimbAngularDamp = 2.5f;
    /// <summary>How hard the hull is tipped over at the moment of death, rad/s.</summary>
    [Export] public float ToppleSpeed = 0.6f;

    [ExportGroup("Joint limits (radians, each way from the pose at death)")]
    [Export] public float HipSwing = 1.0f;
    [Export] public float HipTwist = 0.25f;
    [Export] public float KneeBend = 1.3f;
    [Export] public float HeelBend = 1.1f;
    [Export] public float AnkleBend = 0.8f;
    /// <summary>How far a joint may open against its bend (the other way it folds up to the limits above).</summary>
    [Export] public float BackBend = 0.06f;
    /// <summary>
    /// How hard the dead actuators hold each joint, as a share of the torque the hull's weight puts on it
    /// over a metre: below 1 the leg gives way under the load, slowly at first.
    /// </summary>
    [Export(PropertyHint.Range, "0,1.5")] public float JointHold = 0.15f;

    [ExportGroup("Hitting the ground")]
    /// <summary>Thrown up where the hull lands (dust and the crash of it).</summary>
    [Export] public PackedScene HullImpactScene;
    /// <summary>Where a leg part lands.</summary>
    [Export] public PackedScene LimbImpactScene;
    /// <summary>Speed into the ground, m/s, below which a part just settles.</summary>
    [Export] public float ImpactSpeed = 3f;
    /// <summary>Shake of the player's view from the hull landing next to them (less with distance, none past ShakeRange).</summary>
    [Export] public float ImpactShake = 0.5f;
    [Export] public float ShakeRange = 90f;

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
    Mech _owner;
    // Each body's speed at the last tick, and whether it has already made its crash.
    readonly Dictionary<RigidBody3D, Vector3> _lastVelocity = new();
    readonly HashSet<RigidBody3D> _landed = new();

    public void Activate(Mech mech, Vector3 velocity)
    {
        if (Active) return;
        Node world = mech.GetParent();
        Vector3 right = mech.GlobalTransform.Basis.X.Normalized();
        float limbMass = TotalMass * (1f - HullMassShare) / 2f;

        _owner = mech;
        _hull = MakeBody(world, "WreckHull", mech.Hips, TotalMass * HullMassShare, 0.85f, velocity);
        // A shove so it keels over instead of balancing on dead legs: mostly forward or back, a little sideways.
        Vector3 tip = right * (GD.Randf() < 0.5f ? -1f : 1f) + mech.GlobalTransform.Basis.Z * (float)GD.RandRange(-0.5, 0.5);
        _hull.AngularVelocity = tip.Normalized() * ToppleSpeed;
        // The torque the hull's weight puts on a joint a metre off, and the share of it the joints hold.
        float g = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f) * GravityScale;
        float hold = JointHold * TotalMass * HullMassShare * g;

        foreach (var leg in mech.Legs)
        {
            var pose = leg.Pose;
            var thigh = MakeBody(world, "WreckThigh", leg.Thigh, limbMass * 0.36f, 1f, velocity, leg.HipJoint);
            var shin = MakeBody(world, "WreckShin", leg.Shin, limbMass * 0.28f, 1f, velocity, leg.KneeJoint);
            var meta = MakeBody(world, "WreckMeta", leg.Meta, limbMass * 0.14f, 1f, velocity, leg.HeelJoint);
            var foot = MakeBody(world, "WreckFoot", leg.Foot, limbMass * 0.22f, 1f, velocity);

            // Hip: swings fore and aft, hardly sideways or round; held like the rest.
            var hip = new Generic6DofJoint3D();
            Link(world, hip, pose.Hip, right, _hull, thigh);
            foreach (var (axis, span) in new[] { (Vector3.Axis.X, HipTwist), (Vector3.Axis.Y, HipTwist), (Vector3.Axis.Z, HipSwing) })
            {
                SetAngular(hip, axis, Generic6DofJoint3D.Flag.EnableAngularLimit, true);
                SetAngular(hip, axis, Generic6DofJoint3D.Param.AngularLowerLimit, -span);
                SetAngular(hip, axis, Generic6DofJoint3D.Param.AngularUpperLimit, span);
                Hold(hip, axis, hold);
            }

            // Knee, heel and ankle fold the way the leg is bent, and barely open the other way.
            Hinge(world, pose.Knee, right, thigh, shin, KneeBend, Bend(pose.Hip, pose.Knee, pose.Heel, right), hold);
            Hinge(world, pose.Heel, right, shin, meta, HeelBend, Bend(pose.Knee, pose.Heel, pose.Ankle, right), hold * 0.8f);
            Hinge(world, pose.Ankle, right, meta, foot, AnkleBend, Bend(pose.Heel, pose.Ankle, pose.Ankle - (leg.Foot.GlobalTransform.Basis.Z), right), hold * 0.5f);
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

    public override void _PhysicsProcess(double delta)
    {
        if (_bodies.Count == 0) return;
        foreach (var body in _bodies)
        {
            if (!IsInstanceValid(body)) continue;
            Vector3 now = body.LinearVelocity;
            Vector3 before = _lastVelocity.GetValueOrDefault(body, now);
            _lastVelocity[body] = now;
            // Coming down fast and stopped short: it has hit the ground.
            if (_landed.Contains(body) || before.Y > -ImpactSpeed || now.Y - before.Y < ImpactSpeed * 0.6f) continue;
            _landed.Add(body);
            Land(body, -before.Y);
        }
    }

    /// <summary>A part of the wreck has hit the ground at that speed.</summary>
    void Land(RigidBody3D body, float speed)
    {
        bool hull = body == _hull;
        Vector3 at = body.GlobalPosition;
        var scene = hull ? HullImpactScene : LimbImpactScene;
        if (scene != null) OneShotFx.Spawn(scene, body.GetParent(), new Vector3(at.X, at.Y - 1f, at.Z));
        if (!hull) return;
        // The ground jumps under the player's feet, the more the nearer and the harder it came down.
        if (Game.PlayerUnit is IDrivable player && player.Body != _owner)
        {
            float near = 1f - Mathf.Clamp(player.GlobalPosition.DistanceTo(at) / ShakeRange, 0f, 1f);
            player.Shake += ImpactShake * near * near * Mathf.Clamp(speed / 8f, 0.4f, 1.2f);
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
        _lastVelocity.Clear();
        _landed.Clear();
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
            AngularDamp = _bodies.Count == 0 ? AngularDamp : LimbAngularDamp,  // (the hull is made first)
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

    /// <summary>Which way a joint between the bones a→b and b→c is bent about the axis: +1 or -1.</summary>
    static float Bend(Vector3 a, Vector3 b, Vector3 c, Vector3 axis)
        => (b - a).Cross(c - b).Dot(axis) >= 0f ? 1f : -1f;

    /// <param name="fold">The way the joint is bent (see <see cref="Bend"/>): it may fold on that way, hardly the other.</param>
    /// <param name="hold">Torque, N·m, the dead actuator holds against.</param>
    void Hinge(Node world, Vector3 at, Vector3 axis, RigidBody3D a, RigidBody3D b, float bend, float fold, float hold)
    {
        var hinge = new HingeJoint3D();
        Link(world, hinge, at, axis, a, b);
        hinge.SetFlag(HingeJoint3D.Flag.UseLimit, true);
        hinge.SetParam(HingeJoint3D.Param.LimitLower, fold > 0f ? -BackBend : -bend);
        hinge.SetParam(HingeJoint3D.Param.LimitUpper, fold > 0f ? bend : BackBend);
        // A motor told to stand still, with only so much strength: friction in the joint.
        hinge.SetFlag(HingeJoint3D.Flag.EnableMotor, true);
        hinge.SetParam(HingeJoint3D.Param.MotorTargetVelocity, 0f);
        hinge.SetParam(HingeJoint3D.Param.MotorMaxImpulse, hold / Engine.PhysicsTicksPerSecond);
    }

    static void SetAngular(Generic6DofJoint3D joint, Vector3.Axis axis, Generic6DofJoint3D.Flag flag, bool on)
    {
        switch (axis)
        {
            case Vector3.Axis.X: joint.SetFlagX(flag, on); break;
            case Vector3.Axis.Y: joint.SetFlagY(flag, on); break;
            default: joint.SetFlagZ(flag, on); break;
        }
    }

    static void SetAngular(Generic6DofJoint3D joint, Vector3.Axis axis, Generic6DofJoint3D.Param param, float value)
    {
        switch (axis)
        {
            case Vector3.Axis.X: joint.SetParamX(param, value); break;
            case Vector3.Axis.Y: joint.SetParamY(param, value); break;
            default: joint.SetParamZ(param, value); break;
        }
    }

    /// <summary>Friction about one axis of a six-way joint: a motor held at a standstill with only so much force.</summary>
    static void Hold(Generic6DofJoint3D joint, Vector3.Axis axis, float torque)
    {
        SetAngular(joint, axis, Generic6DofJoint3D.Flag.EnableMotor, true);
        SetAngular(joint, axis, Generic6DofJoint3D.Param.AngularMotorTargetVelocity, 0f);
        SetAngular(joint, axis, Generic6DofJoint3D.Param.AngularMotorForceLimit, torque);
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
