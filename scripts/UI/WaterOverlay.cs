using Godot;

namespace MechSim;

/// <summary>
/// Full-screen water effect, drawn under the instruments: the view swims and turns murky while the
/// camera is below the surface, and for a few seconds after coming up water runs down the lens.
/// The look is the shader on this node's material (materials/underwater.gdshader).
/// </summary>
public partial class WaterOverlay : ColorRect
{
    /// <summary>Seconds the lens stays wet after surfacing.</summary>
    [Export] public float DryTime = 3f;

    float _under, _wet;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var terrain = MapTerrain.Instance;
        var cam = GetViewport().GetCamera3D();
        bool submerged = false;
        if (terrain != null && terrain.HasWater && cam != null)
        {
            Vector3 p = cam.GlobalPosition;
            submerged = p.Y < terrain.WaterLevel && terrain.WaterDepthAt(p.X, p.Z) > 0f;
        }

        _under = Mathf.MoveToward(_under, submerged ? 1f : 0f, dt * 4f);
        if (submerged) _wet = 1f;
        else _wet = Mathf.MoveToward(_wet, 0f, dt / Mathf.Max(DryTime, 0.1f));

        Visible = _under > 0.01f || _wet > 0.01f;
        if (!Visible || Material is not ShaderMaterial shader) return;
        shader.SetShaderParameter("amount", _under);
        shader.SetShaderParameter("wet", _wet * (1f - _under));
    }
}
