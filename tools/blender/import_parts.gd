extends SceneTree
# Turns a model exported by one of the Blender scripts into mesh resources the scenes can use:
#   godot --headless --path . -s tools/blender/import_parts.gd -- <model.glb> <res://models/...>
# Every mesh in the file is saved as <its name>.res, with levels of detail, without materials
# (the scenes give each part its material).
func _initialize():
	var args := OS.get_cmdline_user_args()
	var source: String = args[0]
	var target: String = args[1]
	DirAccess.make_dir_recursive_absolute(target)
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_file(source, state)
	if err != OK:
		push_error("cannot read %s" % source)
		quit(1)
		return
	var names := {}
	for node in state.get_nodes():
		if node.mesh >= 0: names[node.mesh] = node.resource_name
	var meshes := state.get_meshes()
	var total := 0
	for i in meshes.size():
		var im: ImporterMesh = meshes[i].mesh
		for s in im.get_surface_count(): im.set_surface_material(s, null)
		im.generate_lods(25.0, 60.0, [])
		var mesh := im.get_mesh()
		var tris := 0
		for s in mesh.get_surface_count(): tris += mesh.surface_get_array_index_len(s) / 3
		total += tris
		var has_colour := (mesh.surface_get_format(0) & Mesh.ARRAY_FORMAT_COLOR) != 0
		if not has_colour: push_warning("%s has no vertex colours" % names[i])
		ResourceSaver.save(mesh, "%s/%s.res" % [target, names[i]], ResourceSaver.FLAG_COMPRESS)
	print("saved %d meshes, %d triangles, to %s" % [meshes.size(), total, target])
	quit()
