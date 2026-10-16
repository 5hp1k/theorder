// The straight streak of the gauss slug itself (scenes/fx/gauss_trail.tscn): bright for an instant,
// gone long before the spiral round it.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

instance uniform float age = 0.0;
instance uniform vec4 tint : source_color = vec4(0.55, 0.8, 1.0, 1.0);
uniform float fade = 0.18;
uniform float brightness = 6.0;

void fragment() {
	// Brightest down the middle of the rod as seen.
	float edge = abs(dot(NORMAL, VIEW));
	ALBEDO = mix(tint.rgb, vec3(1.0), 0.5) * edge * exp(-age / fade) * brightness;
}
