using Godot;

namespace MechSim;

/// <summary>
/// The mech's posture other than walking: down on one knee to shoot. It is laid over the walking pose
/// (Hips and <see cref="MechLeg.Override"/>), not handed to physics.
///
/// One knee (<see cref="KneelLeg"/>) goes down on the ground just ahead of its hip, its shin and
/// foot lying back behind; the other foot steps forward and takes the weight. The pelvis comes down by
/// about the length of the shin. Neither walks nor turns on its legs; the torso aims as ever.
/// </summary>
public partial class Mech
{
    [ExportGroup("Kneeling")]
    /// <summary>Which knee goes down.</summary>
    [Export(PropertyHint.Enum, "Left,Right")] public int KneelLeg = 1;
    /// <summary>Seconds to go down on the knee, and to stand up again.</summary>
    [Export] public float KneelTime = 1.1f;
    [Export] public float StandTime = 0.9f;
    /// <summary>How far ahead of its hip the knee rests on the ground, as a share of the thigh.</summary>
    [Export] public float KneeAhead = 0.22f;
    /// <summary>How far forward the other foot is put, as a share of its thigh.</summary>
    [Export] public float KneelStep = 0.7f;
    /// <summary>How much of the kick of its own guns it takes on its knee (a share of what it takes standing).</summary>
    [Export(PropertyHint.Range, "0,1")] public float KneelRecoil = 0.45f;

    /// <summary>Set by the pilot each tick: down on one knee (it stops first), or up.</summary>
    public bool Kneel { get; set; }
    /// <summary>How far down on its knee it is: 0 standing, 1 kneeling.</summary>
    public float Kneeling => _kneel;
    /// <summary>On its knee, or on the way down or up: the legs aren't walking.</summary>
    bool Posed => _kneel > 0f;

    float _kneel, _reachDrop;
    /// <summary>The ground under the knee, in the mech's own axes.</summary>
    float _kneelGround;
    /// <summary>Where the foot that stays up is: 0 at home, 1 out ahead (or on its way there), 2 on its way back.</summary>
    int _frontGoal;
    Vector3 _stepTo, _stepToN;

    MechLeg Kneeler => Legs[KneelLeg];
    MechLeg Front => Legs[1 - KneelLeg];

    void ResetPosture()
    {
        _kneel = _reachDrop = 0f;
        _frontGoal = 0;
        Kneel = false;
        foreach (var leg in Legs) leg.Override.Weight = 0f;
    }

    // ── Every tick ──────────────────────────────────────────────

    void UpdatePosture(float dt)
    {
        // It goes down only when it has stopped with both feet on the ground.
        float was = _kneel;
        bool want = Kneel && !Airborne;
        if (want && (_kneel > 0f || (!Offline && Mathf.Abs(Speed) < 0.6f && Legs[0].Planted && Legs[1].Planted)))
        {
            if (_kneel <= 0f) _kneelGround = KneelGround();
            _kneel = Mathf.Min(1f, _kneel + dt / KneelTime);
        }
        else if (!want && !Offline) _kneel = Mathf.Max(0f, _kneel - dt / StandTime);
        if (_kneel != was) StepFront(was);
    }

    Vector3 FrontSpot()
    {
        Vector3 home = Front.HomeLocal;
        return GlobalTransform * new Vector3(home.X, 0f, home.Z - KneelStep * Front.ThighLength);
    }

    float KneelGround()
    {
        Vector3 hip = Kneeler.HipLocal;
        Vector3 at = GlobalTransform * new Vector3(hip.X, 0f, hip.Z - KneeAhead * Kneeler.ThighLength);
        return (GroundAt(at, out var p, out _) ? p.Y : at.Y) - GlobalPosition.Y;
    }

    void GroundOr(Vector3 at, out Vector3 position, out Vector3 normal)
    {
        if (!GroundAt(at, out position, out normal))
        {
            position = new Vector3(at.X, GlobalPosition.Y, at.Z);
            normal = Vector3.Up;
        }
    }

    /// <summary>
    /// The foot that stays up steps out ahead as the mech goes down (the first half of the way) and back
    /// as it stands up (the last half). <paramref name="before"/> is how far down it was a tick ago.
    /// </summary>
    void StepFront(float before)
    {
        var front = Front;
        const float Half = 0.5f;
        float lift = StepHeight * 0.6f;
        if (_kneel > before)
        {
            float u = Mathf.Min(_kneel / Half, 1f);
            if (_frontGoal != 1)
            {
                _frontGoal = 1;
                GroundOr(FrontSpot(), out _stepTo, out _stepToN);
                front.BeginSwing(Mathf.Min(u, 0.9f));
            }
            if (!front.Planted)
            {
                front.Swing(u, _stepTo, _stepToN, lift);
                if (u >= 1f) front.Land(_stepTo, _stepToN);
            }
        }
        else if (_kneel < before && _kneel < Half)
        {
            float u = 1f - _kneel / Half;
            if (_frontGoal != 2)
            {
                _frontGoal = 2;
                GroundOr(GlobalTransform * front.HomeLocal, out _stepTo, out _stepToN);
                front.BeginSwing(Mathf.Min(u, 0.9f));
            }
            if (!front.Planted)
            {
                front.Swing(u, _stepTo, _stepToN, lift);
                if (_kneel <= 0f)
                {
                    front.Land(_stepTo, _stepToN);
                    _frontGoal = 0;
                }
            }
        }
    }

    /// <summary>How far the foot of the kneeling leg is tipped onto its toes, radians.</summary>
    const float KneelToes = 0.9f;

    /// <summary>
    /// The leg the mech kneels on, at the bottom: the knee on the ground just ahead of the hip, the foot
    /// behind it up on its toes, the shin and the metatarsal between (world space).
    /// </summary>
    void KneelLegPose(MechLeg leg, Transform3D root, Vector3 hip, out Vector3 ankle, out Vector3 heel, out Vector3 knee)
    {
        // Worked out in the leg's own upright plane, in the mech's axes: z back, y up.
        float x = (root.AffineInverse() * hip).X;
        float thigh = leg.ThighLength, shin = leg.ShinLength, meta = leg.MetaLength;
        float ground = _kneelGround;
        var k = new Vector2(Kneeler.HipLocal.Z - KneeAhead * thigh, ground + 0.1f * thigh);
        // The ankle over the toes, the foot tipped up onto them.
        float c = Mathf.Cos(KneelToes), s = Mathf.Sin(KneelToes);
        var overToes = new Vector2(leg.ToeReach * c - leg.AnkleHeight * s, leg.AnkleHeight * c + leg.ToeReach * s);
        // The toes as far back as lets the shin and the metatarsal come to them comfortably bent.
        float span = (shin + meta) * 0.85f;
        float dy = Mathf.Min(ground + overToes.Y - k.Y, span * 0.95f);
        var a = new Vector2(k.X + Mathf.Sqrt(span * span - dy * dy), ground + overToes.Y);
        // The heel: shin's length from the knee, the metatarsal's from the ankle, above the line between.
        Vector2 along = a - k;
        float d = Mathf.Clamp(along.Length(), Mathf.Abs(shin - meta) + 0.01f, shin + meta - 0.01f);
        along = along.Normalized();
        float part = (shin * shin - meta * meta + d * d) / (2f * d);
        float rise = Mathf.Sqrt(Mathf.Max(shin * shin - part * part, 0f));
        var up = new Vector2(along.Y, -along.X);
        if (up.Y < 0f) up = -up;
        Vector2 h = k + along * part + up * rise;
        knee = root * new Vector3(x, k.Y, k.X);
        ankle = root * new Vector3(x, a.Y, a.X);
        heel = root * new Vector3(x, h.Y, h.X);
    }

    // ── The pose ────────────────────────────────────────────────

    /// <summary>
    /// The pelvis where walking puts it (in the mech's own axes), taken down to the knee as far as the mech
    /// is kneeling; and the legs' poses to go with it.
    /// </summary>
    void Pose(ref Vector3 position, ref Basis basis)
    {
        foreach (var leg in Legs) leg.Override.Weight = 0f;
        float k = Mathf.SmoothStep(0f, 1f, _kneel);
        if (k <= 0f) return;

        var kneeler = Kneeler;
        float thigh = kneeler.ThighLength;
        float ahead = KneeAhead * thigh;
        float kneeHeight = 0.1f * thigh; // the knee's own thickness
        Vector3 hipAt = kneeler.HipLocal;
        float kneelY = _kneelGround + kneeHeight + Mathf.Sqrt(thigh * thigh - ahead * ahead) - hipAt.Y;
        float bob = _bob;
        position = position.Lerp(new Vector3(0f, kneelY + bob, 0f), k);
        basis = basis.Orthonormalized().Slerp(Basis.Identity, k);
        Transform3D root = GlobalTransform;
        Transform3D hips = root * new Transform3D(basis, position);
        Basis flat = root.Basis.Orthonormalized();
        Vector3 hip = hips * kneeler.HipLocal;
        KneelLegPose(kneeler, root, hip, out var ankle, out var heel, out var knee);
        kneeler.Override = new MechLeg.PoseOverride
        {
            Weight = k, Ankle = ankle, Heel = heel, Pole = (knee - hip).Normalized(),
            Foot = new Basis(flat.X, -KneelToes) * flat,
        };
    }
}
