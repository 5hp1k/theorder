// Water in the manner of Half-Life 2: what is under it is seen bent and tinted by depth, what is above
// it is mirrored, and the two are mixed by the angle you look at it from. The surface is two layers of
// small waves sliding over each other, plus rings spreading from wherever something hit the water
// (fed in by WaterSurface.cs, which also renders the mirror image).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, ambient_light_disabled;

group_uniforms colour;
// Colour the water takes on in depth.
uniform vec4 water_color : source_color = vec4(0.04, 0.17, 0.2, 1.0);
// Tint of what shows through in the shallows.
uniform vec4 shallow_tint : source_color = vec4(0.6, 0.9, 0.85, 1.0);
// Metres you can see into it.
uniform float clarity = 3.5;
uniform vec4 foam_color : source_color = vec4(0.92, 0.96, 1.0, 1.0);
uniform float foam_width = 0.4;

group_uniforms waves;
uniform sampler2D normal_a : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_b : hint_normal, filter_linear_mipmap, repeat_enable;
// Wave pattern repeats per metre.
uniform float wave_scale = 0.05;
uniform vec2 flow_a = vec2(0.02, 0.009);
uniform vec2 flow_b = vec2(-0.013, 0.017);
uniform float wave_strength = 0.55;

group_uniforms optics;
// How far the waves bend the view of what is under the water.
uniform float refraction = 0.035;
// How far they bend the mirror image.
uniform float reflect_distortion = 0.045;
uniform float reflectivity : hint_range(0.0, 1.0) = 0.9;
// Mirror image of the world, rendered by WaterSurface.cs; without it the sky colour stands in.
uniform bool use_reflection = false;
uniform sampler2D reflection_tex : source_color, filter_linear, repeat_disable;
uniform vec4 sky_color : source_color = vec4(0.6, 0.72, 0.85, 1.0);
uniform float sun_glint = 2.0;

group_uniforms ripples;
uniform float ripple_speed = 6.0;
uniform float clock = 0.0;
uniform int ripple_count = 0;
// x, z of the centre and the clock time it started; and how strong.
uniform vec3 ripple_pos[24];
uniform float ripple_amp[24];

uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

varying vec3 world_pos;

float rings(vec2 p) {
	float h = 0.0;
	for (int i = 0; i < 24; i++) {
		if (i >= ripple_count) {
			break;
		}
		float age = clock - ripple_pos[i].z;
		if (age < 0.0 || age > 6.0) {
			continue;
		}
		float d = distance(p, ripple_pos[i].xy) - age * ripple_speed;
		// A short train of waves riding out on the ring, dying with time.
		h += sin(d * 2.2) * exp(-abs(d) * 0.45) * exp(-age * 0.8) * ripple_amp[i];
	}
	return h;
}

// Distance from the camera to whatever was drawn at this spot of the screen.
float scene_depth(vec2 uv, mat4 inv_projection) {
	float raw = texture(depth_tex, uv).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	vec3 ndc = vec3(uv, raw) * 2.0 - 1.0;
#else
	vec3 ndc = vec3(uv * 2.0 - 1.0, raw);
#endif
	vec4 view = inv_projection * vec4(ndc, 1.0);
	return -view.z / view.w;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = world_pos.xz;

	// Slope of the surface: two wave layers drifting across each other, and the rings.
	vec2 slope = (texture(normal_a, p * wave_scale + TIME * flow_a).rg
			+ texture(normal_b, p * wave_scale * 1.7 + TIME * flow_b).rg - 1.0) * 2.0 * wave_strength;
	float e = 0.3;
	float ring_here = rings(p);
	slope += vec2(rings(p + vec2(e, 0.0)) - ring_here, rings(p + vec2(0.0, e)) - ring_here) * 1.6;
	vec3 n = normalize(vec3(-slope.x, 1.0, -slope.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// How much water the eye looks through here.
	float surface = -VERTEX.z;
	float thickness = max(scene_depth(SCREEN_UV, INV_PROJECTION_MATRIX) - surface, 0.0);

	// What is under the water, bent by the waves (less so in the shallows, where it would tear).
	vec2 bent = SCREEN_UV + slope * refraction * clamp(thickness, 0.0, 1.0);
	float bent_thickness = scene_depth(bent, INV_PROJECTION_MATRIX) - surface;
	if (bent_thickness < 0.0) {
		// That spot is something in front of the water: don't drag it in.
		bent = SCREEN_UV;
		bent_thickness = thickness;
	}
	vec3 below = texture(screen_tex, bent).rgb;
	below *= mix(vec3(1.0), shallow_tint.rgb, clamp(bent_thickness * 0.6, 0.0, 1.0));
	below = mix(below, water_color.rgb, 1.0 - exp(-bent_thickness / clarity));

	// What is above it, mirrored. (The mirror camera's picture is flipped left to right.)
	vec3 above = sky_color.rgb;
	if (use_reflection) {
		above = texture(reflection_tex, vec2(1.0 - SCREEN_UV.x, SCREEN_UV.y) + slope * reflect_distortion).rgb;
	}

	// Looking straight down you see in; at a glancing angle you see the mirror.
	float facing = clamp(dot(NORMAL, normalize(VIEW)), 0.0, 1.0);
	float fresnel = 0.03 + 0.97 * pow(1.0 - facing, 5.0);
	vec3 colour = mix(below, above, clamp(fresnel * reflectivity, 0.0, 1.0));

	float foam = (1.0 - smoothstep(0.0, foam_width, thickness)) * 0.7 + clamp(abs(ring_here) * 0.5, 0.0, 0.5);
	colour = mix(colour, foam_color.rgb, clamp(foam, 0.0, 1.0));

	ALBEDO = vec3(0.0);
	EMISSION = colour;
	ROUGHNESS = 0.05;
	SPECULAR = 0.5;
	// Fades out at the very edge instead of cutting the shore with a hard line.
	ALPHA = smoothstep(0.0, 0.06, thickness);
}

void light() {
	// Only the glint of the sun on the waves; everything else is in the colour already.
	vec3 halfway = normalize(VIEW + LIGHT);
	SPECULAR_LIGHT += pow(max(dot(NORMAL, halfway), 0.0), 240.0) * LIGHT_COLOR * ATTENUATION * sun_glint;
}
