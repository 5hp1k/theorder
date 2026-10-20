using System;
using Godot;

namespace MechSim;

/// <summary>
/// One procedural leg. The parts are children in the mech scene:
/// Thigh, Shin, Meta (pivots centred on the bone, local Y along it), Foot, HipJoint, KneeJoint, HeelJoint.
/// This script owns the foot contact point and the swing, runs the IK and moves the parts.
/// The gait coordinator decides WHEN and WHERE to step; the leg only executes it.
///
/// The leg moves the way a limb does, not the way a crane does. A step is: the heel comes off the
/// ground and the foot rolls over its toes while the body passes on ahead; the knee folds and swings
/// a little outwards as the foot is picked up and carried through under the hip; the leg straightens,
/// reaching, toes up; the heel comes down first and the sole slaps flat after it. The joints follow
/// their goals like muscle pulling on a weight — with a lag, a little overshoot and a settle
/// (<see cref="MuscleTone"/>) — rather than tracking them exactly.
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

    [ExportGroup("Foot roll")]
    /// <summary>How far ahead of the foot's ground point the toes reach, and how far behind it the heel: the foot rolls over these.</summary>
    [Export] public float ToeReach = 0.9f;
    [Export] public float HeelReach = 0.5f;
    /// <summary>How far the heel lifts before the foot leaves the ground, radians.</summary>
    [Export] public float HeelOffAngle = 0.5f;
    /// <summary>The heel starts to lift once the hip is this far ahead of the foot, and is fully up at the second figure (shares of the leg's length).</summary>
    [Export] public float HeelOffStart = 0.12f;
    [Export] public float HeelOffFull = 0.45f;
    /// <summary>Toes-up angle the foot lands at (heel first), radians, and the seconds it takes to come down flat.</summary>
    [Export] public float HeelStrikeAngle = 0.24f;
    [Export] public float StrikeTime = 0.16f;
    /// <summary>How far the toes hang while the foot is carried forward, radians.</summary>
    [Export] public float ToeHang = 0.42f;

    [ExportGroup("Muscle")]
    /// <summary>How quickly the joints take up a new pose, 1/s. Lower = heavier, lazier limbs.</summary>
    [Export] public float MuscleTone = 13f;
    /// <summary>0 = joints ring like a spring, 1 = no overshoot at all.</summary>
    [Export(PropertyHint.Range, "0.3,1.2")] public float MuscleDamping = 0.62f;
    /// <summary>How far the knee swings outwards while the foot is in the air (a share of its forward reach).</summary>
    [Export] public float KneeSplay = 0.14f;

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

    /// <summary>The furthest the hip is let be from the heel, a little short of the leg held straight, metres.</summary>
    public float Reach => (ThighLength + ShinLength) * 0.96f;
    /// <summary>How high the heel stands over the ground with the foot flat and the metatarsal at rest.</summary>
    public float HeelRise => AnkleHeight + MetaLength * Mathf.Cos(MetaRestAngle);

    /// <summary>
    /// A pose laid over the walking one (kneeling, lying on the ground): where the ankle and the heel go,
    /// which way the knee bends and how the foot lies, all in world space, taken in by Weight (0 = not at
    /// all, 1 = entirely). The mech sets it each time before the leg is solved.
    /// </summary>
    public struct PoseOverride
    {
        public float Weight;
        public Vector3 Ankle, Heel, Pole;
        public Basis Foot;
    }
    public PoseOverride Override;

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
    float _t, _u0, _metaAngle = 0.6f, _metaVel;
    float _footPitch, _footPitchVel, _splay, _splayVel;
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
        _footPitch = _footPitchVel = _splay = _splayVel = _metaVel = 0f;
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
        // The foot is slow to leave (the toes are still pushing off), is carried through quickly under
        // the hip and slows again as the leg reaches out, arriving over its spot a little early.
        float reach = Mathf.Min(1f, _t / 0.9f);
        float s = reach * reach * reach * (reach * (reach * 6f - 15f) + 10f);
        // It is picked up early, by the knee folding, and from the top of the step let fall: the weight
        // of the machine comes down on it, it isn't set down.
        const float Top = 0.4f;
        float arc;
        if (_t < Top)
        {
            float up = Mathf.Sin(_t / Top * Mathf.Pi * 0.5f);
            arc = up * up;
        }
        else
        {
            float down = (_t - Top) / (1f - Top);
            arc = 1f - down * down;
        }
        // Lift higher when stepping up onto something.
        float lift = height + Mathf.Max(0f, to.Y - _from.Y) * 0.6f;
        _contact = _from.Lerp(to, s) + Vector3.Up * (arc * lift);
        _normal = _fromN.Lerp(toN, s).Normalized();
    }

    /// <summary>The ground under a planted foot has moved (a crater, a wreck gone from under it): the foot goes with it.</summary>
    public void Settle(Vector3 contact, Vector3 normal)
    {
        if (!Planted) return;
        _contact = contact;
        _normal = normal;
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
    Vector3 _airLocal;

    public void Hang(Transform3D root, Vector3 localFoot, float dt)
    {
        Planted = false;
        HasTarget = false;
        // The foot is carried in the body's own axes while in the air: it tucks towards its place at its
        // own pace however fast the body is flying. (Held in the world it trailed metres behind.)
        if (!_airborne) _airLocal = root.AffineInverse() * _contact;
        _airborne = true;
        _t = 0.5f;
        _airLocal = _airLocal.Lerp(localFoot, 1f - Mathf.Exp(-7f * dt));
        _contact = root * _airLocal;
        _normal = root.Basis.Y.Normalized();
    }

    /// <summary>A joint pulled towards its goal by muscle: a damped spring, stepped stably whatever the tick rate.</summary>
    void Pull(ref float value, ref float rate, float goal, float dt, float tone = 1f)
    {
        float w = MuscleTone * tone;
        // (Semi-implicit, so a long tick can't blow it up.)
        rate = (rate + w * w * (goal - value) * dt) / (1f + 2f * MuscleDamping * w * dt + w * w * dt * dt);
        value += rate * dt;
    }

    /// <summary>Runs IK and moves the parts. rootBasis gives the mech's facing.</summary>
    /// <param name="velocity">The body's speed over the ground: which way the foot is being walked over.</param>
    public void Solve(Transform3D hips, Basis rootBasis, Vector3 velocity, float dt)
    {
        Vector3 right = rootBasis.X.Normalized();
        Vector3 forward = -rootBasis.Z.Normalized();
        Vector3 hip = hips * HipLocal;
        dt = Mathf.Clamp(dt, 1e-4f, 0.05f);

        // ── The foot: heel off, toes hanging, toes up, heel strike ──
        velocity.Y = 0f;
        float speed = velocity.Length();
        // Walking backwards the roll runs the other way: toes first down, heel last up.
        float way = speed > 0.4f && velocity.Dot(forward) < 0f ? -1f : 1f;
        float legLength = ThighLength + ShinLength + MetaLength;
        float pitchGoal, pivotShare;
        if (_airborne)
        {
            pitchGoal = -0.5f;
            pivotShare = 0f;
        }
        else if (Planted)
        {
            // Just landed: on its heel, coming down flat.
            float strike = HeelStrikeAngle * Mathf.Clamp(1f - PlantedTime / Mathf.Max(StrikeTime, 0.01f), 0f, 1f);
            // The body has walked on over the foot: the heel peels off the ground behind it.
            float off = 0f;
            if (speed > 0.4f)
            {
                float trail = (hip - _contact).Dot(velocity / speed) / legLength;
                off = HeelOffAngle * Mathf.SmoothStep(HeelOffStart, HeelOffFull, trail) * Mathf.Clamp(speed / 2f, 0f, 1f);
            }
            pitchGoal = way * (strike - off);
            pivotShare = 1f;
        }
        else
        {
            // In the air: toes hanging after the push-off, coming up for the heel strike as the leg reaches.
            pitchGoal = way * Mathf.Lerp(-ToeHang, HeelStrikeAngle, Mathf.SmoothStep(0.3f, 0.86f, _t));
            // Still rolling off the toes as it leaves, already over the heel as it arrives.
            pivotShare = Mathf.Max(1f - Mathf.SmoothStep(0f, 0.22f, _t), Mathf.SmoothStep(0.72f, 1f, _t));
        }
        // (Landing, the foot is already at its strike angle: no need to swing to it.)
        Pull(ref _footPitch, ref _footPitchVel, pitchGoal, dt, 1.5f);

        Basis flat = Geo.BasisFromUpForward(_normal, forward);
        Vector3 ahead = -flat.Z;
        Vector3 ankleFlat = _contact + _normal * AnkleHeight;
        // Toes down: over the toes. Toes up: over the heel. In mid-air: about the ankle.
        Vector3 end = _contact + ahead * (_footPitch < 0f ? ToeReach : -HeelReach);
        Vector3 pivot = ankleFlat.Lerp(end, pivotShare);
        Basis roll = new Basis(flat.X, _footPitch);
        Vector3 ankle = pivot + roll * (ankleFlat - pivot);
        Basis footBasis = roll * flat;

        float a = MetaRestAngle + MetaSwingAngle * SwingPhase;
        // Heel sits above and behind the ankle; tilt it with the ground normal a little.
        Vector3 up = Vector3.Up.Lerp(_normal, 0.5f).Normalized();
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
        // Taken up by muscle, so the heel neither flicks between poses nor arrives dead on them.
        Pull(ref _metaAngle, ref _metaVel, best, dt, 0.8f);
        Vector3 metaDir = up * Mathf.Cos(_metaAngle) - forward * Mathf.Sin(_metaAngle);
        Vector3 metaVec = metaDir * MetaLength;

        // The knee swings out a little as the leg is lifted and carried through, and back in under the load.
        Pull(ref _splay, ref _splayVel, _airborne ? KneeSplay * 0.6f : KneeSplay * SwingPhase, dt, 0.7f);
        Vector3 pole = forward + right * (Side * _splay);

        if (Override.Weight > 0f)
        {
            float w = Mathf.Min(Override.Weight, 1f);
            Vector3 heel = (ankle + metaVec).Lerp(Override.Heel, w);
            ankle = ankle.Lerp(Override.Ankle, w);
            Vector3 along = heel - ankle;
            if (along.LengthSquared() > 1e-6f) metaVec = along.Normalized() * MetaLength;
            pole = pole.Lerp(Override.Pole, w);
            footBasis = footBasis.Orthonormalized().Slerp(Override.Foot.Orthonormalized(), w);
        }

        var r = ChickenLegSolver.Solve(hip, ankle, metaVec, ThighLength, ShinLength, pole);
        Pose = r;

        _hipJ.GlobalPosition = r.Hip;
        _kneeJ.GlobalPosition = r.Knee;
        _heelJ.GlobalPosition = r.Heel;
        Geo.PlaceRigid(_thigh, r.Hip, r.Knee, right);
        Geo.PlaceRigid(_shin, r.Knee, r.Heel, right);
        Geo.PlaceRigid(_meta, r.Heel, r.Ankle, right);

        // (If the leg couldn't reach, the foot goes where the leg ends rather than coming off its ankle.)
        Vector3 footCenter = r.Ankle - footBasis.Y * (AnkleHeight * 0.5f);
        _foot.GlobalTransform = new Transform3D(footBasis, footCenter);
    }
}
