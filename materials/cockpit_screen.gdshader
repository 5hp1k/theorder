// A screen on the cockpit dash (made by CockpitDisplays): the picture of its instruments, lit by itself.
shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;
#include "res://materials/in_front.gdshaderinc"
#include "res://materials/cockpit_interference.gdshaderinc"

uniform sampler2D picture : source_color, filter_linear;
uniform float glow = 1.0;
// The strip along the bottom naming the soft keys (a share of the height): it stays lit while the reactor
// runs, even with the engine (and its generator, which the rest of the screen hangs on) off.
uniform float strip = 0.0;
uniform float strip_power = 1.0;

void vertex() {
	IN_FRONT_VERTEX
}

void fragment() {
	vec2 uv = cab_distort(UV);
	vec3 c = texture(picture, uv).rgb;
	// (the colours pulled apart a little when it is bad)
	c.r = texture(picture, uv + vec2(0.007 * noise, 0.0)).r;
	// a dead screen is dark glass, not a hole
	float lit = UV.y > 1.0 - strip ? max(power, strip_power) : power;
	ALBEDO = max(cab_spoil(c, uv) * glow * lit, vec3(0.012, 0.014, 0.013));
}
