// The canopy glass of a cockpit, seen from inside (scripts/Fx/CockpitGlass.cs sets the amounts): almost
// clear, with whatever is on it — star cracks from hits on the cab, a film of dirt and mud spatter, rain
// drops sitting on it and running down, snow settling up from the bottom edge, wind-blown dust.
// Drops and cracks bend the view behind them (the screen picture, shifted).
// Worked out in the glass's own axes (the Cockpit node's: x right, y up, -z ahead) — no UVs needed.
shader_type spatial;
render_mode blend_premul_alpha, cull_disabled, depth_draw_never, specular_schlick_ggx;
#include "res://materials/in_front.gdshaderinc"

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap, repeat_disable;
// Tileable value noise, for the patches of dirt, snow and dust.
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;

group_uniforms Glass;
uniform vec3 tint : source_color = vec3(0.6, 0.72, 0.74);
// How much of the view the clean glass itself dims and tints.
uniform float clear_alpha : hint_range(0.0, 0.3) = 0.04;
// Height (in the glass's axes) of its lower and upper edge: snow and dirt gather low down.
uniform float bottom = -0.3;
uniform float top = 0.98;

group_uniforms Cracks;
// xyz: centre of a crack on the glass, w: its reach in metres (0: none). Set by CockpitGlass.
uniform vec4 cracks[12];
uniform int crack_count = 0;
uniform vec3 crack_color : source_color = vec3(0.86, 0.9, 0.9);

group_uniforms Dirt;
uniform float dirt : hint_range(0.0, 1.0) = 0.0;
uniform vec3 dirt_color : source_color = vec3(0.36, 0.25, 0.14);
uniform float dust : hint_range(0.0, 1.0) = 0.0;
uniform vec3 dust_color : source_color = vec3(0.66, 0.55, 0.4);

group_uniforms Weather;
// How wet the glass is: how many drops sit on it and run down.
uniform float rain : hint_range(0.0, 1.0) = 0.0;
uniform float snow : hint_range(0.0, 1.0) = 0.0;
uniform vec3 snow_color : source_color = vec3(0.88, 0.9, 0.94);
// Drops per metre across; how fast the running ones go.
uniform float drop_scale = 13.0;
uniform float run_speed = 1.0;

varying vec3 local;
varying vec3 local_normal;

void vertex() {
	local = VERTEX;
	local_normal = NORMAL;
	IN_FRONT_VERTEX
}

float hash12(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

vec2 hash22(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * vec3(0.1031, 0.103, 0.0973));
	q += dot(q, q.yzx + 33.33);
	return fract((q.xx + q.yz) * q.zy);
}

// A point of the glass in 2D: across and up on the windscreen and roof, along and up on the side panes.
vec2 flat_of(vec3 p, vec3 n) {
	return abs(n.x) > 0.6 ? vec2(p.z * sign(n.x), p.y) : vec2(p.x, abs(n.y) > 0.8 ? -p.z : p.y);
}

// Drops sitting still: one in some cells, each growing in, lasting a while and drying off.
// xy: which way its surface leans (for the bending), z: how much of it is drop.
vec3 beads(vec2 p, float t, float amount) {
	vec2 cell = floor(p);
	vec2 f = fract(p) - 0.5;
	vec2 r = hash22(cell);
	float life = fract(t * (0.04 + 0.08 * r.x) + r.y);
	float size = mix(0.1, 0.32, hash12(cell + 7.1)) * smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.75, 1.0, life));
	size *= step(hash12(cell + 3.3), amount);
	vec2 d = f - (r - 0.5) * 0.55;
	float m = smoothstep(size, size * 0.55, length(d));
	return vec3(d / max(size, 1e-3) * m, m);
}

// Drops running down in columns, each leaving a wet trail above it.
vec3 runners(vec2 p, float t, float amount) {
	const vec2 cell = vec2(1.6, 5.0);
	float col = floor(p.x / cell.x);
	float rc = hash12(vec2(col, 3.7));
	float y = p.y / cell.y + t * (0.12 + 0.2 * rc) * run_speed + rc * 9.0;
	vec2 id = vec2(col, floor(y));
	vec2 r = hash22(id);
	if (hash12(id + 1.9) > amount) return vec3(0.0);
	float fy = fract(y);
	// It wanders a little from side to side.
	float x = (r.x - 0.5) * 0.7 * cell.x + sin(fy * 11.0 + r.y * 30.0) * 0.12;
	vec2 d = vec2(p.x - (col + 0.5) * cell.x - x, (fy - 0.3) * cell.y);
	float size = mix(0.2, 0.36, r.y);
	float drop = smoothstep(size, size * 0.5, length(d * vec2(1.0, 0.75)));
	// The trail: up from the drop, narrowing and drying.
	float trail = smoothstep(size * 0.45, size * 0.1, abs(d.x)) * step(0.0, d.y) * smoothstep(cell.y * 0.65, 0.0, d.y);
	float m = max(drop, trail * 0.4);
	return vec3(d / size * drop, m);
}

// Specks: a small dot in some cells (how many: density), of its own size and a little smeared.
float specks(vec2 p, float density) {
	vec2 cell = floor(p);
	vec2 r = hash22(cell);
	if (hash12(cell + 5.7) > density) return 0.0;
	vec2 d = fract(p) - 0.5 - (r - 0.5) * 0.5;
	d.y *= mix(1.0, 0.45, r.x);
	float size = mix(0.08, 0.3, r.y);
	return smoothstep(size, size * 0.4, length(d));
}

// The star of one crack: spokes from the point hit, broken rings round it, and a crazed middle.
// x: how much of a crack line is here, y: how crazed (milky) the glass is.
vec2 crack(vec2 v, float reach, float seed) {
	float d = length(v);
	if (d > reach || reach <= 0.0) return vec2(0.0);
	float ang = atan(v.y, v.x) / 6.2832 + 0.5;
	float spokes = 7.0 + floor(hash12(vec2(seed, 1.0)) * 6.0);
	float a = ang * spokes + seed;
	float k = floor(a);
	// Each spoke at its own angle in its sector, bending as it goes, and of its own length.
	float wobble = (texture(noise, vec2(d * 1.7, k * 0.13 + seed * 0.07)).r - 0.5) * 0.7;
	float off = abs(fract(a) - 0.5 - (hash12(vec2(k, seed)) - 0.5) * 0.5 - wobble * 0.35) / spokes * 6.2832 * d;
	float length_ = reach * mix(0.45, 1.0, hash12(vec2(k, seed + 2.0)));
	float width = mix(0.0034, 0.0012, d / reach);
	float line = smoothstep(width, width * 0.3, off) * smoothstep(length_, length_ * 0.8, d);
	// Rings: arcs from spoke to spoke, some sectors broken off.
	for (int i = 0; i < 3; i++) {
		float rr = reach * (0.16 + 0.17 * float(i)) * mix(0.8, 1.2, hash12(vec2(k + float(i) * 13.0, seed)));
		float bend = (texture(noise, vec2(ang * 5.0 + float(i), seed)).r - 0.5) * reach * 0.12;
		float arc = smoothstep(0.0016, 0.0004, abs(d - rr - bend)) * step(0.35, hash12(vec2(k, seed + float(i) * 5.0)));
		line = max(line, arc);
	}
	float crazed = smoothstep(reach * 0.13, reach * 0.04, d) * (0.55 + 0.45 * texture(noise, v * 9.0 + seed).r);
	return vec2(line, crazed);
}

void fragment() {
	vec3 n = normalize(local_normal);
	vec2 g = flat_of(local, n);
	float h = clamp((local.y - bottom) / max(top - bottom, 0.01), 0.0, 1.0);
	float t = TIME;
	float big = texture(noise, g * 0.6).r;
	float mid = texture(noise, g * 2.3 + 0.37).r;
	float fine = texture(noise, g * 9.0 + 0.71).r;

	// ── What bends the view: drops and cracks ─────────────────────
	vec2 bend = vec2(0.0);
	float wet = 0.0;
	if (rain > 0.001) {
		vec2 p = g * drop_scale;
		vec3 b1 = beads(p, t, rain * 0.75);
		vec3 b2 = beads(p * 1.9 + 17.0, t * 1.3, rain * 0.55);
		vec3 r1 = runners(p, t, rain * 0.6);
		bend = b1.xy + b2.xy * 0.6 + r1.xy;
		wet = clamp(b1.z + b2.z + r1.z, 0.0, 1.0);
	}
	float line = 0.0, crazed = 0.0;
	for (int i = 0; i < crack_count; i++) {
		vec4 c = cracks[i];
		vec2 v = g - flat_of(c.xyz, n);
		// (Only on the pane it is on, or one that meets it at the edge.)
		if (abs(dot(local - c.xyz, n)) > c.w * 0.5) continue;
		vec2 k = crack(v, c.w, float(i) * 7.31 + 1.0);
		line = max(line, k.x);
		crazed = max(crazed, k.y);
	}
	bend += vec2(fine - 0.5, mid - 0.5) * crazed * 0.6;

	// ── What lies on it ──────────────────────────────────────────
	// Dirt: a thin film, thicker low down, and spatter, mostly low down too.
	float low = (1.0 - h) * (1.0 - h);
	float film = dirt * (0.08 + 0.3 * low) * (0.5 + big);
	float spots = max(specks(g * 38.0, dirt * (0.04 + 0.7 * low)), specks(g * 85.0 + 3.1, dirt * (0.08 + 0.8 * low)) * 0.8);
	spots *= smoothstep(0.25, 0.5, mid);
	float dirt_a = clamp(film + spots * 0.97, 0.0, 0.95);
	// Dust: a fine even haze, drifted thicker at the bottom.
	float dust_a = dust * (0.22 + 0.4 * low) * (0.45 + 0.9 * smoothstep(0.2, 0.8, mid * 0.6 + big * 0.4));
	// Rain washes trails through dirt and dust.
	dirt_a *= 1.0 - wet * 0.8;
	dust_a *= 1.0 - wet * 0.9;
	// Snow: banks up from the bottom edge, with flakes stuck all over (more of them the longer it snows).
	float bank = smoothstep(0.0, 0.05, snow * 0.3 * (0.55 + big * 0.9) - h + (fine - 0.5) * 0.06);
	float flake = max(specks(g * 34.0 + 9.3, snow * 0.3), specks(g * 70.0 + 1.7, snow * 0.4) * 0.7);
	float snow_a = clamp(max(bank * 0.95, flake * 0.85), 0.0, 1.0);

	// ── Putting it together ──────────────────────────────────────
	// (A drop shows what is just round it, a little turned about: not the colours from further off.)
	vec2 uv = SCREEN_UV + bend * 0.011 / max(0.3, -VERTEX.z);
	vec3 behind = textureLod(screen, uv, crazed * 3.0).rgb;
	// A drop is a little lens: the view through it as bright as round it, catching the sky's light along
	// its upper edge and a thin shade along its lower one (bend points from its middle to its rim).
	float lens = clamp(max(wet, crazed * 0.8), 0.0, 1.0);
	float rim = smoothstep(0.55, 0.95, length(bend)) * wet;
	float upper = clamp(bend.y, 0.0, 1.0), lower = clamp(-bend.y, 0.0, 1.0);
	vec3 sky = textureLod(screen, vec2(0.5, 0.3), 7.0).rgb;
	vec3 seen = behind * (1.0 + 0.12 * wet) * (1.0 - 0.25 * rim * lower) + sky * 0.35 * rim * upper + crazed * 0.12;

	// Layers, front-most last: snow over dirt over dust, all lit by the scene; cracks catch the light.
	vec3 albedo = vec3(0.0);
	float a = 0.0;
	albedo = mix(albedo, dust_color, dust_a);         a = mix(a, 1.0, dust_a);
	albedo = mix(albedo, dirt_color, dirt_a);         a = mix(a, 1.0, dirt_a);
	albedo = mix(albedo, crack_color, line * 0.8);    a = mix(a, 1.0, line * 0.8);
	albedo = mix(albedo, snow_color, snow_a);         a = mix(a, 1.0, snow_a);
	// Through what's left of the glass: the bent view behind, and the faint tint of the glass itself.
	float through = 1.0 - a;
	ALBEDO = albedo * a + tint * clear_alpha * through;
	// Light from outside comes through what lies on the glass: dust and snow glow with it, mud a little.
	// (How bright it is out there as a whole, not the colour right behind: mud against the blue sky
	// would come out grey, against the sand brown.)
	vec3 out_there = textureLod(screen, vec2(0.5), 7.0).rgb;
	float light = max(dot(out_there, vec3(0.299, 0.587, 0.114)), 0.06);
	vec3 lit_through = light * ((dust_color * 1.5 + 0.15) * dust_a + dirt_color * 1.7 * dirt_a + snow_color * 0.45 * snow_a);
	EMISSION = seen * lens * through + lit_through * (1.0 - line * 0.5);
	ALPHA = clamp(a + (lens + clear_alpha) * through, 0.0, 1.0);
	ROUGHNESS = mix(0.08, 0.9, a);
	SPECULAR = mix(0.25, 0.5, wet) * (1.0 - a * 0.7);
	NORMAL = normalize(NORMAL + vec3(bend * 0.35, 0.0));
}
