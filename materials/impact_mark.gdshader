// What a hit leaves on the ground: a charred core with a ragged edge, soot thrown out in streaks,
// spilled earth round it, and embers that glow for a few seconds. All of it drawn from noise, so no
// two marks are the same (seed) and nothing reads as a ring. Three materials use it: mark_blast,
// mark_bullet, mark_burn.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, shadows_disabled, specular_disabled;

/// Radius of the charred core, in mark half-widths.
uniform float core_size : hint_range(0.0, 1.0) = 0.34;
/// How ragged the core's edge is.
uniform float core_ragged : hint_range(0.0, 1.0) = 0.5;
uniform vec3 char_color : source_color = vec3(0.035, 0.028, 0.022);
/// Soot blown outwards from the core.
uniform float soot_strength : hint_range(0.0, 1.0) = 0.7;
uniform float soot_reach : hint_range(0.0, 1.0) = 0.9;
/// Streaks in the soot: how many, and how much they stand out.
uniform float ray_count = 17.0;
uniform float ray_strength : hint_range(0.0, 1.0) = 0.75;
/// Earth (or chips of whatever was hit) thrown out round the core.
uniform vec3 spill_color : source_color = vec3(0.33, 0.28, 0.22);
uniform float spill_strength : hint_range(0.0, 1.0) = 0.5;
uniform float spill_reach : hint_range(0.0, 1.0) = 0.6;
/// Embers in the core while it's hot.
uniform vec3 glow_color : source_color = vec3(1.0, 0.36, 0.06);
uniform float glow_energy = 3.0;

instance uniform float seed = 0.0;
/// 1 → 0 as the mark weathers away.
instance uniform float fade = 1.0;
/// 1 → 0 as it cools.
instance uniform float heat = 0.0;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21) + seed * 0.137);
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	return noise(p) * 0.5 + noise(p * 2.03 + 7.1) * 0.3 + noise(p * 4.1 + 3.7) * 0.2;
}

// Noise round the circle that joins up with itself.
float ring_noise(float angle, float count) {
	float a = angle / 6.2831853 + 0.5;
	float x = a * count, i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(hash(vec2(mod(i, count), 3.0)), hash(vec2(mod(i + 1.0, count), 3.0)), f);
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float r = length(p), angle = atan(p.y, p.x);
	float grain = fbm(p * 5.0 + seed);
	float fine = fbm(p * 17.0 - seed);

	// The core: its edge wanders in and out, and breaks up into specks.
	float edge = core_size * (1.0 + core_ragged * (ring_noise(angle, 7.0) - 0.5) * 1.1 + core_ragged * (grain - 0.5) * 0.9);
	float core = smoothstep(edge, edge * 0.62, r);
	core *= mix(1.0, smoothstep(0.2, 0.55, fine + (1.0 - r / max(edge, 0.001)) * 0.6), core_ragged);

	// Soot: long uneven streaks, each reaching its own distance, thinning as they go.
	float streak = ring_noise(angle, ray_count) * 0.6 + ring_noise(angle + 1.7, ray_count * 2.7) * 0.4;
	streak = mix(1.0, smoothstep(0.25, 0.85, streak), ray_strength);
	float reach = mix(core_size, 1.0, soot_reach) * mix(0.3, 1.0, streak * streak);
	float soot = soot_strength * pow(smoothstep(reach, core_size * 0.6, r), 1.4) * mix(0.6, 1.0, grain) * mix(0.45, 1.0, streak);

	// Spill: clods and dust just outside the core, in specks.
	float spill_edge = mix(core_size, 1.0, spill_reach) * (0.7 + 0.6 * ring_noise(angle + 0.6, 11.0));
	float spill = smoothstep(spill_edge, core_size * 0.8, r) * smoothstep(core_size * 0.5, core_size * 1.0, r);
	spill *= spill_strength * smoothstep(0.5, 0.72, fbm(p * 41.0 + seed) + (grain - 0.5) * 0.5);

	float dark = max(core, soot);
	float alpha = max(dark, spill) * smoothstep(1.0, 0.86, r) * fade;
	vec3 color = mix(char_color * mix(1.6, 1.0, core), spill_color * mix(0.7, 1.15, fine), spill * (1.0 - core) / max(max(dark, spill), 0.001));

	// Embers sit in the cracks of the char and die from the outside in.
	float ember = core * smoothstep(1.2 - heat * 0.65, 1.35 - heat * 0.35, fine * 0.9 + (1.0 - r / max(edge, 0.001)) * 0.55);

	ALBEDO = color;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	EMISSION = glow_color * glow_energy * ember * heat;
	ALPHA = alpha;
}
