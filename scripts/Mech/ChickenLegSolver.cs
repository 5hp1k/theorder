using Godot;

namespace MechSim;

/// <summary>
/// Analytic IK for a digitigrade ("chicken walker") leg:
///
///   hip ─ thigh ─ knee ─ shin ─ heel ─ metatarsal ─ ankle(foot)
///
/// The thigh goes forward/down to a forward-pointing knee, the shin comes back down to the heel
/// (the visible "reversed knee"), and the metatarsal drops forward to the foot.
/// The metatarsal direction is given by the caller (it encodes foot roll / toe-off), so only the
/// hip→heel part is a real 2-bone problem, solved with the law of cosines.
/// </summary>
public static class ChickenLegSolver
{
    public struct Result
    {
        public Vector3 Hip, Knee, Heel, Ankle;
        /// <summary>False if the target was out of reach and the leg was clamped.</summary>
        public bool Reached;
    }

    /// <param name="hip">Hip joint, world space.</param>
    /// <param name="ankle">Where the metatarsal should end (top of the foot), world space.</param>
    /// <param name="metaVec">Vector ankle→heel (length = metatarsal length).</param>
    /// <param name="thigh">Thigh length.</param>
    /// <param name="shin">Shin length.</param>
    /// <param name="pole">Direction the knee should bend towards (usually mech forward).</param>
    public static Result Solve(Vector3 hip, Vector3 ankle, Vector3 metaVec, float thigh, float shin, Vector3 pole)
    {
        Vector3 heel = ankle + metaVec;
        Vector3 toHeel = heel - hip;
        float d = toHeel.Length();
        Vector3 dir = d > 1e-5f ? toHeel / d : Vector3.Down;

        float maxReach = (thigh + shin) * 0.999f;
        float minReach = Mathf.Abs(thigh - shin) + 0.01f;
        bool reached = d <= maxReach && d >= minReach;
        d = Mathf.Clamp(d, minReach, maxReach);
        heel = hip + dir * d;

        // Angle at the hip between hip→heel and hip→knee.
        float cosA = Mathf.Clamp((thigh * thigh + d * d - shin * shin) / (2f * thigh * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);

        // Bend plane: component of the pole perpendicular to hip→heel.
        Vector3 bend = pole - dir * pole.Dot(dir);
        if (bend.LengthSquared() < 1e-8f)
            bend = Mathf.Abs(dir.Dot(Vector3.Up)) < 0.9f ? Vector3.Up - dir * dir.Dot(Vector3.Up) : Vector3.Forward;
        bend = bend.Normalized();

        Vector3 knee = hip + dir * (thigh * cosA) + bend * (thigh * sinA);

        return new Result
        {
            Hip = hip,
            Knee = knee,
            Heel = heel,
            Ankle = heel - metaVec,
            Reached = reached,
        };
    }
}
