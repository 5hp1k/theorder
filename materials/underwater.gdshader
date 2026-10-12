// Full-screen water on the camera: "amount" = under the surface (the view swims and turns murky),
// "wet" = just surfaced (water runs down the lens). Driven by WaterOverlay.cs.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear, repeat_disable;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float wet : hint_range(0.0, 1.0) = 0.0;
uniform vec4 tint : source_color = vec4(0.05, 0.3, 0.4, 1.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	// Under water everything sways.
	vec2 sway = vec2(sin(uv.y * 18.0 + TIME * 1.7), cos(uv.x * 14.0 + TIME * 1.3)) * 0.008 * amount;
	// Out of it, streaks of water run down and bend what's behind them.
	float drops = smoothstep(0.62, 0.9, noise(vec2(uv.x * 22.0, uv.y * 9.0 - TIME * 0.6))) * wet;
	vec3 col = texture(screen_texture, uv + sway + vec2(drops * 0.02, drops * 0.035)).rgb;
	float edge = smoothstep(0.25, 0.85, distance(uv, vec2(0.5)));
	col = mix(col, tint.rgb, amount * (0.45 + 0.4 * edge));
	col += drops * 0.08;
	COLOR = vec4(col, 1.0);
}
