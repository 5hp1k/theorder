// Palm fronds (models/trees/palm_*/Crown.res): real leaflets, not cut-out cards, coloured by vertex
// colour (young fronds green, old ones yellowing, dead ones brown). Both sides are drawn and lit, light
// shines through, and the wind moves the fronds — more the further a point is from where they leave the
// trunk (the crown, in the mesh's own space: each palm's material says where that is).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec3 tint : source_color = vec3(1.0);
// Each palm is tinted a little differently; this is how much.
uniform float variation : hint_range(0.0, 1.0) = 0.25;
uniform float backlight : hint_range(0.0, 1.0) = 0.5;
uniform float roughness : hint_range(0.0, 1.0) = 0.65;
// Where the fronds leave the trunk, in the mesh's space.
uniform vec3 crown = vec3(0.0, 10.0, 0.0);

group_uniforms wind;
// Metres the frond tips swing.
uniform float sway = 0.3;
uniform float sway_speed = 1.1;

varying float tree_tone;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	tree_tone = fract(sin(dot(floor(origin.xz), vec2(12.9898, 78.233))) * 43758.5453);
	vec3 from_crown = VERTEX - crown;
	float free = clamp(length(from_crown) / 5.0, 0.0, 1.0);
	free *= free;
	// Each frond with its own timing (by the way it points), each palm with its own.
	float phase = origin.x * 0.31 + origin.z * 0.17 + atan(from_crown.z, from_crown.x) * 1.3;
	float gust = sin(TIME * sway_speed + phase) + 0.5 * sin(TIME * sway_speed * 2.3 + phase * 1.7);
	vec3 push = vec3(gust, 0.4 * sin(TIME * sway_speed * 1.7 + phase), 0.6 * gust) * (sway * free);
	VERTEX += (inverse(MODEL_MATRIX) * vec4(push, 0.0)).xyz;
}

void fragment() {
	vec3 albedo = COLOR.rgb * tint;
	albedo *= mix(vec3(1.0), vec3(0.85 + 0.3 * tree_tone, 0.9 + 0.2 * fract(tree_tone * 7.0), 0.8 + 0.3 * tree_tone), variation);
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	ALBEDO = albedo;
	ROUGHNESS = roughness;
	SPECULAR = 0.3;
	BACKLIGHT = albedo * backlight;
}
