// Night vision and the thermal sight (VisionFx): the picture of the world (not the interface) through
// an image intensifier — green, amplified, grainy, over the whole view — or a thermal imager — heat as
// light through an "iron" palette, from cold black through blue and red to white-hot.
shader_type canvas_item;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform int mode = 0;          // 1 night vision, 2 thermal
uniform float gain = 5.0;
uniform float grain = 0.08;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

vec3 iron(float t) {
	t = clamp(t, 0.0, 1.0);
	vec3 c = mix(vec3(0.0, 0.0, 0.02), vec3(0.15, 0.0, 0.45), smoothstep(0.0, 0.25, t));
	c = mix(c, vec3(0.75, 0.05, 0.35), smoothstep(0.2, 0.45, t));
	c = mix(c, vec3(1.0, 0.45, 0.0), smoothstep(0.4, 0.68, t));
	c = mix(c, vec3(1.0, 0.95, 0.4), smoothstep(0.65, 0.88, t));
	return mix(c, vec3(1.0), smoothstep(0.88, 1.0, t));
}

void fragment() {
	vec3 col = textureLod(screen, SCREEN_UV, 0.0).rgb;
	float lum = dot(col, vec3(0.299, 0.587, 0.114));
	// the tube's automatic gain: the mean brightness of the frame, from the smallest mips
	vec3 m = textureLod(screen, vec2(0.25, 0.3), 7.0).rgb + textureLod(screen, vec2(0.75, 0.3), 7.0).rgb
		+ textureLod(screen, vec2(0.25, 0.7), 7.0).rgb + textureLod(screen, vec2(0.75, 0.7), 7.0).rgb;
	float avg = max(dot(m * 0.25, vec3(0.299, 0.587, 0.114)), 0.01);
	float n = hash(SCREEN_UV * 900.0 + TIME * 37.0) - 0.5;
	vec2 c = (SCREEN_UV - 0.5) * vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x, 1.0);
	if (mode == 1) {
		// amplified hard in the dark, held back by day so the tube is not washed out
		float k = clamp(0.35 / avg, 1.0, gain);
		float v = 1.0 - exp(-lum * k * 1.3);
		v += n * grain * (1.2 - v);
		// a bloom of the brightest lights
		v += smoothstep(0.85, 1.0, lum) * 0.4;
		// (only the corners a shade darker: the whole view is seen, no tube to look down)
		float tube = 1.0 - 0.3 * smoothstep(0.45, 0.85, length(c));
		float scan = 0.94 + 0.06 * sin(SCREEN_UV.y * 900.0);
		COLOR = vec4(vec3(0.32, 1.0, 0.42) * v * tube * scan, 1.0);
	} else {
		// what glows (the machines, fires) is hot; the dimmed world lies cold under it
		// heat is read against the frame's mean, so the cold scene sits low and what glows stands out
		float rel = lum / max(avg, 0.03);
		float heat = 1.0 - exp(-max(rel - 0.25, 0.0) * 0.32);
		heat += n * grain * 0.6;
		COLOR = vec4(iron(heat), 1.0);
	}
}
