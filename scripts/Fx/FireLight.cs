using Godot;

namespace MechSim;

/// <summary>Makes a light flicker like a fire.</summary>
public partial class FireLight : OmniLight3D
{
    [Export] public float Flicker = 0.35f;

    float _base, _time;

    public override void _Ready()
    {
        _base = LightEnergy;
        _time = GD.Randf() * 10f;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        float n = Mathf.Sin(_time * 13f) * 0.5f + Mathf.Sin(_time * 7.3f + 1f) * 0.3f + Mathf.Sin(_time * 23f) * 0.2f;
        LightEnergy = _base * (1f + n * Flicker);
    }
}
