using Godot;

namespace MechSim;

/// <summary>
/// Foot mark left on the ground: a Decal ("Print") pressed into whatever ground is under it (it projects
/// only onto the terrain's render layer, see MapTerrain.GroundLayer), stays for a while, then fades out
/// and frees itself. The map's scene sets its colour; the mech sets its shape (see Mech.FootprintTexture).
/// </summary>
public partial class Footprint : Node3D
{
	[Export] public float Lifetime = 30f;
	[Export] public float FadeTime = 6f;

	/// <summary>Gives the print the shape of this foot: its picture, the size it covers and where the foot's middle is on it.</summary>
	public void Shape(Texture2D mask, Vector2 size, Vector2 centre)
	{
		if (GetNodeOrNull<Decal>("Print") is not { } print || mask == null) return;
		print.TextureAlbedo = mask;
		print.Size = new Vector3(size.X, print.Size.Y, size.Y);
		print.Position = new Vector3(centre.X, 0f, centre.Y);
	}

	public override void _Ready()
	{
		// A tween instead of per-frame script work: there can be hundreds of these.
		if (GetNodeOrNull<Decal>("Print") is not { } print) return;
		var tween = CreateTween();
		tween.TweenInterval(Mathf.Max(0.1f, Lifetime - FadeTime));
		tween.TweenProperty(print, "albedo_mix", 0f, FadeTime);
		tween.Parallel().TweenProperty(print, "modulate:a", 0f, FadeTime);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
