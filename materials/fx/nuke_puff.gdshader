// One puff of the mushroom cloud (see nuke_cloud.gdshader): a ragged, billowing lump that faces the
// camera but is lit as part of the cloud's surface — the particle says which way that surface faces —
// so the mushroom has a sunlit side and a shaded one. Fire glows in its folds while it is hot.
shader_type spatial;
// Lit at its four corners, not at every pixel: the puffs are huge, there are hundreds of them one over
// another, and per-pixel lighting (with shadows) on all that is what a graphics card chokes on.
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_disabled, diffuse_lambert_wrap, vertex_lighting;

/// The lumps and folds: any seamless cloudy noise.
uniform sampler2D noise_texture : repeat_enable, filter_linear_mipmap;
/// How ragged the edge is.
uniform float raggedness : hint_range(0.0, 1.0) = 0.7;
/// How much darker the folds inside are.
uniform float folds : hint_range(0.0, 1.0) = 0.55;
/// How fast the lump churns.
uniform float churn = 0.02;
/// How much the lighting follows the shape of the whole cloud rather than the puff's own roundness.
uniform float solidity : hint_range(0.0, 1.0) = 0.6;
/// Fraction of its light that is its own (so the cloud isn't black at night).
uniform float glow : hint_range(0.0, 1.0) = 0.05;
/// The fire inside: dull red, through orange, to white-hot.
uniform vec3 fire_dull : source_color = vec3(0.75, 0.07, 0.01);
uniform vec3 fire_bright : source_color = vec3(1.0, 0.5, 0.1);
uniform vec3 fire_white : source_color = vec3(1.0, 0.93, 0.78);
uniform float fire_energy = 5.0;
/// Metres over which a puff fades out where it meets the ground, instead of ending in a hard line.
uniform float soft_edge = 9.0;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

varying float seed;
varying float heat;

void vertex() {
	seed = fract(sin(float(INSTANCE_ID) * 12.9898) * 43758.5453);
	heat = INSTANCE_CUSTOM.w;
	// The light falls on it as on the cloud's surface here, rounded off a little towards each corner.
	vec3 corner = (INV_VIEW_MATRIX * vec4(VERTEX.xy, 0.7, 0.0)).xyz;
	NORMAL = normalize(mix(normalize(corner), INSTANCE_CUSTOM.xyz, solidity));
	// Facing the camera, keeping the particle's size.
	mat4 face = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	face = face * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * face;
	MODELVIEW_NORMAL_MATRIX = mat3(VIEW_MATRIX);
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	float turn = seed * 6.283;
	p = mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn))) * p;
	float r = length(p);
	if (r > 1.0) discard;
	vec2 q = p * 0.27 + seed * 7.0 + vec2(0.0, -TIME * churn);
	float lump = texture(noise_texture, q).r;
	float fine = texture(noise_texture, q * 2.3 + 0.37).r;
	// The outline: cauliflower, not cotton wool.
	float edge = 0.86 - raggedness * 0.5 * (1.0 - lump);
	float body = smoothstep(edge, edge * 0.55, r);
	float crease = 1.0 - fine;
	vec3 colour = COLOR.rgb * (1.0 - folds * crease * 0.75);
	// Fire: in the folds first, everywhere while it is white-hot.
	float hot = heat * mix(0.25 + 1.5 * crease * crease, 1.0, clamp(heat - 1.5, 0.0, 1.0));
	vec3 fire = mix(fire_dull, fire_bright, smoothstep(0.15, 1.0, hot));
	fire = mix(fire, fire_white, smoothstep(1.4, 2.8, hot));
	ALBEDO = colour * (1.0 - clamp(hot, 0.0, 1.0) * 0.6);
	EMISSION = colour * glow + fire * fire_energy * smoothstep(0.02, 1.0, hot) * min(hot, 2.5);
	ROUGHNESS = 1.0;

	// Fades into whatever it touches.
	float depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	vec3 ndc = vec3(SCREEN_UV, depth) * 2.0 - 1.0;
#else
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
#endif
	vec4 behind = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	float gap = -behind.z / behind.w + VERTEX.z;
	ALPHA = body * COLOR.a * clamp(gap / soft_edge, 0.0, 1.0);
}
