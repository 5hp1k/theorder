// Painted metal of a war machine. The paint (a flat colour, or a camouflage pattern) is wrapped round
// the part from three sides, so the parts need no texture coordinates. It wears through to bare metal
// on the sharp edges and in scattered chips, and dirt sits in the seams and in blotches. Where the
// edges and seams are is written into the mesh's vertex colours by ArmorBoxMesh / ArmorTubeMesh
// (red: 0 = edge, green: 0 = recess); a mesh without colours is simply painted all over.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

group_uniforms paint;
uniform vec3 paint_color : source_color = vec3(1.0);
// Camouflage pattern; white = just the colour above.
uniform sampler2D paint_tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
// Repeats of the pattern per metre.
uniform float paint_scale = 0.22;
uniform float paint_roughness : hint_range(0.0, 1.0) = 0.5;
uniform float paint_metallic : hint_range(0.0, 1.0) = 0.1;

group_uniforms pattern;
// A pattern drawn over the paint, in three tones: paint_color, pattern_a, pattern_b.
// 0 = none (the paint is paint_color times paint_tex), 1 = blotches, 2 = digital, 3 = tiger stripes,
// 4 = splinter, 5 = slanting bands, 6 = flecks, 7 = chequers, 8 = two tones, 9 = waves, 10 = brush
// strokes, 11 = spots, 12 = cracked.
uniform int pattern = 0;
uniform vec3 pattern_a : source_color = vec3(0.2, 0.25, 0.12);
uniform vec3 pattern_b : source_color = vec3(0.1, 0.1, 0.08);
// Size of the pattern's shapes, as a multiple of the usual.
uniform float pattern_size = 1.0;

group_uniforms surface;
// Fine relief of the metal under the paint.
uniform sampler2D surface_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D surface_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float surface_scale = 0.45;
uniform float surface_bump : hint_range(0.0, 2.0) = 0.6;

group_uniforms wear;
uniform vec3 metal_color : source_color = vec3(0.46, 0.44, 0.42);
// 0 = fresh from the factory, 1 = paint gone from every edge and chipped all over.
uniform float wear : hint_range(0.0, 1.0) = 0.55;
uniform vec3 dirt_color : source_color = vec3(0.13, 0.11, 0.09);
uniform float dirt : hint_range(0.0, 1.0) = 0.45;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
/// Mud and dust the part has picked up in the battle (set per part by the game, see Grime.cs), 0..1.
instance uniform float grime_level : hint_range(0.0, 1.0) = 0.0;
/// How wet the part is (just out of the water), 0..1: darker and glossier while it dries.
instance uniform float wetness : hint_range(0.0, 1.0) = 0.0;

varying vec3 part_pos;
varying vec3 part_normal;
varying mat3 to_view;

void vertex() {
	part_pos = VERTEX;
	part_normal = NORMAL;
	to_view = MODELVIEW_NORMAL_MATRIX;
}

// A texture seen from the three sides of the part and mixed by which way the surface faces.
#define three_sides(tex, p, w) (texture(tex, (p).zy).rgb * (w).x + texture(tex, (p).xz).rgb * (w).y + texture(tex, (p).xy).rgb * (w).z)

float hash3(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.x + p.y) * p.z);
}

float noise3(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm3(vec3 p) {
	return noise3(p) * 0.55 + noise3(p * 2.1 + 5.3) * 0.3 + noise3(p * 4.3 + 9.1) * 0.15;
}

// Which of the three tones the pattern has at a point of the part: 0, 1 or 2.
float pattern_tone(vec3 p) {
	p /= max(pattern_size, 0.05);
	if (pattern == 1) {            // blotches
		float n = fbm3(p * 1.1);
		return n < 0.44 ? 0.0 : (n < 0.56 ? 1.0 : 2.0);
	} else if (pattern == 2) {     // digital: the same, in little squares
		vec3 q = floor(p * 5.0) / 5.0;
		float n = fbm3(q * 1.1) + (hash3(floor(p * 5.0)) - 0.5) * 0.1;
		return n < 0.44 ? 0.0 : (n < 0.56 ? 1.0 : 2.0);
	} else if (pattern == 3) {     // tiger stripes: wavering bands across the part
		float s = sin(p.y * 5.0 + p.x * 1.4 + (noise3(p * 1.4) - 0.5) * 7.0);
		return s > 0.5 ? 1.0 : (s < -0.82 ? 2.0 : 0.0);
	} else if (pattern == 4) {     // splinter: shards cut by three families of planes
		vec3 v = vec3(floor(dot(p, vec3(0.9, 0.5, 0.3)) * 1.5), floor(dot(p, vec3(-0.4, 0.8, 0.6)) * 1.5 + 0.3), floor(dot(p, vec3(0.3, -0.5, 0.9)) * 1.5 + 0.7));
		float h = hash3(v + 3.1);
		return h < 0.45 ? 0.0 : (h < 0.75 ? 1.0 : 2.0);
	} else if (pattern == 5) {     // slanting bands, a thin line of the third tone between them
		float s = fract(dot(p, normalize(vec3(1.0, 1.0, 0.35))) * 0.9);
		return s < 0.46 ? 0.0 : (s < 0.5 ? 2.0 : (s < 0.96 ? 1.0 : 2.0));
	} else if (pattern == 6) {     // flecks of two colours, gathered into drifts
		float drift = noise3(p * 1.0) * 0.6;
		if (noise3(p * 5.0) + drift > 0.98) return 1.0;
		return noise3(p * 5.0 + 17.0) + drift > 1.02 ? 2.0 : 0.0;
	} else if (pattern == 7) {     // chequers
		vec3 c = floor(p * 1.6);
		return mod(c.x + c.y + c.z, 2.0);
	} else if (pattern == 8) {     // two tones: dark below a wavering line, a stripe of the third along it
		float line = p.y + (noise3(p * vec3(0.9, 0.0, 0.9)) - 0.5) * 0.9;
		return line > 0.12 ? 0.0 : (line > -0.02 ? 2.0 : 1.0);
	} else if (pattern == 9) {     // waves running along the part
		float s = sin(p.y * 6.0 + sin(p.z * 2.2 + p.x * 1.1) * 2.2);
		return s > 0.35 ? 1.0 : (s < -0.7 ? 2.0 : 0.0);
	} else if (pattern == 10) {    // brush strokes: long streaks laid on at a slant
		vec3 q = vec3(dot(p, vec3(0.8, 0.6, 0.0)) * 0.45, dot(p, vec3(-0.6, 0.8, 0.0)) * 2.6, p.z * 1.2);
		float n = fbm3(q);
		return n < 0.46 ? 0.0 : (n < 0.58 ? 1.0 : 2.0);
	} else if (pattern == 11) {    // spots: round dots in rows, every other one in the third tone
		vec3 cell = floor(p * 1.8);
		float d = length(fract(p * 1.8) - 0.5);
		if (d > 0.3) return 0.0;
		return mod(cell.x + cell.y * 2.0 + cell.z, 3.0) < 1.5 ? 1.0 : 2.0;
	} else if (pattern == 12) {    // cracked: thin dark veins between plates of the base colour
		float a = abs(noise3(p * 1.7) - 0.5), b = abs(noise3(p * 1.7 + 31.0) - 0.5);
		return min(a, b) < 0.022 ? 1.0 : (noise3(p * 0.6 + 7.0) > 0.68 ? 2.0 : 0.0);
	}
	return 0.0;
}

void fragment() {
	vec3 n = normalize(part_normal);
	vec3 w = pow(abs(n), vec3(6.0));
	w /= w.x + w.y + w.z;

	vec3 paint;
	if (pattern == 0) {
		paint = three_sides(paint_tex, part_pos * paint_scale, w) * paint_color;
	} else {
		float tone = pattern_tone(part_pos);
		paint = tone < 0.5 ? paint_color : (tone < 1.5 ? pattern_a : pattern_b);
	}
	vec3 arm = three_sides(surface_arm, part_pos * surface_scale, w);
	vec3 coarse = three_sides(noise_tex, part_pos * 0.11, w);
	vec3 fine = three_sides(noise_tex, part_pos * 0.9, w);

	float edge = 1.0 - COLOR.r;
	float recess = 1.0 - COLOR.g;

	// Bare metal: along the edges, raggedly, and in small chips where the noise peaks.
	float bare = smoothstep(0.55, 0.75, edge * 0.8 + fine.r * 0.45 + coarse.b * 0.15 - (1.0 - wear) * 0.6);
	bare = max(bare, smoothstep(0.93 - wear * 0.1, 0.97, fine.g * 0.7 + coarse.r * 0.3 + 0.12) * wear);
	// Paint fades and dulls unevenly.
	paint *= 0.85 + 0.3 * coarse.g;

	// Dirt: in the seams, and in broad stains.
	float grime = clamp(recess * 1.1 + smoothstep(0.45, 0.9, coarse.r * 0.7 + fine.b * 0.3) * dirt, 0.0, 1.0);
	grime *= mix(0.5, 1.0, dirt) * step(0.001, dirt + recess);
	// Mud from the battle: first in blotches, then over the whole plate.
	grime = max(grime, min(1.0, grime_level * 2.5) * smoothstep(0.45, 0.75, coarse.r * 0.6 + fine.b * 0.4 + grime_level * 0.6));

	vec3 albedo = mix(paint, metal_color * (0.7 + 0.5 * arm.g), bare);
	float metallic = mix(paint_metallic, 0.95, bare);
	float rough = mix(clamp(paint_roughness * (0.75 + 0.5 * arm.g), 0.05, 1.0), 0.32 + 0.2 * fine.b, bare);
	albedo = mix(albedo, dirt_color, grime * 0.75);
	metallic *= 1.0 - grime * 0.8;
	rough = mix(rough, 0.92, grime);
	albedo *= 1.0 - wetness * 0.28;
	rough = mix(rough, 0.12, wetness * 0.75);

	// Relief, from the same three sides.
	vec2 bx = texture(surface_normal, part_pos.zy * surface_scale).rg * 2.0 - 1.0;
	vec2 by = texture(surface_normal, part_pos.xz * surface_scale).rg * 2.0 - 1.0;
	vec2 bz = texture(surface_normal, part_pos.xy * surface_scale).rg * 2.0 - 1.0;
	vec3 bumped = n + (vec3(0.0, bx.y, bx.x) * w.x + vec3(by.x, 0.0, by.y) * w.y + vec3(bz.x, bz.y, 0.0) * w.z) * surface_bump * (1.0 - grime * 0.5);

	ALBEDO = albedo;
	METALLIC = metallic;
	ROUGHNESS = rough;
	SPECULAR = 0.5;
	AO = arm.r * (1.0 - recess * 0.55);
	AO_LIGHT_AFFECT = 0.4;
	NORMAL = normalize(to_view * normalize(bumped));
}
