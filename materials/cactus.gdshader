// Skin of a cactus. The ribs are in the mesh; this darkens the grooves between them and dots the
// ridges with pale tufts of spines. UV.x counts ribs round the stem, UV.y is height in metres.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 skin : source_color = vec3(0.23, 0.36, 0.17);
uniform vec3 groove : source_color = vec3(0.1, 0.19, 0.09);
uniform vec3 spines : source_color = vec3(0.82, 0.78, 0.62);
// Tufts per metre of rib.
uniform float tufts = 7.0;
uniform sampler2D grain_tex : filter_linear_mipmap, repeat_enable;

void fragment() {
	float across = abs(fract(UV.x) - 0.5) * 2.0; // 0 on the ridge, 1 in the groove
	vec3 grain = texture(grain_tex, vec2(UV.x * 0.13, UV.y * 0.21)).rgb;
	vec3 albedo = mix(skin, groove, smoothstep(0.35, 1.0, across)) * (0.75 + grain.r * 0.5);
	// Scars and sun-bleached patches.
	albedo = mix(albedo, vec3(0.42, 0.38, 0.24), smoothstep(0.78, 0.95, grain.g) * 0.5);
	vec2 dot_at = vec2(across * 0.9, fract(UV.y * tufts + floor(UV.x) * 0.37) - 0.5);
	float tuft = 1.0 - smoothstep(0.05, 0.16, length(dot_at * vec2(1.0, 0.55)));
	ALBEDO = mix(albedo, spines, tuft);
	ROUGHNESS = mix(0.55, 0.9, tuft);
	SPECULAR = 0.35;
	AO = mix(1.0, 0.55, smoothstep(0.5, 1.0, across));
	AO_LIGHT_AFFECT = 0.5;
}
