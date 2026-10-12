// Ground of a map. Four surfaces are laid over one another by the lie of the land:
//   base   - what the ground is mostly made of (sand, grass, snow);
//   patch  - a second surface that breaks it up in broad irregular patches;
//   cliff  - bare rock, wherever the slope is steep;
//   shore  - what lies low, along the water (wet sand, cracked mud).
// All of them are mapped straight down in world space, so nothing stretches when a crater reshapes
// the mesh. Each is sampled at two sizes and the two are mixed with distance, which hides the
// repetition; a slow noise shifts the brightness across hundreds of metres for the same reason.
// The red vertex colour marks ground torn up by explosions.
// In a city the streets are painted on as well: a grid of asphalt roads with their markings,
// pavements beside them, and car parks on the blocks that CityLayout marks as such. Being part of
// the ground, the streets follow the hills.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

group_uniforms base;
uniform sampler2D base_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D base_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// R = occlusion, G = roughness.
uniform sampler2D base_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres one repeat of the texture covers.
uniform float base_size = 6.0;
uniform vec3 base_tint : source_color = vec3(1.0);

group_uniforms patch;
uniform sampler2D patch_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D patch_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D patch_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float patch_size = 7.0;
uniform vec3 patch_tint : source_color = vec3(1.0);
// Share of the ground the patches take, and how big they are, metres.
uniform float patch_amount : hint_range(0.0, 1.0) = 0.4;
uniform float patch_spread = 90.0;

group_uniforms cliff;
uniform sampler2D cliff_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D cliff_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D cliff_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float cliff_size = 14.0;
uniform vec3 cliff_tint : source_color = vec3(1.0);
// Slope (0 = level, 1 = vertical) where rock starts to show and where nothing else is left.
uniform float slope_start = 0.12;
uniform float slope_end = 0.3;

group_uniforms shore;
uniform sampler2D shore_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D shore_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D shore_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float shore_size = 5.0;
uniform vec3 shore_tint : source_color = vec3(1.0);
// Height below which the ground is all shore, and how far above that it fades out. Set by MapTerrain.
uniform float shore_level = -1000.0;
uniform float shore_fade = 2.5;
// Height of the water: ground near it and under it is dark and glistening.
uniform float water_level = -1000.0;
uniform float wetness : hint_range(0.0, 1.0) = 0.6;

group_uniforms heights;
// Above this height the patch surface takes over completely (snow on the peaks). Off by default.
uniform float peak_level = 100000.0;
uniform float peak_fade = 60.0;

group_uniforms variation;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
// How strongly brightness drifts over long distances.
uniform float macro_strength : hint_range(0.0, 1.0) = 0.22;
// The distant copy of each texture is this many times larger.
uniform float far_stretch = 5.3;
uniform float far_start = 18.0;
uniform float far_end = 110.0;
uniform float normal_strength = 1.0;
// Washes the pictures of the base and patch surfaces out towards their average colour, keeping
// their relief: for snow, whose photographs are full of twigs and tracks.
uniform float plain : hint_range(0.0, 1.0) = 0.0;
uniform vec3 scorch_color : source_color = vec3(0.07, 0.06, 0.05);

group_uniforms streets;
// Distance between streets; 0 = no streets. Set by CityLayout, like the next three.
uniform float street_pitch = 0.0;
uniform float street_width = 16.0;
// The streets run this far from the middle of the map, each way.
uniform float street_reach = 360.0;
// One pixel per block: red 0 = yards, 0.5 = a car park, 1 = a paved square.
uniform sampler2D block_map : filter_nearest, repeat_disable;
uniform sampler2D street_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D street_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D street_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float street_size = 6.0;
uniform vec3 street_tint : source_color = vec3(0.6);
uniform sampler2D walk_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D walk_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D walk_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float walk_size = 4.0;
uniform vec3 walk_tint : source_color = vec3(0.8);
// Width of the pavement beside a street.
uniform float walk_width = 4.0;
uniform vec3 marking_color : source_color = vec3(0.72, 0.71, 0.66);

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

struct Surface {
	vec3 albedo;
	vec2 bump;
	float ao;
	float rough;
};

Surface surface(sampler2D albedo, sampler2D normal, sampler2D arm, float size, vec3 tint, float far) {
	vec2 near_uv = world_pos.xz / size;
	// Turned a little as well as enlarged, so the two never line up.
	vec2 far_uv = (mat2(vec2(0.94, 0.34), vec2(-0.34, 0.94)) * world_pos.xz) / (size * far_stretch);
	vec3 a = mix(texture(albedo, near_uv).rgb, texture(albedo, far_uv).rgb, far);
	vec2 n = mix(texture(normal, near_uv).rg, texture(normal, far_uv).rg, far) * 2.0 - 1.0;
	vec2 m = mix(texture(arm, near_uv).rg, texture(arm, far_uv).rg, far);
	return Surface(a * tint, n, m.r, m.g);
}

Surface blend(Surface a, Surface b, float t) {
	// The darker, lower parts of the incoming surface arrive last: sand fills the cracks first.
	float lift = dot(b.albedo - a.albedo, vec3(0.33));
	float w = smoothstep(0.0, 1.0, clamp(t + lift * 0.9 * t * (1.0 - t) * 4.0, 0.0, 1.0));
	return Surface(mix(a.albedo, b.albedo, w), mix(a.bump, b.bump, w), mix(a.ao, b.ao, w), mix(a.rough, b.rough, w));
}

void fragment() {
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	float far = smoothstep(far_start, far_end, dist) * 0.9;

	vec3 broad = texture(noise_tex, world_pos.xz / (patch_spread * 4.0)).rgb;
	vec3 fine = texture(noise_tex, world_pos.xz / 37.0).rgb;

	Surface s = surface(base_albedo, base_normal, base_arm, base_size, base_tint, far);
	s.albedo = mix(s.albedo, textureLod(base_albedo, vec2(0.5), 10.0).rgb * base_tint, plain);

	float patches = smoothstep(1.0 - patch_amount - 0.12, 1.0 - patch_amount + 0.12, broad.r * 0.75 + fine.g * 0.25);
	patches = max(patches, smoothstep(peak_level, peak_level + peak_fade, world_pos.y + (broad.b - 0.5) * peak_fade));
	Surface other = surface(patch_albedo, patch_normal, patch_arm, patch_size, patch_tint, far);
	other.albedo = mix(other.albedo, textureLod(patch_albedo, vec2(0.5), 10.0).rgb * patch_tint, plain);
	s = blend(s, other, patches);

	float low = 1.0 - smoothstep(shore_level, shore_level + shore_fade, world_pos.y + (fine.r - 0.5) * 1.2);
	s = blend(s, surface(shore_albedo, shore_normal, shore_arm, shore_size, shore_tint, far), low);

	float slope = 1.0 - world_normal.y;
	float rock = smoothstep(slope_start, slope_end, slope + (fine.b - 0.5) * 0.06);
	s = blend(s, surface(cliff_albedo, cliff_normal, cliff_arm, cliff_size, cliff_tint, far), rock);

	if (street_pitch > 0.0) {
		vec2 p = world_pos.xz;
		vec2 street = round(p / street_pitch) * street_pitch;
		vec2 off = abs(p - street);                // distance to the nearest street running along Z (x) and along X (y)
		float half_w = street_width * 0.5;
		float inside = step(max(abs(p.x), abs(p.y)), street_reach + half_w);
		float dry = step(water_level + 0.3, world_pos.y);
		float nearest = min(off.x, off.y);
		float on_street = step(nearest, half_w) * inside * dry;
		float on_walk = step(nearest, half_w + walk_width) * (1.0 - on_street) * inside * dry;

		// What the block itself is.
		float count = floor(street_reach / street_pitch + 0.5);
		vec2 block = floor(p / street_pitch);
		float kind = texture(block_map, (block + count + 0.5) / (count * 2.0)).r;
		vec2 in_block = p - (block + 0.5) * street_pitch;
		float yard_edge = street_pitch * 0.5 - half_w - walk_width;
		float in_yard = step(max(abs(in_block.x), abs(in_block.y)), yard_edge) * inside * dry;
		float parking = in_yard * step(0.25, kind) * step(kind, 0.75);
		float square = in_yard * step(0.75, kind);

		float tarmac = max(on_street, parking);
		float paved = max(on_walk, square);
		vec2 su = p / street_size;
		Surface road = Surface(texture(street_albedo, su).rgb * street_tint, texture(street_normal, su).rg * 2.0 - 1.0, 1.0, texture(street_arm, su).g);
		vec2 wu = p / walk_size;
		Surface walk = Surface(texture(walk_albedo, wu).rgb * walk_tint, texture(walk_normal, wu).rg * 2.0 - 1.0, texture(walk_arm, wu).r, texture(walk_arm, wu).g);
		// Kerb: a dark line where pavement meets roadway.
		walk.albedo *= 1.0 - 0.45 * (1.0 - smoothstep(0.0, 0.22, nearest - half_w)) * on_walk;

		// Road markings. Worked out for a street along Z, then for one along X with the axes swapped.
		float mark = 0.0;
		for (int axis = 0; axis < 2; axis++) {
			float across = axis == 0 ? off.x : off.y;       // from the middle of this street
			float along = axis == 0 ? p.y : p.x;
			float to_crossing = (axis == 0 ? off.y : off.x) - half_w;   // to the edge of the nearest cross street
			if (across > half_w || to_crossing < 0.0) {
				continue;
			}
			float open_road = step(6.2, to_crossing);
			mark = max(mark, step(abs(across - (half_w - 0.5)), 0.08) * open_road);                       // edge lines
			mark = max(mark, step(across, 0.09) * open_road);                                             // centre line
			mark = max(mark, step(abs(across - half_w * 0.5), 0.07) * step(fract(along / 9.0), 0.33) * open_road);  // lane dashes
			mark = max(mark, step(abs(to_crossing - 5.6), 0.2) * step(across, half_w - 0.6));             // stop line
			mark = max(mark, step(1.2, to_crossing) * step(to_crossing, 4.4) * step(fract(across / 1.1), 0.5) * step(across, half_w - 1.0));  // zebra
		}
		// Parking bays: rows of stalls with aisles between.
		vec2 bay = in_block + yard_edge;
		float row = fract(bay.y / 16.0) * 16.0;
		float stalls = step(abs(fract(bay.x / 2.7) - 0.5), 0.02 * 2.7) * (step(row, 5.0) + step(11.0, row));
		stalls = max(stalls, step(abs(row - 5.0), 0.06) + step(abs(row - 11.0), 0.06));
		mark = max(mark * on_street, stalls * parking * step(max(abs(in_block.x), abs(in_block.y)), yard_edge - 1.0));
		// Wear: the paint is patchy, and gone where the asphalt is most broken.
		mark *= smoothstep(0.25, 0.5, fine.g + road.rough * 0.3) * (1.0 - smoothstep(120.0, 260.0, dist));
		road.albedo = mix(road.albedo, marking_color, mark * 0.85);

		s = blend(s, walk, paved);
		float t = tarmac;
		s = Surface(mix(s.albedo, road.albedo, t), mix(s.bump, road.bump, t), mix(s.ao, road.ao, t), mix(s.rough, road.rough, t));
	}

	// Long, slow drift in brightness and a little in hue.
	float drift = (broad.g - 0.5) * 2.0 * macro_strength;
	s.albedo *= 1.0 + drift;
	s.albedo = mix(s.albedo, s.albedo.gbr, clamp(drift, -0.06, 0.06) + 0.0);

	// Wet by the water.
	float wet = (1.0 - smoothstep(water_level + 0.15, water_level + 1.6, world_pos.y)) * wetness;
	s.albedo *= mix(1.0, 0.55, wet);
	s.rough = mix(s.rough, 0.22, wet);

	// Churned and burnt by explosions.
	float scorch = clamp(COLOR.r * (0.7 + fine.r * 0.6), 0.0, 1.0);
	s.albedo = mix(s.albedo, scorch_color * (0.6 + fine.g), scorch);
	s.rough = mix(s.rough, 0.95, scorch);

	// Mapped along world X and Z, so the texture's "right" and "up" are those axes.
	vec2 b = s.bump * normal_strength;
	vec3 n = normalize(world_normal + vec3(b.x, 0.0, -b.y) * world_normal.y - vec3(0.0, dot(b, world_normal.xz * vec2(1.0, -1.0)), 0.0));

	ALBEDO = s.albedo;
	ROUGHNESS = s.rough;
	AO = s.ao;
	AO_LIGHT_AFFECT = 0.3;
	SPECULAR = 0.35;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
