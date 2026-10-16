// Drawing a part over everything else on the map: the player's own cab, arms and guns seen from the
// cockpit (switched on per part by Mech.DrawOver). Its depth is squeezed into the sliver right in front
// of the camera, keeping the order of its own parts, so a gun poking into a wall is still seen whole.
// Shadows are cast from where the part really is.
instance uniform float in_front = 0.0;

// (A shader that sets POSITION anywhere must always set it: the engine no longer works it out itself.)
#define IN_FRONT_VERTEX { vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)); if (in_front > 0.5 && !IN_SHADOW_PASS) { clip.z = clip.w * 0.99 + clip.z * 0.01; } POSITION = clip; }
