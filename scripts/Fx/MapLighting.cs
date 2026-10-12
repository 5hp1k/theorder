using Godot;

namespace MechSim;

/// <summary>
/// Sits on a map's WorldEnvironment. The environment in the scene is the best-looking version; this
/// switches off the costly parts of it according to the "effects" setting from the menu, and sets the
/// matching quality of the sun's shadows.
/// </summary>
public partial class MapLighting : WorldEnvironment
{
    /// <summary>The map's sun; its shadows are cheapened on the lower settings.</summary>
    [Export] public NodePath SunPath = "../Sun";

    public override void _Ready()
    {
        int quality = GameConfig.EffectsQuality;
        var env = Environment;
        if (env == null) return;
        // The scene's resource is shared by every load of the map: change a copy.
        env = (Environment)env.Duplicate();
        Environment = env;

        // High: everything. Medium: no bounced light, no light shafts in the haze. Low: plain fog and glow only.
        env.SdfgiEnabled &= quality >= 2;
        env.VolumetricFogEnabled &= quality >= 2;
        env.SsilEnabled &= quality >= 2;
        env.SsaoEnabled &= quality >= 1;

        if (GetNodeOrNull(SunPath) is DirectionalLight3D sun)
        {
            sun.DirectionalShadowMode = quality >= 1
                ? DirectionalLight3D.ShadowMode.Parallel4Splits
                : DirectionalLight3D.ShadowMode.Parallel2Splits;
            if (quality == 0)
            {
                sun.DirectionalShadowMaxDistance = Mathf.Min(sun.DirectionalShadowMaxDistance, 220f);
                sun.LightAngularDistance = 0f;
            }
        }
    }
}
