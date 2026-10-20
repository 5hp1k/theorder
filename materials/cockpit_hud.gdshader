// The head-up display of a cockpit (made by CockpitDisplays): the HUD's sight, compass and warnings,
// light on the combiner glass in front of the pilot — glowing, and see-through where nothing is drawn.
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"

uniform sampler2D picture : source_color, filter_linear;
uniform float glow = 1.6;
uniform float opacity : hint_range(0.0, 1.0) = 0.85;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	vec4 c = texture(picture, UV);
	// (A viewport with a transparent background keeps its colours multiplied by their alpha.)
	vec3 rgb = c.rgb / max(c.a, 0.001);
	ALBEDO = rgb * glow;
	// It is light: the dark backing of the HUD's panels hardly shows on the glass.
	ALPHA = c.a * opacity * clamp(max(rgb.r, max(rgb.g, rgb.b)) * 3.0, 0.4, 1.0);
}
