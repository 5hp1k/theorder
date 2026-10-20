// The mission editor's glow over what is selected (or under the mouse): a pulsing rim, drawn on top of
// the thing's own materials as an overlay.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;

uniform vec4 tint : source_color = vec4(1.0, 0.78, 0.2, 1.0);
uniform float strength = 1.0;
uniform float pulse = 1.0;

void fragment() {
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.2);
	float beat = mix(1.0, 0.7 + 0.3 * sin(TIME * 6.0), pulse);
	ALBEDO = tint.rgb * (0.16 + rim * 1.1) * strength * beat;
}
