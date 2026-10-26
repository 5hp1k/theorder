// The head-up display of a cockpit (made by CockpitDisplays): the HUD's sight, compass and warnings,
// light on the combiner glass in front of the pilot — glowing, and see-through where nothing is drawn.
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"
#include "res://materials/cockpit_interference.gdshaderinc"

uniform sampler2D picture : source_color, filter_linear;
uniform float glow = 1.6;
uniform float opacity : hint_range(0.0, 1.0) = 0.85;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.85;
// On the cab's own combiner glass: the picture is laid over the whole view, and the glass shows the part
// of it that lies behind it on the screen (so the sight stays on the aim point wherever the eye looks).
uniform bool collimated = false;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	vec2 uv = cab_distort(collimated ? SCREEN_UV : UV);
	vec4 c = texture(picture, uv);
	// (A viewport with a transparent background keeps its colours multiplied by their alpha.)
	vec3 rgb = c.rgb / max(c.a, 0.001);
	// It is light: the dark backing of the HUD's panels hardly shows on the glass.
	float core = c.a * opacity * clamp(max(rgb.r, max(rgb.g, rgb.b)) * 3.0, 0.4, 1.0);
	// A faint dark halo round every line, so the symbols read against snow, frost or a bright sky as well
	// as against the ground.
	vec2 px = 2.5 / vec2(textureSize(picture, 0));
	float halo = 0.0;
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.785398;
		halo = max(halo, texture(picture, uv + vec2(cos(a), sin(a)) * px).a);
	}
	halo *= halo_strength * opacity;
	float alpha = max(core, halo);
	ALBEDO = mix(vec3(0.0, 0.025, 0.01), cab_spoil(rgb, uv) * glow, core / max(alpha, 0.001));
	ALPHA = alpha * power;
}
