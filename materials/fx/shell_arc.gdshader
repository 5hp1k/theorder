// The howitzer's trajectory line and landing ring (scenes/fx/shell_arc.tscn): a dashed, glowing strip.
// UV.x runs along the line in metres, UV.y across it.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, blend_mix, depth_draw_never;

uniform vec4 tint : source_color = vec4(1.0, 0.75, 0.2, 0.9);
/// Length of a dash and of the gap after it, metres.
uniform float dash = 5.0;
uniform float gap = 3.0;
/// How bright it glows.
uniform float glow = 2.2;

void fragment() {
	float along = mod(UV.x, dash + gap);
	float on = smoothstep(0.0, 0.25, along) * (1.0 - smoothstep(dash - 0.25, dash, along));
	float across = 1.0 - abs(UV.y * 2.0 - 1.0);
	ALBEDO = tint.rgb * glow;
	ALPHA = tint.a * on * smoothstep(0.0, 0.5, across);
}
