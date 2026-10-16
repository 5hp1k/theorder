// A screen on the cockpit dash (made by CockpitDisplays): the picture of its instruments, lit by itself.
shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"

uniform sampler2D picture : source_color, filter_linear;
uniform float glow = 1.0;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	ALBEDO = texture(picture, UV).rgb * glow;
}
