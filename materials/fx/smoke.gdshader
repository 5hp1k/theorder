// A puff of smoke or dust for particles: not a soft dot but a ragged, billowing lump. Its outline and
// its inner folds come from noise that is different for every puff and churns as the puff ages; it
// is lit like a ball, so smoke has a bright side and a dark one, and it thins out raggedly as it dies.
// The particle's colour ramp gives the colour and the overall fade.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_disabled, diffuse_lambert_wrap;

/// How ragged the edge is.
uniform float raggedness : hint_range(0.0, 1.0) = 0.6;
/// How much darker the folds inside are.
uniform float folds : hint_range(0.0, 1.0) = 0.5;
/// How fast the lump churns.
uniform float churn = 0.35;
/// Fraction of its light that is its own (so smoke isn't black at night next to a fire).
uniform float glow : hint_range(0.0, 1.0) = 0.06;

varying float seed;
varying float age;

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

float fbm(vec2 p) {
	return noise(p) * 0.5 + noise(p * 2.1 + 3.7) * 0.3 + noise(p * 4.3 + 8.1) * 0.2;
}

void vertex() {
	seed = hash(vec2(float(INSTANCE_ID), 7.0));
	age = INSTANCE_CUSTOM.y;
	// Facing the camera, keeping the particle's size.
	mat4 face = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	face = face * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * face;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	// Each puff is turned its own way and turns slowly.
	float turn = seed * 6.283 + age * (seed - 0.5) * 2.0;
	p = mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn))) * p;
	float r = length(p);
	vec2 q = p * 1.7 + seed * 40.0 + vec2(0.0, -age * churn * 3.0);
	float lump = fbm(q);
	float fine = fbm(q * 2.6 + 11.0);
	// The outline.
	float edge = 0.78 - raggedness * 0.45 * (1.0 - lump);
	float body = smoothstep(edge, edge * 0.45, r);
	// Dying puffs are eaten away from the thin parts.
	body *= smoothstep(age * 1.15 - 0.35, age * 1.15 + 0.1, lump * 0.7 + fine * 0.3 + (1.0 - r) * 0.35);
	// A ball's normal, buckled by the folds: light and shade.
	vec3 n = normalize(vec3(p + (vec2(lump, fine) - 0.5) * 0.9, sqrt(max(1.0 - min(r * r, 1.0), 0.02))));
	NORMAL = n;
	vec3 colour = COLOR.rgb * (1.0 - folds * (1.0 - fine) * 0.7);
	ALBEDO = colour;
	EMISSION = colour * glow;
	ROUGHNESS = 1.0;
	ALPHA = body * COLOR.a;
}
