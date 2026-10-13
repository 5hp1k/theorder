// The mushroom cloud of a nuclear explosion, as one particle system. Nothing is simulated: every puff
// is put where it belongs from its age, so the cloud always takes the same well-known shape —
//  · the cap: a ring vortex rising on the fireball, rolling up through its middle and down its outside,
//    with a dome of cloud over the top;
//  · the stem: dust drawn up from the ground into the cap, flared at the foot and where it joins;
//  · the base surge: a low wall of dust racing outwards along the ground;
//  · the skirt: a thin white ring of condensation round the stem, for a few seconds.
// Each puff also carries the way its part of the cloud faces (CUSTOM.xyz), so the whole is lit as one
// solid shape by the sun, and how hot it still is (CUSTOM.w) — see nuke_puff.gdshader.
shader_type particles;
render_mode disable_velocity, disable_force;

group_uniforms shape;
/// How far the cap climbs above the ground, metres.
uniform float rise_height = 260.0;
/// Seconds it takes to get most of the way there (it slows as it goes).
uniform float rise_time = 9.0;
/// Radius of the full-grown cap.
uniform float cap_radius = 110.0;
/// How flat the cap is: 1 = round in section.
uniform float cap_flatten : hint_range(0.3, 1.0) = 0.78;
/// Radius of the stem at its waist.
uniform float stem_radius = 20.0;
/// How far the dust runs out along the ground.
uniform float surge_radius = 240.0;
/// Turns the vortex of the cap makes, in radians per second at the start.
uniform float roll_speed = 1.3;

group_uniforms parts;
/// How many puffs the emitter has (its Amount). NukeBlast fills this in.
uniform float puffs = 720.0;
/// Share of the puffs that make the cap, the stem and the base surge; what is left is the skirt.
uniform float cap_share : hint_range(0.0, 1.0) = 0.5;
uniform float stem_share : hint_range(0.0, 1.0) = 0.24;
uniform float surge_share : hint_range(0.0, 1.0) = 0.2;
/// Size of the puffs, as a multiple of what the shape needs.
uniform float puff_size = 1.0;

group_uniforms colour;
/// Dust and soot, straight after the blast.
uniform vec3 dust_color : source_color = vec3(0.3, 0.24, 0.19);
/// What the cloud bleaches to as it cools and condenses.
uniform vec3 cloud_color : source_color = vec3(0.74, 0.71, 0.68);
/// Dust of the base surge.
uniform vec3 ground_color : source_color = vec3(0.5, 0.42, 0.33);
/// Seconds the fire inside takes to die down.
uniform float glow_time = 7.0;

group_uniforms life;
/// Seconds the cloud stands, the last fade_time of them thinning away.
uniform float life = 62.0;
uniform float fade_time = 22.0;
/// Wind: the cloud leans and drifts, more the higher up.
uniform vec3 wind = vec3(2.2, 0.0, 0.9);

float rnd(float i, float salt) {
	return fract(sin(i * 12.9898 + salt * 78.233) * 43758.5453);
}

void start() {
	// (VELOCITY isn't a velocity here: its x keeps the puff's age.)
	VELOCITY = vec3(0.0);
	CUSTOM = vec4(0.0, 1.0, 0.0, 0.0);
	COLOR = vec4(0.0);
	TRANSFORM = mat4(vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

void process() {
	VELOCITY.x += DELTA;
	float t = VELOCITY.x;
	float i = float(INDEX) + float(RANDOM_SEED % 977u);
	float a = rnd(i, 1.0), b = rnd(i, 2.0), c = rnd(i, 3.0), d = rnd(i, 4.0), e = rnd(i, 5.0);
	float share = (float(INDEX) + 0.5) / max(puffs, 1.0);

	// How far the cloud has risen and filled out.
	float grow = 1.0 - exp(-t / (rise_time * 0.45));
	float top = cap_radius * 0.25 + rise_height * (1.0 - exp(-t / rise_time));
	float minor = cap_radius * 0.5 * mix(0.5, 1.0, grow);
	float major = cap_radius * 0.5 * mix(0.3, 1.0, grow);
	float bleach = smoothstep(glow_time * 0.4, glow_time * 3.0, t);

	vec3 pos, nrm, col;
	float size, heat, alpha = 1.0;

	if (share < cap_share) {
		float th = a * TAU;
		vec2 ring = vec2(cos(th), sin(th));
		float side;
		if (d < 0.2) {
			// The dome over the middle of the ring.
			float u = sqrt(b);
			pos = vec3(ring.x * major * u, top + minor * cap_flatten * (0.95 - 0.6 * u * u), ring.y * major * u);
			nrm = normalize(vec3(ring.x * u * 0.7, 1.0, ring.y * u * 0.7));
			side = 0.2;
		} else {
			// The vortex: round the tube, slower and slower as the cloud tires.
			float roll = roll_speed * rise_time * (1.0 - exp(-t / rise_time)) * mix(0.75, 1.25, c);
			float ph = b * TAU - roll;
			vec2 tube = vec2(cos(ph), sin(ph));
			float shell = mix(0.72, 1.0, sqrt(e));
			float rad = major + minor * tube.x * shell;
			pos = vec3(ring.x * rad, top + minor * cap_flatten * tube.y * shell, ring.y * rad);
			nrm = normalize(vec3(ring.x * tube.x, tube.y, ring.y * tube.x));
			// The fire shows longest where the cloud turns in on itself: the inside and the underside.
			side = max(0.5 - 0.5 * tube.x, 0.5 - 0.5 * tube.y);
		}
		size = minor * mix(0.5, 0.8, e);
		heat = 3.2 * exp(-t / glow_time) * mix(0.3, 1.0, side * side);
		col = mix(dust_color, cloud_color, bleach);
	} else if (share < cap_share + stem_share) {
		// Up the stem, again and again.
		float u = fract(a + t * 0.055 * mix(0.7, 1.3, c));
		float flare = 1.0 + 2.0 * exp(-u * 7.0) + 1.1 * smoothstep(0.72, 1.0, u);
		float th = b * TAU + u * 2.5 + t * 0.12;
		float rad = stem_radius * flare * mix(0.3, 1.0, sqrt(d)) * mix(0.6, 1.0, grow);
		pos = vec3(cos(th) * rad, u * (top - minor * 0.35), sin(th) * rad);
		nrm = normalize(vec3(cos(th), 0.3, sin(th)));
		size = stem_radius * mix(0.8, 1.3, e) * (0.65 + 0.35 * flare) * mix(0.6, 1.0, grow);
		heat = 2.4 * exp(-t / (glow_time * 0.8)) * mix(0.25, 1.0, 1.0 - sqrt(d));
		alpha = smoothstep(0.0, 0.05, u) * (1.0 - smoothstep(0.9, 1.0, u));
		col = mix(dust_color, mix(ground_color, cloud_color, 0.5), bleach);
	} else if (share < cap_share + stem_share + surge_share) {
		float reach = surge_radius * (1.0 - exp(-t / (rise_time * 0.6))) * mix(0.2, 1.0, sqrt(a));
		float th = b * TAU;
		size = mix(14.0, 40.0, c) * mix(0.5, 1.0, grow);
		pos = vec3(cos(th) * reach, size * 0.3 + d * 12.0 * grow + t * 0.35 * c, sin(th) * reach);
		nrm = normalize(vec3(cos(th) * 0.6, 1.0, sin(th) * 0.6));
		heat = 1.2 * exp(-t / 2.0) * (1.0 - sqrt(a));
		alpha = 0.75;
		col = ground_color;
	} else {
		float th = a * TAU;
		float rad = cap_radius * 0.85 * mix(0.45, 1.1, grow) * mix(0.92, 1.08, b);
		pos = vec3(cos(th) * rad, top * 0.58 + (c - 0.5) * 8.0, sin(th) * rad);
		nrm = normalize(vec3(cos(th) * 0.5, 1.0, sin(th) * 0.5));
		size = mix(13.0, 22.0, d);
		heat = 0.0;
		alpha = 0.4 * smoothstep(1.5, 4.0, t) * (1.0 - smoothstep(9.0, 17.0, t));
		col = vec3(0.92);
	}

	// No two puffs sit quite where the shape says; the whole leans with the wind and spreads as it ages.
	pos += (vec3(rnd(i, 6.0), rnd(i, 7.0), rnd(i, 8.0)) - 0.5) * size * 0.35;
	pos += wind * (pos.y / max(rise_height, 1.0)) * max(t - 6.0, 0.0);
	size *= puff_size * (1.0 + t * 0.008);
	alpha *= smoothstep(0.0, 0.35, t) * (1.0 - smoothstep(life - fade_time, life - 1.0, t));

	TRANSFORM = mat4(vec4(size, 0.0, 0.0, 0.0), vec4(0.0, size, 0.0, 0.0), vec4(0.0, 0.0, size, 0.0), vec4(pos, 1.0));
	COLOR = vec4(col * mix(0.82, 1.1, rnd(i, 9.0)), alpha);
	CUSTOM = vec4(nrm, heat);
}
