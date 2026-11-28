#!/usr/bin/env bash
# Composes every Class 1 lesson: pictures + poster + body into $OUT/<key>/.
# Usage (Cloud Shell, repo root): GRIDS=~/w/b OUT=~/w/out bash tools/syllabus/class1/compose_all.sh
# GRIDS holds grid_<key>.png from gen_grid.py (ml-MATHS/ml-ICT reuse the en grids; redraws: redo_<gridkey>_<N>.png).
# ml-MAL-01: panels 3-5 are single pictures (prompts/prompt_ml-MAL-01-pN.txt, 1024x1024) saved as redo_ml-MAL-01_N.png.
set -u
HERE=$(cd "$(dirname "$0")" && pwd); TOOLS=$(dirname "$HERE")
: "${GRIDS:=$HOME/w/b}"; : "${OUT:=$HOME/w/out}"; : "${FONT_DIR:=$HOME/w/fonts}"; export FONT_DIR
mkdir -p "$OUT/lessons" "$FONT_DIR"
[ -f "$FONT_DIR/BalooChettan2.ttf" ] || curl -fsSL -o "$FONT_DIR/BalooChettan2.ttf" "https://github.com/google/fonts/raw/main/ofl/baloochettan2/BalooChettan2%5Bwght%5D.ttf"
[ -f "$FONT_DIR/NotoSansMalayalam.ttf" ] || curl -fsSL -o "$FONT_DIR/NotoSansMalayalam.ttf" "https://github.com/google/fonts/raw/main/ofl/notosansmalayalam/NotoSansMalayalam%5Bwdth,wght%5D.ttf"
cp "$HERE"/lessons/*.json "$OUT/lessons/"
for j in "$HERE"/lessons/*.json; do
  k=$(basename "$j" .json); g=${k/ml-MATHS/en-MATHS}; g=${g/ml-ICT/en-ICT}
  grid="$GRIDS/grid_$g.png"; [ -f "$grid" ] || { echo "no grid for $k"; continue; }
  ov=$(ls "$GRIDS"/redo_${g}_*.png 2>/dev/null | sed -E 's/.*_([0-9])\.png$/\1=&/')
  python3 "$TOOLS/compose_lesson.py" "$j" "$grid" "$OUT/$k" $ov >/dev/null || echo "FAILED $k"
done
ls -d "$OUT"/*-*/ | wc -l
