using Godot;

namespace MechSim;

/// <summary>Something a launcher throws: a guided missile, a bomb. The weapon instances the scene and calls Launch.</summary>
public abstract partial class Projectile : Node3D
{
    /// <param name="carrierVelocity">Velocity of whoever fired it (bombs keep it).</param>
    public abstract void Launch(Vector3 from, Vector3 aimPoint, ITarget target, float damage, float range, uint mask,
                                Godot.Collections.Array<Rid> exclude, Vector3 carrierVelocity);
}
