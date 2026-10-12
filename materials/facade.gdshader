// Wall of a building made of blocks (see ChunkedBuilding). Everything is laid out from the block's
// place in the building (handed over per block by ChunkedBuilding), so the blocks need no texture
// coordinates, the pattern runs unbroken across them and the windows sit square in them: a wall surface
// (brick, plaster, concrete) with a grid of windows cut into it. Behind each pane is a room that
// isn't there: where the eye's ray would land on its back wall, ceiling or floor is worked out and
// painted in, so the windows have depth as you move past. Some rooms have the blinds part-way
// down, some have the light on.
// A wall that has been hit breaks up: ragged holes eat into a block as its damage grows (and through
// them you see into the hollow block), the edges of the holes are cracked and blackened; a face that
// was inside the building until its neighbour went shows bare structure — floor slabs and raw
// concrete — instead of windows. ChunkedBuilding tells each block which of its faces are outside
// and how damaged it is.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

group_uniforms wall;
uniform sampler2D wall_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wall_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wall_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres one repeat of the wall texture covers.
uniform float wall_size = 3.0;
uniform vec3 wall_tint : source_color = vec3(1.0);
// Streaks and grime: 0 = freshly built.
uniform float dirt : hint_range(0.0, 1.0) = 0.5;

group_uniforms windows;
// Side of one block of the building, metres. Set by ChunkedBuilding.
instance uniform float cell_size = 2.0;
// Width of one bay (a window and the wall round it) and height of one storey, metres.
uniform float bay_width = 2.0;
uniform float floor_height = 2.0;
// Size of the opening, as a share of the bay and of the storey.
uniform vec2 window_size = vec2(0.55, 0.6);
// Height of the sill above the floor, share of the storey.
uniform float sill = 0.22;
uniform float frame_width = 0.06;
uniform vec3 frame_color : source_color = vec3(0.75, 0.74, 0.7);
// Number of panes across and up.
uniform vec2 panes = vec2(2.0, 1.0);
uniform vec3 glass_tint : source_color = vec3(0.55, 0.65, 0.7);
// Depth of the rooms behind, metres.
uniform float room_depth = 3.0;
// Share of rooms with the light on, and how bright.
uniform float lit_rooms : hint_range(0.0, 1.0) = 0.12;
uniform float lit_energy = 0.8;
// A band of plain wall marking each storey (concrete floor slab), metres; 0 = none.
uniform float slab_band = 0.0;
uniform vec3 slab_color : source_color = vec3(0.6, 0.6, 0.58);

varying vec3 world_pos;
varying vec3 block_pos;   // within the block, from its centre
varying vec3 house_pos;   // within the building
varying vec3 house_normal;
varying mat3 to_world;
varying float outer;    // 1 on a face of the building's outside
varying float damage;   // 0 = whole, 1 = barely hanging together

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	block_pos = VERTEX;
	house_pos = VERTEX + INSTANCE_CUSTOM.xyz;
	house_normal = NORMAL;
	to_world = MODEL_NORMAL_MATRIX;
	// Fourth number from ChunkedBuilding: 64 + one bit per outside face (+X, -X, +Y, -Y, +Z, -Z),
	// and the damage in its fraction. Anything else drawn with this material is all outside, undamaged.
	float code = INSTANCE_CUSTOM.a;
	if (code < 63.5) {
		outer = 1.0;
		damage = 0.0;
	} else {
		float bit = NORMAL.x > 0.5 ? 1.0 : NORMAL.x < -0.5 ? 2.0 : NORMAL.y > 0.5 ? 4.0 : NORMAL.y < -0.5 ? 8.0 : NORMAL.z > 0.5 ? 16.0 : 32.0;
		outer = mod(floor((floor(code) - 64.0) / bit), 2.0);
		damage = fract(code) / 0.9;
	}
}

void fragment() {
	vec3 n = normalize(house_normal);
	bool side = abs(n.y) < 0.5;
	// Along the wall, up the wall, and out of it.
	vec3 along = side ? normalize(vec3(n.z, 0.0, -n.x)) : vec3(1.0, 0.0, 0.0);
	vec3 up = side ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, -1.0);
	vec2 at = vec2(dot(house_pos, along), dot(house_pos, up));
	// The same, measured from the block's lower corner: bays and storeys are counted from there.
	vec2 in_block = vec2(dot(block_pos, along), dot(block_pos, up)) + cell_size * 0.5;
	// Which block this is, as whole numbers: these seed the random choices, and must not waver from pixel to pixel.
	vec2 block_at = floor((at - in_block) / cell_size + 0.5);

	// Holes: wherever a slow noise falls below the damage. The same noise, a little higher, is the cracked rim.
	float solid = vnoise(at * 0.6 + dot(n, vec3(3.0, 7.0, 5.0))) * 0.62 + vnoise(at * 1.9 + 11.0) * 0.38;
	float gone = damage * 0.66;
	if (damage > 0.01 && solid < gone) {
		discard;
	}
	float rim = damage > 0.01 ? 1.0 - smoothstep(gone, gone + 0.13, solid) : 0.0;
	// The inside of a block, or a face that used to be inside the building.
	bool raw = outer < 0.5 || !FRONT_FACING;

	vec2 uv = vec2(at.x, -at.y) / wall_size;
	vec3 albedo = texture(wall_albedo, uv).rgb * wall_tint;
	vec2 bump = texture(wall_normal, uv).rg * 2.0 - 1.0;
	vec2 arm = texture(wall_arm, uv).rg;
	float ao = arm.r, rough = arm.g, spec = 0.4;
	vec3 glow = vec3(0.0);

	if (raw) {
		// Bare structure: grey, rough, with the edge of a floor slab at every storey.
		float grey = dot(albedo, vec3(0.33));
		albedo = mix(vec3(grey), albedo, 0.3) * 0.85;
		float slab = side ? step(fract(house_pos.y / floor_height + 0.04), 0.09) : 0.0;
		albedo = mix(albedo, vec3(0.42, 0.41, 0.39) * (0.7 + 0.3 * arm.r), slab);
		albedo *= 0.75 + 0.5 * vnoise(at * 2.3);
		rough = 0.95;
		spec = 0.2;
	}
	if (side && !raw) {
		// Grime: rain streaks down the wall and splash-back near the ground.
		float streaks = vnoise(vec2(at.x * 1.7, at.y * 0.12)) * vnoise(vec2(at.x * 0.31, at.y * 0.05) + 9.0);
		float grime = clamp(streaks * 1.6 + (1.0 - smoothstep(0.0, 2.5, at.y)) * 0.7, 0.0, 1.0) * dirt;
		albedo *= mix(1.0, 0.45, grime);

		vec2 bay = in_block / vec2(bay_width, floor_height);
		vec2 cell = floor(bay) + block_at * 7.0 + floor(dot(n, vec3(13.0, 0.0, 29.0)) + 0.5);
		vec2 in_cell = fract(bay);

		if (slab_band > 0.0) {
			float band = step(in_cell.y * floor_height, slab_band);
			albedo = mix(albedo, slab_color * (0.8 + 0.2 * arm.r) * mix(1.0, 0.6, grime), band);
			rough = mix(rough, 0.85, band);
		}

		// The opening, 0..1 across and up; the frame is the outermost part of it.
		vec2 lo = vec2((1.0 - window_size.x) * 0.5, sill);
		vec2 w = (in_cell - lo) / window_size;
		vec2 w_m = w * window_size * vec2(bay_width, floor_height);
		vec2 size_m = window_size * vec2(bay_width, floor_height);
		vec2 edge = min(w_m, size_m - w_m);
		float aa = max(fwidth(at.x), fwidth(at.y)) * 1.5;
		float opening = smoothstep(-aa, aa, min(edge.x, edge.y));
		if (opening > 0.0) {
			// Bars between the panes.
			vec2 pane = fract(w * panes);
			vec2 pane_m = min(pane, 1.0 - pane) * size_m / panes;
			float glass = smoothstep(frame_width - aa, frame_width + aa, min(pane_m.x, pane_m.y));

			// The room behind: a box one bay wide, one storey high, room_depth deep.
			vec3 to_eye = normalize((CAMERA_POSITION_WORLD - world_pos) * to_world);
			vec3 ray = vec3(dot(-to_eye, along), dot(-to_eye, up), max(dot(-to_eye, -n), 0.02));
			vec3 room = vec3(bay_width, floor_height, room_depth);
			vec3 from = vec3(in_cell * room.xy, 0.0);
			vec3 t3 = (mix(vec3(0.0), room, step(vec3(0.0), ray)) - from) / (ray + vec3(1e-5) * sign(ray + 1e-6));
			float t = min(t3.x, min(t3.y, t3.z));
			vec3 hit = from + ray * t;
			float seed = hash(cell + 0.5);
			vec3 paint = mix(vec3(0.5, 0.47, 0.42), vec3(0.42, 0.48, 0.5), hash(cell + 3.1));
			vec3 inside;
			if (t == t3.z) {
				// Back wall, with a door-shaped darker patch somewhere along it.
				float door = step(abs(hit.x / room.x - 0.2 - seed * 0.6), 0.14) * step(hit.y / room.y, 0.75);
				inside = paint * mix(1.0, 0.45, door);
			} else if (t == t3.y) {
				inside = ray.y > 0.0 ? vec3(0.62) : vec3(0.22, 0.19, 0.16);
			} else {
				inside = paint * 0.8;
			}
			// Darker the deeper in; the ceiling corners darkest.
			inside *= 0.25 + 0.5 * (1.0 - hit.z / room.z);
			float lit = step(1.0 - lit_rooms, hash(cell + 7.7));
			vec3 lamp = mix(vec3(1.0, 0.82, 0.55), vec3(0.8, 0.9, 1.0), hash(cell + 1.3));
			// Blinds, part-way down in some windows.
			float blind = step(1.0 - w.y, hash(cell + 5.5) * step(0.45, hash(cell + 2.2)));
			// The slats show close up and blur into a flat tone further off.
			float slats = mix(step(0.5, fract(w_m.y * 14.0)), 0.5, smoothstep(0.02, 0.07, fwidth(w_m.y)));
			vec3 blind_color = vec3(0.6, 0.58, 0.53) * (0.8 + 0.2 * slats);
			inside = mix(inside, blind_color * 0.35, blind);

			vec3 window_albedo = mix(frame_color, inside * glass_tint, glass);
			albedo = mix(albedo, window_albedo, opening);
			rough = mix(rough, mix(0.5, 0.06, glass), opening);
			spec = mix(spec, mix(0.4, 0.75, glass), opening);
			ao = mix(ao, 1.0, opening);
			bump = mix(bump, vec2(0.0), opening);
			glow = inside * lamp * lit * lit_energy * glass * opening * (1.0 - blind * 0.8);
		}
	}

	// Broken edges are dark with soot and dust.
	albedo *= mix(1.0, 0.3, rim);
	rough = mix(rough, 1.0, rim);
	glow *= 1.0 - rim;
	vec3 world_n = normalize(to_world * (n + (along * bump.x + up * bump.y) * 0.9));
	if (!FRONT_FACING) {
		world_n = -world_n;
	}
	ALBEDO = albedo;
	ROUGHNESS = rough;
	SPECULAR = spec;
	AO = ao;
	AO_LIGHT_AFFECT = 0.3;
	EMISSION = glow;
	NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
}
