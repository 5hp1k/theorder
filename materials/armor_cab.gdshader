// The armour of the player's own machine while the pilot looks out of its cockpit: as armor.gdshader,
// but whatever of it is inside the cab is not drawn (Mech.DrawOver swaps it in).
shader_type spatial;
#define CAB_CUT
#include "res://materials/armor.gdshaderinc"
