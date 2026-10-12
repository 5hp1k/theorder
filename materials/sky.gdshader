// Photographic sky. The picture you see is the upper half of a panorama (an ordinary 8-bit photo, sharp);
// the light the sky gives the world comes from a small high-dynamic-range copy of the same panorama,
// so shadows and reflections take their colour from the real thing. Below the horizon the sky runs
// into a flat haze colour, which the distant ground and the fog fade into.
shader_type sky;

// Upper hemisphere, zenith at the top edge, horizon at the bottom edge.
uniform sampler2D sky_tex : source_color, filter_linear, repeat_enable;
// Whole panorama in HDR, used for lighting only.
uniform sampler2D light_tex : filter_linear_mipmap, repeat_enable;
// Brightness of the visible sky.
uniform float background_energy = 1.0;
// How much the brightest parts of the photo (sunlit cloud edges, the glow round the sun) are pushed up.
uniform float highlight_boost = 1.5;
// Brightness of the light the sky casts.
uniform float light_energy = 1.0;
// The sun itself is a DirectionalLight3D; in the lighting copy it is cut down to this so it isn't counted twice.
uniform float light_clamp = 8.0;
// What lies below the horizon: seen in the distance and lighting the undersides of things.
uniform vec3 ground_color : source_color = vec3(0.3, 0.27, 0.22);
// Turns the whole sky about the vertical, radians.
uniform float rotation = 0.0;

vec3 turned(vec3 d) {
	float c = cos(rotation), s = sin(rotation);
	return vec3(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
}

void sky() {
	vec3 d = turned(EYEDIR);
	float u = atan(d.x, -d.z) / TAU + 0.5;
	if (AT_CUBEMAP_PASS) {
		float v = acos(clamp(d.y, -1.0, 1.0)) / PI;
		vec3 light = min(textureLod(light_tex, vec2(u, min(v, 0.5)), 1.0).rgb, vec3(light_clamp));
		// The ground's own bounce stands in for the lower half.
		float below = smoothstep(0.0, -0.12, d.y);
		COLOR = mix(light, ground_color * dot(textureLod(light_tex, vec2(u, 0.3), 6.0).rgb, vec3(0.33)), below) * light_energy;
	} else {
		float v = acos(clamp(d.y, 0.0, 1.0)) / (PI * 0.5);
		vec3 c = textureLod(sky_tex, vec2(u, min(v, 0.997)), 0.0).rgb;
		float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
		c *= 1.0 + highlight_boost * smoothstep(0.55, 1.0, lum);
		// Under the horizon: the horizon's own colour sinking into the haze.
		// (taken from the blurred copy of the sky that lights the scene: a row of the photo itself would smear into streaks)
		vec3 haze = textureLod(RADIANCE, normalize(vec3(EYEDIR.x, 0.06, EYEDIR.z)), 4.0).rgb / max(background_energy, 0.001);
		float below = smoothstep(0.0, -0.25, d.y);
		c = mix(c, mix(haze, ground_color, below), step(d.y, 0.0));
		COLOR = c * background_energy;
	}
}
