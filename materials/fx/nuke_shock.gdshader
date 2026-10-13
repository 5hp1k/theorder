// The shock front of a nuclear explosion: a thin shell of condensed air on a sphere, seen only where
// it is looked at edge-on. NukeBlast blows it up and fades it.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, unshaded;

uniform vec3 color : source_color = vec3(0.95, 0.95, 0.97);
uniform float strength : hint_range(0.0, 1.0) = 0.55;
/// How thin the shell looks: higher = only the very rim.
uniform float rim = 4.0;
/// 1 → 0 as it spreads.
instance uniform float alpha = 1.0;

void fragment() {
	ALBEDO = color;
	ALPHA = pow(1.0 - abs(dot(NORMAL, VIEW)), rim) * strength * alpha;
}
