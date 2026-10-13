// The ball of fire of a nuclear explosion, on a sphere: white-hot at first, mottled and reddening as it
// cools, soft at its edge. Added onto the picture. NukeBlast sets heat and alpha as it burns out.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_back, shadows_disabled, unshaded;

uniform vec3 hot_color : source_color = vec3(1.0, 0.96, 0.86);
uniform vec3 cool_color : source_color = vec3(1.0, 0.3, 0.04);
uniform float energy = 9.0;
/// How strongly the surface boils once it has cooled a little.
uniform float mottle : hint_range(0.0, 1.0) = 0.6;

/// 1 → 0 as it cools.
instance uniform float heat = 1.0;
/// 1 → 0 as it gives way to the cloud.
instance uniform float alpha = 1.0;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec2 q = UV * vec2(14.0, 7.0) + vec2(TIME * 0.05, -TIME * 0.2);
	float boil = noise(q) * 0.6 + noise(q * 2.3 + 4.0) * 0.4;
	float cooled = 1.0 - heat;
	vec3 colour = mix(cool_color, hot_color, clamp(heat * 1.3 - boil * cooled * mottle, 0.0, 1.0));
	ALBEDO = colour * energy * mix(0.25, 1.0, heat) * (1.0 - cooled * mottle * boil);
	ALPHA = alpha * smoothstep(0.0, 0.45, facing);
}
