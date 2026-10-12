using Godot;

namespace MechSim;

/// <summary>Collision layer bits (named in Project Settings → Layer Names), raycasts and damage delivery.</summary>
public static class Phys
{
    public const uint LayerWorld = 1u << 0;     // terrain, rocks
    public const uint LayerMech = 1u << 1;      // player mech (solid body)
    public const uint LayerTargets = 1u << 2;   // enemy mechs (solid body)
    public const uint LayerHitPlayer = 1u << 3; // player's hit boxes
    public const uint LayerHitEnemy = 1u << 4;  // enemies' hit boxes
    public const uint LayerProps = 1u << 5;     // buildings, trees, cacti: stop shots and infantry, not mechs
    public const uint LayerWreck = 1u << 6;     // ragdoll parts of destroyed mechs
    public const uint LayerVehicles = 1u << 7;  // ground vehicles and ships (solid body)

    public const uint MaskPlayerFire = LayerWorld | LayerProps | LayerWreck | LayerHitEnemy;
    public const uint MaskEnemyFire = LayerWorld | LayerProps | LayerWreck | LayerHitPlayer;
    public const uint MaskFeet = LayerWorld;

    public static readonly Godot.Collections.Array<Rid> NoExclude = new();

    public struct Hit
    {
        public Vector3 Position;
        public Vector3 Normal;
        public GodotObject Collider;
        /// <summary>Which piece of the collider was hit, for things made of pieces (a building's block).</summary>
        public int Part;
    }

    /// <summary>
    /// Something with holes in it: its collision shape is cruder than what is really there, so a ray that
    /// reaches the shape asks it where (and whether) it actually lands.
    /// </summary>
    public interface IPenetrable
    {
        /// <returns>False if the ray goes clean through.</returns>
        bool Refine(Vector3 from, Vector3 to, ref Hit hit);
    }

    public static bool Ray(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, uint mask,
                           Godot.Collections.Array<Rid> exclude, out Hit hit)
    {
        hit = default;
        // A shot can pass through several broken-open things in a row; each one is skipped in turn.
        for (int pass = 0; pass < 6; pass++)
        {
            var query = PhysicsRayQueryParameters3D.Create(from, to, mask, exclude);
            var result = space.IntersectRay(query);
            if (result.Count == 0) return false;
            hit.Position = result["position"].AsVector3();
            hit.Normal = result["normal"].AsVector3();
            hit.Collider = result["collider"].AsGodotObject();
            hit.Part = -1;
            if (hit.Collider is not IPenetrable holed || holed.Refine(from, to, ref hit)) return true;

            exclude = exclude.Duplicate();
            exclude.Add(result["rid"].AsRid());
        }
        return false;
    }

    /// <summary>Delivers a hit to whatever a ray found: the block of a building, or the thing as a whole.</summary>
    public static void Damage(in Hit hit, float amount)
    {
        // The masonry flies back out towards the shooter, off the face that was hit.
        if (hit.Collider is ChunkedBuilding building && hit.Part >= 0) building.DamageCell(hit.Part, amount, hit.Normal * 7f);
        else (hit.Collider as IDamageable)?.TakeDamage(amount, hit.Position);
    }

    /// <summary>Area damage: everything hurtable within the radius, full at the centre, a quarter at the edge.</summary>
    public static void Blast(PhysicsDirectSpaceState3D space, Vector3 center, float radius, float damage, uint mask,
                             Godot.Collections.Array<Rid> exclude)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollisionMask = mask,
            Exclude = exclude,
        };
        foreach (var result in space.IntersectShape(query, 64))
        {
            var collider = result["collider"].AsGodotObject();
            if (collider is ChunkedBuilding building)
            {
                // Takes a bite out of the building rather than hurting "the building".
                building.Blast(center, radius, damage);
                continue;
            }
            if (collider is not IDamageable victim) continue;
            Vector3 at = victim is Node3D node ? node.GlobalPosition : center;
            float falloff = Mathf.Lerp(1f, 0.25f, Mathf.Clamp(at.DistanceTo(center) / radius, 0f, 1f));
            victim.TakeDamage(damage * falloff, center);
        }
    }
}

/// <summary>Anything weapons can hurt.</summary>
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 point);
}
