// Scanned rock. The rock's own textures give it its look from a distance; because the props are far
// bigger than the stones that were scanned, a second, repeating rock texture is laid over it in world
// space for close-up grain. Whatever faces the sky can be covered: snow, drifted sand, moss.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D arm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 tint : source_color = vec3(1.0);
uniform float normal_depth = 1.2;

group_uniforms grain;
uniform sampler2D grain_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres one repeat covers.
uniform float grain_size = 3.0;
uniform float grain_strength : hint_range(0.0, 1.0) = 0.6;

group_uniforms cover;
uniform sampler2D cover_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D cover_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float cover_size = 4.0;
uniform vec3 cover_tint : source_color = vec3(1.0);
// 0 = bare rock, 1 = everything but the undersides.
uniform float cover_amount : hint_range(0.0, 1.0) = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

void fragment() {
	vec3 albedo = texture(albedo_tex, UV).rgb * tint;
	vec2 arm = texture(arm_tex, UV).rg;

	// Grain, projected along the three world axes and mixed by which way the surface faces.
	vec3 w = pow(abs(world_normal), vec3(4.0));
	w /= w.x + w.y + w.z;
	vec3 p = world_pos / grain_size;
	vec3 grain = texture(grain_tex, p.zy).rgb * w.x + texture(grain_tex, p.xz).rgb * w.y + texture(grain_tex, p.xy).rgb * w.z;
	float g = dot(grain, vec3(0.33));
	albedo *= mix(1.0, 0.45 + g * 2.2, grain_strength);

	float up = world_normal.y + (g - 0.25) * 0.8;
	float cover = smoothstep(1.0 - cover_amount - 0.15, 1.0 - cover_amount + 0.15, up * 0.5 + 0.5) * step(0.001, cover_amount);
	vec3 top = texture(cover_albedo, world_pos.xz / cover_size).rgb * cover_tint;
	vec2 top_arm = texture(cover_arm, world_pos.xz / cover_size).rg;

	ALBEDO = mix(albedo, top, cover);
	ROUGHNESS = mix(arm.g, top_arm.g, cover);
	AO = mix(arm.r, top_arm.r, cover);
	AO_LIGHT_AFFECT = 0.4;
	NORMAL_MAP = texture(normal_tex, UV).rgb;
	NORMAL_MAP_DEPTH = normal_depth * (1.0 - cover * 0.8);
}
