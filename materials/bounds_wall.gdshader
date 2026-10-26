// The edge of the battlefield, shown in the mission editor and to the console's free camera (BoundsWall):
// a glowing curtain rising from the ground, brightest at its foot, with bands running up it.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform vec3 color : source_color = vec3(1.0, 0.45, 0.15);
uniform float strength = 0.9;

void fragment() {
	float up = UV.y;
	float bands = 0.55 + 0.45 * smoothstep(0.4, 0.6, fract(UV.x * 0.08 - TIME * 0.25));
	float foot = smoothstep(0.0, 0.03, up) * (1.0 - smoothstep(0.03, 1.0, up));
	ALBEDO = color * strength * (foot * 1.4 + (1.0 - up) * 0.25) * bands;
}
