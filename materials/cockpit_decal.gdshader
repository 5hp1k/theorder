// A stencilled label in the cockpit (CockpitControls): paint on a panel, lit like the panel, drawn
// squeezed in front with the rest of the cab (in_front.gdshaderinc) — so nothing shows it through itself.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"

uniform sampler2D picture : source_color, filter_linear_mipmap;
uniform vec3 tint : source_color = vec3(0.86, 0.86, 0.78);
uniform float glow = 0.0;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	vec4 c = texture(picture, UV);
	ALBEDO = tint * c.rgb;
	ROUGHNESS = 0.8;
	EMISSION = tint * glow * c.a;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.4;
}
