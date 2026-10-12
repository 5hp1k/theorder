using Godot;

namespace MechSim;

/// <summary>
/// Walk cycle. One phase runs 0..1 per full cycle (left step + right step) and advances with distance
/// covered, so cadence and stride always match the ground speed: the legs keep a steady left-right
/// rhythm, each foot is planted for its share of the cycle and swings for the rest, and lands as far
/// ahead of the hip as it will end up behind it. Planted feet are locked to the ground, so they never
/// slide. Pure logic (no nodes).
/// </summary>
public sealed class Gait
{
    public delegate bool GroundQuery(Vector3 point, out Vector3 position, out Vector3 normal);

    /// <summary>Top speed, used to scale stride and lift.</summary>
    public float MaxSpeed = 13.9f;
    /// <summary>Distance covered per step at top speed, metres.</summary>
    public float StepLength = 4.6f;
    /// <summary>Shortest step, as a share of StepLength: a slow walk is slow long strides, not a quick shuffle.</summary>
    public float MinStepShare = 0.6f;
    /// <summary>How high the foot is lifted at top speed.</summary>
    public float StepHeight = 0.9f;
    /// <summary>Share of the cycle a foot spends in the air at a slow walk (under 0.5 = both feet down between steps).</summary>
    public float WalkSwing = 0.36f;
    /// <summary>The same at top speed. Over 0.5 gives a run with both feet off the ground.</summary>
    public float RunSwing = 0.5f;
    /// <summary>Slowest the legs go while the mech is moving at all, seconds per cycle: below the speed this
    /// corresponds to, the steps get shorter rather than slower.</summary>
    public float MaxCycle = 1.7f;
    /// <summary>Highest step up and deepest step down a foot will take, metres.</summary>
    public float MaxStepUp = 1.6f;
    public float MaxStepDown = 2.5f;
    /// <summary>Seconds per cycle when shuffling the feet into place at a standstill.</summary>
    public float IdleCycle = 1.7f;
    /// <summary>Half the distance between the feet; turns the yaw rate into foot travel.</summary>
    public float HipWidth = 1.5f;

    /// <summary>0..1 over a full left-right cycle.</summary>
    public float Phase { get; private set; }
    /// <summary>Length of the step being taken right now.</summary>
    public float CurrentStep { get; private set; } = 2.5f;

    bool _wasMoving;
    int _probe;

    readonly Vector3[] _target = new Vector3[2];
    readonly Vector3[] _targetN = { Vector3.Up, Vector3.Up };

    /// <param name="desiredVelocity">Where the speed is heading (throttle setting); with the acceleration it
    /// tells where the body will be when a foot comes down, so the feet neither overshoot when the mech is
    /// stopping nor lag when it is setting off.</param>
    public void Update(float dt, Transform3D root, Vector3 horizontalVelocity, Vector3 desiredVelocity, float acceleration,
                       float yawRate, MechLeg[] legs, GroundQuery ground)
    {
        Vector3 hv = horizontalVelocity;
        float spd = hv.Length();
        // Turning on the spot moves the feet too.
        float travel = spd + Mathf.Abs(yawRate) * HipWidth * 1.5f;
        float norm = Mathf.Clamp(spd / MaxSpeed, 0f, 1f);
        // Throttle on counts as moving: the first step starts at once, not when the speed has built up.
        bool moving = travel > 0.35f || desiredVelocity.Length() > 0.5f;

        float step = StepLength * Mathf.Lerp(MinStepShare, 1f, norm);
        float swing = moving ? Mathf.Lerp(WalkSwing, RunSwing, norm) : WalkSwing;
        float rate; // cycles per second
        if (moving)
        {
            // A steady, unhurried cadence at low speed, with the stride as long as that cadence allows.
            rate = Mathf.Max(travel / (2f * step), 1f / MaxCycle);
            step = Mathf.Max(travel, 0.2f) / (2f * rate);
        }
        else
        {
            // Standing: finish the step in progress and bring stray feet home, then hold still.
            bool busy = false;
            foreach (var leg in legs) busy |= !leg.Planted || HomeError(leg, root) > 0.9f;
            rate = busy ? 1f / IdleCycle : 0f;
        }
        // Setting off: start the cycle at the top of a step instead of somewhere in the middle of one.
        if (moving && !_wasMoving && legs[0].Planted && legs[1].Planted) Phase = 0f;
        _wasMoving = moving;
        CurrentStep = step;
        Phase = Mathf.PosMod(Phase + rate * dt, 1f);
        float cycleTime = rate > 1e-4f ? 1f / rate : IdleCycle;
        float height = StepHeight * (0.55f + 0.45f * norm) * Mathf.Clamp(step / (StepLength * MinStepShare), 0.45f, 1f);

        for (int i = 0; i < legs.Length; i++)
        {
            var leg = legs[i];
            leg.Advance(dt);
            float p = Mathf.PosMod(Phase + i * 0.5f, 1f);

            if (p >= swing)
            {
                if (!leg.Planted) leg.Land(_target[i], _targetN[i]);
                continue;
            }

            float u = p / swing;
            if (leg.Planted)
            {
                // Standing with this foot near enough: leave it.
                if (!moving && HomeError(leg, root) < 0.5f) continue;
                // Too late in this foot's turn for a proper step: wait for the next one rather than
                // take a hurried half-step, unless it has been left far behind.
                if (u > 0.3f && HomeError(leg, root) < Mathf.Max(step, 2f) * 1.5f) continue;
                leg.BeginSwing(u);
            }

            // Where the hip will be when this foot comes down (turning included); the foot lands ahead of
            // its neutral spot by half of what the body covers while it's planted.
            // (Looked up a few times during the swing rather than every tick: it costs a ray.)
            if (!leg.HasTarget || (u < 0.8f && ++_probe % 4 == 0))
            {
                float left = (1f - u) * swing * cycleTime;
                // Speed at touchdown: on the way to what the throttle asks for.
                Vector3 vLand = hv.MoveToward(desiredVelocity, acceleration * left);
                var turn = new Basis(Vector3.Up, yawRate * left);
                var rootAtLanding = new Transform3D(turn * root.Basis, root.Origin + (hv + vLand) * 0.5f * left);
                // Ahead of the hip by half of what the body will cover while this foot is planted.
                Vector3 lead = turn * vLand * ((1f - swing) * cycleTime * 0.5f);
                Vector3 target = rootAtLanding * leg.HomeLocal + lead;
                if (ground(target, out var pos, out var n))
                {
                    // No stepping up onto boulders or down into pits beyond what a leg can take:
                    // past that the foot goes where the ground would be at the mech's own level.
                    float rise = pos.Y - root.Origin.Y;
                    if (rise > MaxStepUp || rise < -MaxStepDown)
                    {
                        pos.Y = root.Origin.Y + Mathf.Clamp(rise, -MaxStepDown, MaxStepUp);
                        n = Vector3.Up;
                    }
                    _target[i] = pos;
                    _targetN[i] = n;
                }
                else
                {
                    _target[i] = new Vector3(target.X, root.Origin.Y, target.Z);
                    _targetN[i] = Vector3.Up;
                }
            }
            leg.Swing(u, _target[i], _targetN[i], height);
        }
    }

    static float HomeError(MechLeg leg, Transform3D root)
    {
        Vector3 diff = leg.Contact - root * leg.HomeLocal;
        diff.Y = 0f;
        return diff.Length();
    }
}
