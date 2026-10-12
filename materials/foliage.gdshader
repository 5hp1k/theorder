// Branches and leaves drawn as flat cards with a cut-out picture on them. Both sides are drawn and
// both are lit from the same side (the mesh's normals point out of the crown, not out of the card),
// light shines through, and the wind moves the cards: the red vertex colour says how far a point is
// free to swing (0 at the trunk), the green gives each branch its own timing, the blue marks the cards
// snow can lie on.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx, alpha_to_coverage;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 tint : source_color = vec3(1.0);
// Each tree is tinted a little differently; this is how much.
uniform float variation : hint_range(0.0, 1.0) = 0.25;
uniform float alpha_cut : hint_range(0.0, 1.0) = 0.33;
uniform float backlight : hint_range(0.0, 1.0) = 0.45;
uniform float roughness : hint_range(0.0, 1.0) = 0.7;

group_uniforms wind;
// Metres the tips swing.
uniform float sway = 0.12;
uniform float sway_speed = 1.3;

group_uniforms snow;
// 0 = none, 1 = boughs laden with it.
uniform float snow : hint_range(0.0, 1.0) = 0.0;
uniform vec3 snow_color : source_color = vec3(0.9, 0.93, 0.97);

varying float tree_tone;
varying float snow_here;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	tree_tone = fract(sin(dot(floor(origin.xz), vec2(12.9898, 78.233))) * 43758.5453);
	snow_here = COLOR.b;
	float phase = COLOR.g * 6.283 + origin.x * 0.31 + origin.z * 0.17;
	float gust = sin(TIME * sway_speed + phase) + 0.5 * sin(TIME * sway_speed * 2.7 + phase * 1.7 + world.y);
	vec3 push = vec3(gust, 0.35 * sin(TIME * sway_speed * 1.9 + phase), 0.6 * gust) * (sway * COLOR.r);
	VERTEX += (inverse(MODEL_MATRIX) * vec4(push, 0.0)).xyz;
}

void fragment() {
	vec4 t = texture(albedo_tex, UV);
	// Coverage is taken from a sharper level of the texture, or distant trees thin out to nothing.
	float alpha = texture(albedo_tex, UV, -1.0).a;
	vec3 albedo = t.rgb * tint;
	albedo *= mix(vec3(1.0), vec3(0.8 + 0.4 * tree_tone, 0.85 + 0.25 * fract(tree_tone * 7.0), 0.75 + 0.3 * tree_tone), variation);

	// Snow lies along the middle of a bough, thicker where the picture is denser.
	float lying = snow * snow_here * smoothstep(0.5, 0.1, abs(UV.y - 0.5) + (1.0 - t.g * 3.0) * 0.08) * smoothstep(0.0, 0.25, UV.x);
	lying = smoothstep(0.25, 0.6, lying + t.a * 0.25 * snow * snow_here);
	albedo = mix(albedo, snow_color, lying);

	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	ALBEDO = albedo;
	ROUGHNESS = mix(roughness, 0.55, lying);
	SPECULAR = 0.25;
	BACKLIGHT = albedo * backlight * (1.0 - lying);
	ALPHA = alpha;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	ALPHA_ANTIALIASING_EDGE = 0.35;
	ALPHA_TEXTURE_COORDINATE = UV * vec2(textureSize(albedo_tex, 0));
}
