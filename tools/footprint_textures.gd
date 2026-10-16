extends SceneTree
# Makes the pictures footprints are projected with (scenes/fx/footprint_*.tscn are Decals):
#   godot --headless --path . -s tools/footprint_textures.gd
# For every mech, the outline of its foot (models/mechs/<mech>/Footprint.res, a flat mesh seen from
# above) is drawn into textures/footprints/<mech>.png as a soft mask in the alpha channel: the decal
# darkens the ground through it, so the print reads as pressed in rather than laid on top. Prints the
# size in metres each texture covers (X, Z) and the foot's middle: set them as FootprintSize and
# FootprintCentre on the mech.

const SIDE := 256
const MARGIN := 0.18  # metres of soft edge round the foot


func _initialize():
	DirAccess.make_dir_recursive_absolute("res://textures/footprints")
	for mech in DirAccess.get_directories_at("res://models/mechs"):
		var path := "res://models/mechs/%s/Footprint.res" % mech
		if not ResourceLoader.exists(path):
			continue
		var mesh: Mesh = load(path)
		var box := mesh.get_aabb()
		var size := Vector2(box.size.x, box.size.z) + Vector2.ONE * MARGIN * 2.0
		var origin := Vector2(box.position.x, box.position.z) - Vector2.ONE * MARGIN
		var mask := PackedFloat32Array()
		mask.resize(SIDE * SIDE)
		var arrays := mesh.surface_get_arrays(0)
		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var idx: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		if idx.is_empty():
			for i in verts.size():
				idx.append(i)
		for t in range(0, idx.size(), 3):
			var a := _uv(verts[idx[t]], origin, size)
			var b := _uv(verts[idx[t + 1]], origin, size)
			var c := _uv(verts[idx[t + 2]], origin, size)
			_fill(mask, a, b, c)
		# Soft edges: blurred a few times.
		var r := int(MARGIN / max(size.x, size.y) * SIDE * 0.7)
		for k in 3:
			mask = _blur(mask, max(r, 1))
		var albedo := Image.create(SIDE, SIDE, false, Image.FORMAT_RGBA8)
		for y in SIDE:
			for x in SIDE:
				var m := mask[y * SIDE + x]
				albedo.set_pixel(x, y, Color(1, 1, 1, clamp(m * 1.15, 0.0, 1.0)))
		albedo.save_png("res://textures/footprints/%s.png" % mech)
		print("%s: FootprintSize = Vector2(%.3f, %.3f), centre = (%.3f, %.3f)" % [mech, size.x, size.y, origin.x + size.x * 0.5, origin.y + size.y * 0.5])
	quit()


func _uv(v: Vector3, origin: Vector2, size: Vector2) -> Vector2:
	return (Vector2(v.x, v.z) - origin) / size * SIDE


func _fill(mask: PackedFloat32Array, a: Vector2, b: Vector2, c: Vector2) -> void:
	var lo := a.min(b).min(c).floor()
	var hi := a.max(b).max(c).ceil()
	for y in range(max(int(lo.y), 0), min(int(hi.y) + 1, SIDE)):
		for x in range(max(int(lo.x), 0), min(int(hi.x) + 1, SIDE)):
			var p := Vector2(x + 0.5, y + 0.5)
			var d1 := (b - a).cross(p - a)
			var d2 := (c - b).cross(p - b)
			var d3 := (a - c).cross(p - c)
			if (d1 >= 0 and d2 >= 0 and d3 >= 0) or (d1 <= 0 and d2 <= 0 and d3 <= 0):
				mask[y * SIDE + x] = 1.0


func _at(mask: PackedFloat32Array, x: int, y: int) -> float:
	return mask[clamp(y, 0, SIDE - 1) * SIDE + clamp(x, 0, SIDE - 1)]


func _blur(mask: PackedFloat32Array, r: int) -> PackedFloat32Array:
	var tmp := PackedFloat32Array()
	tmp.resize(SIDE * SIDE)
	var out := PackedFloat32Array()
	out.resize(SIDE * SIDE)
	for y in SIDE:
		for x in SIDE:
			var s := 0.0
			for k in range(-r, r + 1):
				s += _at(mask, x + k, y)
			tmp[y * SIDE + x] = s / (2 * r + 1)
	for y in SIDE:
		for x in SIDE:
			var s := 0.0
			for k in range(-r, r + 1):
				s += _at(tmp, x, y + k)
			out[y * SIDE + x] = s / (2 * r + 1)
	return out
