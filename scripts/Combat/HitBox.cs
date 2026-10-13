using System.Collections.Generic;
using Godot;

namespace MechSim;

/// <summary>
/// Collision volume of one section, placed in the mech scene. Weapons hit this and the damage goes to
/// the owner's model. Meshes parented under the box are the section's armour plates: they get charred
/// when the section is destroyed.
/// A kinematic body, not a static one: it rides on the mech every tick, and moving a static body makes
/// the physics engine re-sort everything that never moves (thousands of trees and rocks).
/// </summary>
public partial class HitBox : AnimatableBody3D, IDamageable
{
    [Export] public Section Section;

    /// <summary>The mech this box belongs to.</summary>
    public Node Unit { get; private set; }

    DamageModel _model;
    uint _layer;
    readonly List<MeshInstance3D> _meshes = new();
    readonly List<Material> _original = new();

    /// <summary>World position of the section (centre of its first collision shape).</summary>
    public Vector3 Center
    {
        get
        {
            foreach (Node child in GetChildren())
                if (child is CollisionShape3D shape) return shape.GlobalPosition;
            return GlobalPosition;
        }
    }

    public void Init(Node unit, DamageModel model, uint layer)
    {
        Unit = unit;
        _model = model;
        _layer = layer;
        CollisionLayer = layer;
        CollisionMask = 0;
        Geo.Collect(this, _meshes);
        foreach (var mesh in _meshes) _original.Add(mesh.MaterialOverride);
    }

    public void SetEnabled(bool on) => CollisionLayer = on ? _layer : 0;

    public void SetCharred(Material charred)
    {
        foreach (var mesh in _meshes) mesh.MaterialOverride = charred;
    }

    /// <summary>The material the section's plating wears right now (what its debris is made of).</summary>
    public Material PlateMaterial => _meshes.Count > 0 ? _meshes[0].MaterialOverride : null;

    public void Restore()
    {
        for (int i = 0; i < _meshes.Count; i++) _meshes[i].MaterialOverride = _original[i];
        SetEnabled(true);
    }

    public void TakeDamage(float amount, Vector3 point)
    {
        Score.Hit(Unit as ITarget);
        NetLink.I?.QueueHurt(Unit, (int)Section, amount);
        _model?.Apply(Section, amount);
    }
}
