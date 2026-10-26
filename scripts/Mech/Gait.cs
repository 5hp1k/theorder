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
    /// <summary>
    /// How far ahead of or behind its hip a planted foot can be, metres, with the leg still reaching it
    /// (worked out by the mech from the leg's length and its height). A step that would carry the foot
    /// further is made quicker and shorter instead.
    /// </summary>
    public float Reach = 3f;

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
            // While a foot is planted the body walks on from a foot's length ahead of it to as far behind
            // it: no further than the leg reaches. (Past that the legs go quicker, not longer.)
            rate = Mathf.Max(rate, travel * (1f - swing) / (2f * Mathf.Max(Reach, 0.5f)));
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
                else if (Lost(leg, root))
                {
                    if (ground(root * leg.HomeLocal, out var pos, out var n)) leg.Teleport(pos, n);
                    else leg.Teleport(root * leg.HomeLocal, Vector3.Up);
                }
                continue;
            }

            float u = p / swing;
            if (leg.Planted && Lost(leg, root))
            {
                // Left far behind, high above or below where it belongs (the body slid off a ledge, was
                // thrown, came down off a height): put back under the hip now — a stumble — rather than
                // the leg stretched out after it like a spider's.
                if (ground(root * leg.HomeLocal, out var pos, out var n)) leg.Teleport(pos, n);
                else leg.Teleport(root * leg.HomeLocal, Vector3.Up);
                continue;
            }
            if (leg.Planted)
            {
                // Standing with this foot near enough: leave it.
                if (!moving && HomeError(leg, root) < 0.5f) continue;
                // Too late in this foot's turn for a proper step: wait for the next one rather than
                // take a hurried half-step, unless it has been left far behind.
                if (u > 0.3f && HomeError(leg, root) < Mathf.Min(Mathf.Max(step, 2f) * 1.5f, Reach)) continue;
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
                Vector3 lead = (turn * vLand * ((1f - swing) * cycleTime * 0.5f)).LimitLength(Reach);
                // Walking sideways (the arcade controls, a hard turn): a foot steps out its own way, but
                // never in across the other one — at most to half way to the middle line.
                Vector3 side = rootAtLanding.Basis.X.Normalized();
                float homeX = leg.HomeLocal.X, across = lead.Dot(side);
                if (homeX * across < 0f && Mathf.Abs(across) > Mathf.Abs(homeX) * 0.5f)
                    lead -= side * (across - Mathf.Sign(across) * Mathf.Abs(homeX) * 0.5f);
                Vector3 home = rootAtLanding * leg.HomeLocal;
                Place(i, home, lead, root.Origin.Y, ground);
            }
            leg.Swing(u, _target[i], _targetN[i], height);
        }

        // Now and then, under each planted foot: the ground there may have gone (a crater blown in it,
        // a wreck or a building gone from under it), and the foot goes down with it rather than stand on air.
        if (++_settle % 16 == 0)
        {
            var leg = legs[(_settle / 16) % legs.Length];
            if (leg.Planted && ground(leg.Contact, out var pos, out var n))
            {
                float dy = pos.Y - leg.Contact.Y;
                if (Mathf.Abs(dy) > 0.12f && Mathf.Abs(dy) < 1.5f) leg.Settle(pos, n);
            }
        }
    }

    int _settle;

    /// <summary>
    /// Picks where foot <paramref name="i"/> comes down: <paramref name="lead"/> ahead of its neutral spot,
    /// unless the ground there is beyond a step up or down (a boulder, a ledge, a pit): then nearer the
    /// hip, where it is not; and if nowhere is, at the step's limit.
    /// </summary>
    void Place(int i, Vector3 home, Vector3 lead, float level, GroundQuery ground)
    {
        Vector3 fallback = new Vector3(home.X, level, home.Z), fallbackN = Vector3.Up;
        foreach (float share in Shares)
        {
            Vector3 at = home + lead * share;
            if (!ground(at, out var pos, out var n)) continue;
            float rise = pos.Y - level;
            if (rise <= MaxStepUp && rise >= -MaxStepDown)
            {
                _target[i] = pos;
                _targetN[i] = n;
                return;
            }
            // (The last tried is under the hip itself: there, on the ground as it really is — the body is
            // going up or down it all the same — rather than a foot set down on the air.)
            fallback = pos;
            fallbackN = n;
        }
        _target[i] = fallback;
        _targetN[i] = fallbackN;
    }

    static readonly float[] Shares = { 1f, 0.5f, 0f };

    /// <summary>A planted foot the leg can no longer be standing on: far out of reach across, or well
    /// above or below the body's own level.</summary>
    bool Lost(MechLeg leg, Transform3D root)
    {
        float dy = leg.Contact.Y - root.Origin.Y;
        return HomeError(leg, root) > Reach * 1.6f || dy > MaxStepUp * 1.5f || dy < -MaxStepDown * 1.3f;
    }

    static float HomeError(MechLeg leg, Transform3D root)
    {
        Vector3 diff = leg.Contact - root * leg.HomeLocal;
        diff.Y = 0f;
        return diff.Length();
    }
}
