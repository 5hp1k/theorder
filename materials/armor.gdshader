// Painted metal of a war machine. The paint (a flat colour, or a camouflage pattern) is wrapped round
// the part from three sides, so the parts need no texture coordinates. It wears through to bare metal
// on the sharp edges and in scattered chips, and dirt sits in the seams and in blotches. Where the
// edges and seams are is written into the mesh's vertex colours by ArmorBoxMesh / ArmorTubeMesh
// (red: 0 = edge, green: 0 = recess); a mesh without colours is simply painted all over.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

group_uniforms paint;
uniform vec3 paint_color : source_color = vec3(1.0);
// Camouflage pattern; white = just the colour above.
uniform sampler2D paint_tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
// Repeats of the pattern per metre.
uniform float paint_scale = 0.22;
uniform float paint_roughness : hint_range(0.0, 1.0) = 0.5;
uniform float paint_metallic : hint_range(0.0, 1.0) = 0.1;

group_uniforms surface;
// Fine relief of the metal under the paint.
uniform sampler2D surface_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D surface_arm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float surface_scale = 0.45;
uniform float surface_bump : hint_range(0.0, 2.0) = 0.6;

group_uniforms wear;
uniform vec3 metal_color : source_color = vec3(0.46, 0.44, 0.42);
// 0 = fresh from the factory, 1 = paint gone from every edge and chipped all over.
uniform float wear : hint_range(0.0, 1.0) = 0.55;
uniform vec3 dirt_color : source_color = vec3(0.13, 0.11, 0.09);
uniform float dirt : hint_range(0.0, 1.0) = 0.45;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;

varying vec3 part_pos;
varying vec3 part_normal;
varying mat3 to_view;

void vertex() {
	part_pos = VERTEX;
	part_normal = NORMAL;
	to_view = MODELVIEW_NORMAL_MATRIX;
}

// A texture seen from the three sides of the part and mixed by which way the surface faces.
#define three_sides(tex, p, w) (texture(tex, (p).zy).rgb * (w).x + texture(tex, (p).xz).rgb * (w).y + texture(tex, (p).xy).rgb * (w).z)

void fragment() {
	vec3 n = normalize(part_normal);
	vec3 w = pow(abs(n), vec3(6.0));
	w /= w.x + w.y + w.z;

	vec3 paint = three_sides(paint_tex, part_pos * paint_scale, w) * paint_color;
	vec3 arm = three_sides(surface_arm, part_pos * surface_scale, w);
	vec3 coarse = three_sides(noise_tex, part_pos * 0.11, w);
	vec3 fine = three_sides(noise_tex, part_pos * 0.9, w);

	float edge = 1.0 - COLOR.r;
	float recess = 1.0 - COLOR.g;

	// Bare metal: along the edges, raggedly, and in small chips where the noise peaks.
	float bare = smoothstep(0.55, 0.75, edge * 0.8 + fine.r * 0.45 + coarse.b * 0.15 - (1.0 - wear) * 0.6);
	bare = max(bare, smoothstep(0.93 - wear * 0.1, 0.97, fine.g * 0.7 + coarse.r * 0.3 + 0.12) * wear);
	// Paint fades and dulls unevenly.
	paint *= 0.85 + 0.3 * coarse.g;

	// Dirt: in the seams, and in broad stains.
	float grime = clamp(recess * 1.1 + smoothstep(0.45, 0.9, coarse.r * 0.7 + fine.b * 0.3) * dirt, 0.0, 1.0);
	grime *= mix(0.5, 1.0, dirt) * step(0.001, dirt + recess);

	vec3 albedo = mix(paint, metal_color * (0.7 + 0.5 * arm.g), bare);
	float metallic = mix(paint_metallic, 0.95, bare);
	float rough = mix(clamp(paint_roughness * (0.75 + 0.5 * arm.g), 0.05, 1.0), 0.32 + 0.2 * fine.b, bare);
	albedo = mix(albedo, dirt_color, grime * 0.75);
	metallic *= 1.0 - grime * 0.8;
	rough = mix(rough, 0.92, grime);

	// Relief, from the same three sides.
	vec2 bx = texture(surface_normal, part_pos.zy * surface_scale).rg * 2.0 - 1.0;
	vec2 by = texture(surface_normal, part_pos.xz * surface_scale).rg * 2.0 - 1.0;
	vec2 bz = texture(surface_normal, part_pos.xy * surface_scale).rg * 2.0 - 1.0;
	vec3 bumped = n + (vec3(0.0, bx.y, bx.x) * w.x + vec3(by.x, 0.0, by.y) * w.y + vec3(bz.x, bz.y, 0.0) * w.z) * surface_bump * (1.0 - grime * 0.5);

	ALBEDO = albedo;
	METALLIC = metallic;
	ROUGHNESS = rough;
	SPECULAR = 0.5;
	AO = arm.r * (1.0 - recess * 0.55);
	AO_LIGHT_AFFECT = 0.4;
	NORMAL = normalize(to_view * normalize(bumped));
}
