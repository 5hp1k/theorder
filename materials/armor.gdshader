// Painted metal of a war machine. The paint (a flat colour, or a camouflage pattern) is wrapped round
// the part from three sides, so the parts need no texture coordinates. It wears through to bare metal
// on the sharp edges and in scattered chips, and dirt sits in the seams and in blotches. Where the
// edges and seams are is written into the mesh's vertex colours by ArmorBoxMesh / ArmorTubeMesh
// (red: 0 = edge, green: 0 = recess); a mesh without colours is simply painted all over.
shader_type spatial;
#include "res://materials/armor.gdshaderinc"
