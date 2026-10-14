// A lick of flame for particles: tongues of noise rising through the puff, white-hot in the middle
// and deepening to red at the ragged edge, added onto the picture. The particle's colour ramp tints it
// and fades it; each flame has its own shape and flickers as it burns out.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, shadows_disabled, unshaded;

/// How bright the core is.
uniform float heat = 2.6;
/// How fast the tongues rise through the flame.
uniform float rise = 1.6;
/// 0 = a tongue of flame standing up; 1 = a round rolling puff of it (the jet of a flamethrower).
uniform float puff : hint_range(0.0, 1.0) = 0.0;
/// Colour of the hottest part, and how far the core goes towards it. (Kept short of white for a big
/// flame: the picture's tone mapping washes bright orange out to white.)
uniform vec3 core_tint : source_color = vec3(1.0, 0.92, 0.7);
uniform float core_mix : hint_range(0.0, 1.0) = 0.7;

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

void vertex() {
	seed = hash(vec2(float(INSTANCE_ID), 3.0));
	age = INSTANCE_CUSTOM.y;
	mat4 face = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	face = face * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * face;
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	p.y = -p.y; // up is up
	// Tongues: noise streaming upwards, pinched towards the top.
	vec2 q = vec2(p.x * 2.2, p.y * 1.3 - TIME * rise - age * 2.0) + seed * 30.0;
	float tongue = noise(q) * 0.6 + noise(q * 2.3 + 5.0) * 0.4;
	float width = mix(0.85, 0.3, clamp(p.y * 0.5 + 0.5, 0.0, 1.0));
	float shape = 1.0 - smoothstep(0.0, width, abs(p.x) + (tongue - 0.5) * 0.5);
	shape *= smoothstep(1.0, 0.2, p.y + (tongue - 0.5) * 0.8) * smoothstep(-1.0, -0.65, p.y);
	// A puff: a ragged ball instead of a tongue.
	float ball = 1.0 - smoothstep(0.25, 0.85, length(p) + (tongue - 0.5) * 0.7);
	shape = mix(shape, ball, puff);
	// Nothing reaches the edge of the card it is drawn on, whatever the noise does: no square outlines.
	shape *= (1.0 - smoothstep(0.6, 0.98, abs(p.x))) * (1.0 - smoothstep(0.6, 0.98, abs(p.y)));
	shape *= smoothstep(age - 0.25, age + 0.25, tongue * 0.6 + shape * 0.5);
	float core = shape * shape;
	// White-yellow where it is hottest, the tint of the ramp towards the edge.
	vec3 colour = mix(COLOR.rgb, core_tint, core * core_mix) * (0.6 + heat * core);
	ALBEDO = colour * shape * COLOR.a;
	ALPHA = 1.0;
}
