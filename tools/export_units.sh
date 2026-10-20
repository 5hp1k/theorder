#!/bin/bash
# Checks the units as the game assembles them for loose pieces, fighting faces and clipping weapons
# (see tools/blender/check_assembled.py):
#     tools/export_units.sh [walk] [default] [unit ...]
# "walk": the mechs caught in mid-stride; "default": with the catalogue's weapons, not the player's.
# Needs godot-mono and blender on the PATH (or GODOT / BLENDER set). Writes the OBJ files to $OUT
# (default /tmp/units) and prints one line per fault, and a summary line per unit.
cd "$(dirname "$0")/.."
GODOT=${GODOT:-godot-mono}
BLENDER=${BLENDER:-blender}
OUT=${OUT:-/tmp/units}
flags=()
units=()
for a in "$@"; do
  case $a in walk|default) flags+=("$a");; *) units+=("$a");; esac
done
[ ${#units[@]} -eq 0 ] && units=(agni ballista cleaver executioner gladiator hades hound ostrich panther amphibian apc bomber helicopter laser_tank mlrs ship spg tank)
mkdir -p "$OUT"
for u in "${units[@]}"; do
  timeout 120 "$GODOT" --headless --path . tools/export_unit.tscn -- "$u" "$OUT" "${flags[@]}" > "$OUT/$u.log" 2>&1
  if ! grep -q EXPORTED "$OUT/$u.log"; then echo "$u: export failed (see $OUT/$u.log)"; continue; fi
  "$BLENDER" -b --python tools/blender/check_assembled.py -- "$OUT/$u.obj" 2>/dev/null | grep "^$u:"
done
