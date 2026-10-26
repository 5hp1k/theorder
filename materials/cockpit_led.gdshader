// A status lamp in the cockpit (CockpitControls lights it with "lit"): a coloured lens, glowing when on.
// Squeezed in front with the rest of the cab (in_front.gdshaderinc).
shader_type spatial;
render_mode cull_back, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"

uniform vec3 color : source_color = vec3(0.35, 1.0, 0.45);
instance uniform float lit : hint_range(0.0, 1.0) = 0.0;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	ALBEDO = color * mix(0.08, 0.3, lit);
	ROUGHNESS = 0.2;
	SPECULAR = 0.7;
	EMISSION = color * lit * 3.0;
}
