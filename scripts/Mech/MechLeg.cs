using System;
using Godot;

namespace MechSim;

/// <summary>
/// One procedural leg. The parts are children in the mech scene:
/// Thigh, Shin, Meta (pivots centred on the bone, local Y along it), Foot, HipJoint, KneeJoint, HeelJoint.
/// This script owns the foot contact point and the swing, runs the IK and moves the parts.
/// The gait coordinator decides WHEN and WHERE to step; the leg only executes it.
/// </summary>
public partial class MechLeg : Node3D
{
    /// <summary>-1 = left, +1 = right.</summary>
    [Export] public float Side = -1f;
    /// <summary>Hip joint marker (child of Hips).</summary>
    [Export] public NodePath HipMarker;
    /// <summary>Neutral foot position marker at ground level (child of the mech root).</summary>
    [Export] public NodePath HomeMarker;

    [ExportGroup("Proportions (must match the meshes)")]
    [Export] public float ThighLength = 2.7f;
    [Export] public float ShinLength = 2.9f;
    [Export] public float MetaLength = 1.6f;
    /// <summary>Distance from ground contact to the top of the foot where the metatarsal attaches.</summary>
    [Export] public float AnkleHeight = 0.35f;
    /// <summary>Metatarsal lean back from vertical when standing (radians).</summary>
    [Export] public float MetaRestAngle = 0.62f;
    /// <summary>Extra lean while the foot is in the air (toe hangs, heel lifts).</summary>
    [Export] public float MetaSwingAngle = 0.55f;
    /// <summary>How bent the thigh and shin like to be: hip-to-heel distance as a share of their combined length.</summary>
    [Export(PropertyHint.Range, "0.4,0.98")] public float LegFold = 0.72f;
    /// <summary>How strongly the metatarsal holds its rest angle against rolling with the stride.</summary>
    [Export] public float MetaStiffness = 0.05f;

    /// <summary>Hip joint in Hips-node space.</summary>
    public Vector3 HipLocal { get; private set; }
    /// <summary>Neutral foot position in mech-root space (ground level).</summary>
    public Vector3 HomeLocal { get; private set; }

    public bool Planted { get; private set; } = true;
    public float PlantedTime { get; private set; }
    /// <summary>0 when planted, peaks at 1 mid-swing.</summary>
    public float SwingPhase => Planted ? 0f : Mathf.Sin(_t * Mathf.Pi);
    public Vector3 Contact => _contact;
    public Vector3 Normal => _normal;

    public event Action<MechLeg> Landed;

    /// <summary>Joint positions from the last IK solve (world space).</summary>
    public ChickenLegSolver.Result Pose { get; private set; }
    public Node3D Thigh => _thigh;
    public Node3D Shin => _shin;
    public Node3D Meta => _meta;
    public Node3D Foot => _foot;
    public Node3D HipJoint => _hipJ;
    public Node3D KneeJoint => _kneeJ;
    public Node3D HeelJoint => _heelJ;

    Vector3 _contact;
    Vector3 _normal = Vector3.Up;
    Vector3 _from, _to, _fromN, _toN;
    float _t, _u0, _metaAngle = 0.6f;
    bool _airborne;

    Node3D _thigh, _shin, _meta, _foot, _hipJ, _kneeJ, _heelJ;

    public override void _Ready()
    {
        HipLocal = GetNode<Node3D>(HipMarker).Position;
        HomeLocal = GetNode<Node3D>(HomeMarker).Position;
        _thigh = GetNode<Node3D>("Thigh");
        _shin = GetNode<Node3D>("Shin");
        _meta = GetNode<Node3D>("Meta");
        _foot = GetNode<Node3D>("Foot");
        _hipJ = GetNode<Node3D>("HipJoint");
        _kneeJ = GetNode<Node3D>("KneeJoint");
        _heelJ = GetNode<Node3D>("HeelJoint");
    }

    public void Teleport(Vector3 contact, Vector3 normal)
    {
        _contact = contact;
        _normal = normal;
        Planted = true;
        PlantedTime = 1f;
        HasTarget = false;
        _airborne = false;
        _t = 0f;
    }

    /// <summary>True once a landing spot has been given for the swing in progress.</summary>
    public bool HasTarget { get; private set; }

    /// <summary>Lifts the foot. u is how far into the swing window the cycle already is (0..1).</summary>
    public void BeginSwing(float u)
    {
        _from = _contact;
        _fromN = _normal;
        _u0 = Mathf.Min(u, 0.9f);
        _airborne = false;
        _t = 0f;
        HasTarget = false;
        Planted = false;
    }

    /// <summary>Moves the foot along its arc towards the landing spot; u runs 0..1 over the swing window.</summary>
    public void Swing(float u, Vector3 to, Vector3 toN, float height)
    {
        HasTarget = true;
        _t = Mathf.Clamp((u - _u0) / (1f - _u0), 0f, 1f);
        // The foot is carried forward and arrives over its spot early…
        float reach = Mathf.Min(1f, _t / 0.88f);
        float s = reach * reach * (3f - 2f * reach);
        // …hangs high late in the swing, then is driven straight down: a stomp, not a glide.
        float arc = Mathf.Sin(Mathf.Pow(_t, 1.7f) * Mathf.Pi);
        // Lift higher when stepping up onto something.
        float lift = height + Mathf.Max(0f, to.Y - _from.Y) * 0.6f;
        _contact = _from.Lerp(to, s) + Vector3.Up * (arc * lift);
        _normal = _fromN.Lerp(toN, s).Normalized();
    }

    public void Land(Vector3 to, Vector3 toN)
    {
        _contact = to;
        _normal = toN;
        _airborne = false;
        _t = 0f;
        Planted = true;
        PlantedTime = 0f;
        Landed?.Invoke(this);
    }

    public void Advance(float dt)
    {
        if (Planted) PlantedTime += dt;
    }

    /// <summary>
    /// Airborne: the leg is drawn up into a tucked pose held relative to the body (so it rides with the
    /// mech instead of trailing behind it), given as a foot position in the mech's own space.
    /// </summary>
    public void Hang(Transform3D root, Vector3 localFoot, float dt)
    {
        Planted = false;
        HasTarget = false;
        _airborne = true;
        _t = 0.5f;
        Vector3 local = root.AffineInverse() * _contact;
        _contact = root * local.Lerp(localFoot, 1f - Mathf.Exp(-7f * dt));
        _normal = root.Basis.Y.Normalized();
    }

    /// <summary>Runs IK and moves the parts. rootBasis gives the mech's facing.</summary>
    public void Solve(Transform3D hips, Basis rootBasis)
    {
        Vector3 right = rootBasis.X.Normalized();
        Vector3 forward = -rootBasis.Z.Normalized();
        Vector3 hip = hips * HipLocal;

        float a = MetaRestAngle + MetaSwingAngle * SwingPhase;
        // Heel sits above and behind the ankle; tilt it with the ground normal a little.
        Vector3 up = Vector3.Up.Lerp(_normal, 0.5f).Normalized();
        Vector3 ankle = _contact + _normal * AnkleHeight;
        // The metatarsal rolls over the foot as the leg sweeps. Its lean is chosen so the thigh and shin
        // stay comfortably bent: with the foot trailing the heel rises up and forward (instead of the
        // shin stretching out flat behind the mech), with the foot ahead it lies back.
        float comfort = (ThighLength + ShinLength) * LegFold;
        float best = a, bestCost = float.MaxValue;
        for (int i = 0; i <= 14; i++)
        {
            float angle = Mathf.Lerp(-0.8f, 1.2f, i / 14f);
            Vector3 heel = ankle + (up * Mathf.Cos(angle) - forward * Mathf.Sin(angle)) * MetaLength;
            float stretch = (hip.DistanceTo(heel) - comfort) / comfort;
            float cost = stretch * stretch + MetaStiffness * (angle - a) * (angle - a);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = angle;
            }
        }
        // Eased, so the heel doesn't flick between poses.
        _metaAngle = Mathf.Lerp(_metaAngle, best, 0.12f);
        Vector3 metaDir = up * Mathf.Cos(_metaAngle) - forward * Mathf.Sin(_metaAngle);
        Vector3 metaVec = metaDir * MetaLength;

        var r = ChickenLegSolver.Solve(hip, ankle, metaVec, ThighLength, ShinLength, forward);
        Pose = r;

        _hipJ.GlobalPosition = r.Hip;
        _kneeJ.GlobalPosition = r.Knee;
        _heelJ.GlobalPosition = r.Heel;
        Geo.PlaceRigid(_thigh, r.Hip, r.Knee, right);
        Geo.PlaceRigid(_shin, r.Knee, r.Heel, right);
        Geo.PlaceRigid(_meta, r.Heel, r.Ankle, right);

        // Foot plate follows the ground normal; toes droop while swinging.
        Basis footBasis = Geo.BasisFromUpForward(_normal, forward);
        // Toe-off, then toes up for the heel strike.
        // In the air the toes hang; in a step: toe-off, then toes up for the heel strike.
        float pitch = Planted ? 0f : _airborne ? -0.5f : -0.45f * Mathf.Sin(_t * Mathf.Tau);
        footBasis = new Basis(right, pitch) * footBasis;
        Vector3 footCenter = r.Ankle - _normal * (AnkleHeight * 0.5f);
        _foot.GlobalTransform = new Transform3D(footBasis, footCenter);
    }
}
