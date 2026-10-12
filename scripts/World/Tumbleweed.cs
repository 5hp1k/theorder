using Godot;

namespace MechSim;

/// <summary>Rolls downwind over the dunes, hopping a little; wraps around to the far edge of the map.</summary>
public partial class Tumbleweed : Node3D
{
    [Export] public float Radius = 0.9f;
    /// <summary>How fast it goes relative to the wind.</summary>
    [Export] public float WindFactor = 0.8f;
    [Export] public float HopHeight = 0.7f;

    float _phase, _drift, _speedScale;

    public override void _Ready()
    {
        _phase = GD.Randf() * Mathf.Tau;
        _drift = (float)GD.RandRange(-0.25, 0.25);
        _speedScale = (float)GD.RandRange(0.7, 1.2);
    }

    public override void _Process(double delta)
    {
        var terrain = MapTerrain.Instance;
        if (terrain == null) return;
        float dt = (float)delta;

        Vector3 dir = Weather.Direction.Rotated(Vector3.Up, _drift);
        float speed = Weather.Speed * WindFactor * _speedScale;
        Vector3 p = GlobalPosition + dir * speed * dt;

        // Off the map: come back in from the upwind side.
        float half = terrain.Size * 0.5f - 5f;
        if (Mathf.Abs(p.X) > half || Mathf.Abs(p.Z) > half)
        {
            p -= dir * (terrain.Size - 20f);
            p.X = Mathf.Clamp(p.X, -half, half);
            p.Z = Mathf.Clamp(p.Z, -half, half);
        }

        _phase += speed / Radius * dt;
        float hop = Mathf.Abs(Mathf.Sin(_phase * 0.35f)) * HopHeight * Mathf.Clamp(speed / 8f, 0f, 1.5f);
        p.Y = terrain.HeightAt(p.X, p.Z) + Radius + hop;
        GlobalPosition = p;

        // Roll about the axis across the direction of travel.
        Vector3 axis = Vector3.Up.Cross(dir).Normalized();
        GlobalBasis = new Basis(axis, speed / Radius * dt) * GlobalBasis;
    }
}
