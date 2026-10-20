using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>The body of one square of a <see cref="ForestField"/>: weapons hit it, and it hands the blow to the tree struck.</summary>
public partial class ForestChunkBody : StaticBody3D, IDamageable
{
    public ForestField Forest;
    public List<int> Trees;

    public void TakeDamage(float amount, Vector3 point) => Forest?.Strike(Trees, amount, point);
}
