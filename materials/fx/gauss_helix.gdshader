// The spiral a gauss slug leaves in the air (scenes/fx/gauss_trail.tscn), drawn on an open cylinder
// of radius 1 and height 1 stretched along the shot. It widens as it drifts apart and breaks up into
// pieces before it is gone. The script sets age, length and tint on each trail.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform float age = 0.0;
instance uniform float length = 100.0;
instance uniform vec4 tint : source_color = vec4(0.55, 0.8, 1.0, 1.0);

/// Seconds it lasts.
uniform float life = 1.8;
/// Metres along the shot for one turn of the spiral.
uniform float pitch = 1.5;
/// Radius when it is made, and how fast it widens (m/s, slowing down).
uniform float start_radius = 0.16;
uniform float spread = 0.9;
/// Width of the band, as a part of a turn.
uniform float width = 0.07;
uniform float brightness = 3.0;

float hash(float n) { return fract(sin(n * 12.9898) * 43758.5453); }

void vertex() {
	float r = start_radius + spread * (1.0 - exp(-age * 1.6)) / 1.6;
	VERTEX.xz *= r;
}

void fragment() {
	float turns = UV.y * length / pitch;
	float f = fract(UV.x + turns + age * 0.35);
	float d = min(f, 1.0 - f);
	// Far off the bands are thinner than a pixel: blurred into a dimmer, wider one instead of flickering.
	float aa = max(width, fwidth(UV.x + turns) * 1.5);
	float band = (1.0 - smoothstep(aa * 0.5, aa, d)) * (width / aa);
	float t = clamp(age / life, 0.0, 1.0);
	// Whole for the first quarter of its life, then pieces of a quarter turn go out one after another.
	float k = clamp((t - 0.25) / 0.75, 0.0, 1.0) * 1.15;
	float piece = hash(floor(turns * 4.0) + 17.0);
	float left = smoothstep(k - 0.15, k, piece);
	// Fades in over the first metres from the muzzle and out at the far end.
	float ends = smoothstep(0.0, 3.0, UV.y * length) * smoothstep(0.0, 1.5, (1.0 - UV.y) * length);
	float glow = band * left * ends * pow(1.0 - t, 1.6) * brightness;
	ALBEDO = tint.rgb * glow;
}
