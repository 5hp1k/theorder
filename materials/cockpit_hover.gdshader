// The glow over a cockpit control under the free cursor (CockpitControls): a pulsing green rim, drawn
// over the control as an overlay — and squeezed in front with the rest of the cab (in_front.gdshaderinc).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"

uniform vec4 tint : source_color = vec4(0.45, 1.0, 0.55, 1.0);
uniform float strength = 0.9;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
	ALBEDO = tint.rgb * (0.25 + rim * 1.2) * strength * (0.7 + 0.3 * sin(TIME * 6.0));
}
