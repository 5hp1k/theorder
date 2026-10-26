// The cab's screens and head-up display going bad (CockpitDisplays): with the cab knocked about, the
// picture tears in bands, rolls, splits its colours, snows and drops out; "power" fades it out
// altogether (the engine off, the reactor down) — and flickers it back on while starting up.
uniform float power = 1.0;
uniform float noise = 0.0;

float cab_hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

// Where to read the picture from instead of uv: torn sideways in bands, rolled now and then.
vec2 cab_distort(vec2 uv) {
	if (noise <= 0.001) return uv;
	float t = TIME;
	float band = floor(uv.y * 36.0) + floor(t * 13.0) * 7.0;
	float tear = step(1.0 - noise * 0.4, cab_hash(vec2(band, floor(t * 17.0))));
	uv.x += (cab_hash(vec2(band, floor(t * 31.0))) - 0.5) * 0.14 * tear * noise;
	uv.y = fract(uv.y + step(0.82, cab_hash(vec2(floor(t * 1.7), 3.0))) * noise * 0.06 * sin(t * 4.0));
	return uv;
}

// The picture's colour, spoiled: snow, a dark bar rolling down, dropouts.
vec3 cab_spoil(vec3 c, vec2 uv) {
	if (noise <= 0.001) return c;
	float t = TIME;
	float snow = cab_hash(floor(uv * vec2(320.0, 240.0)) + fract(t * 61.0) * vec2(17.0, 31.0));
	c = mix(c, vec3(snow) * 0.55, noise * noise * 0.55);
	float bar = abs(fract(uv.y + t * 0.35) - 0.5);
	c *= 1.0 - noise * 0.45 * (1.0 - smoothstep(0.0, 0.07, bar));
	c *= 1.0 - step(1.0 - noise * 0.12, cab_hash(vec2(floor(t * 19.0), 9.0))) * 0.85;
	return c;
}
