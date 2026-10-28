#!/bin/sh
# Put the salamander's kit proposal into his package: back up the package source, promote each candidate clip from
# Assets/Art/Experiments/GenRA/salamander/ into Animations/ (clips.json names the targets), write the move data
# (apply_kit.py), bind the catalog (bind_catalog.eval.cs) and cook + verify through the gateway.
# usage: tools/blender/salamander_rig/integrate_kit.sh <backup_dir>
# Revert: copy <backup_dir>/character.json and CharacterAssetCatalog.asset back, then sloparena.character.cook.
set -e
cd /home/binoui/Documents/projects/SlopArena
BK=$1; [ -n "$BK" ] || { echo "usage: $0 <backup_dir>"; exit 2; }
PKG=client/Unity/Assets/CharacterPackages/salamander
PY=artifacts/genra-20261014/.venv/bin/python
D=$PWD/tools/blender/salamander_rig
mkdir -p "$BK"
cp $PKG/character.json $PKG/CharacterAssetCatalog.asset "$BK/"
# candidate asset (experiments) per semantic id; the retimed Big Iron blast replaces the authored one
python3 - "$D/clips.json" > /tmp/salamander-promote.txt <<'EOF'
import json, sys
clips = json.load(open(sys.argv[1]))
exp = 'Assets/Art/Experiments/GenRA/salamander/'
src = {'idle': 'salamander-idle-r2-slow3', 'run': 'salamander-run-r1', 'g1': 'salamander-g1-backhand-slap',
       'g3': 'salamander-g3-uppercut', 'g4': 'salamander-g4-tail-spin', 'a1': 'salamander-a1-double-tap',
       'a3': 'salamander-a3-tail-whip', 'a4': 'salamander-a4-spur-stomp', 'ga': 'salamander-ga-finger-gun-shot-r2b',
       'ga-reload': 'salamander-ga-finger-gun-reload-a1', 'ge': 'salamander-ge-sombrero-copter',
       'gr': 'salamander-gr-tumbleweed', 'gf-loop': 'salamander-gf-big-iron-aim', 'gf': 'salamander-gf-big-iron-blast-r'}
for sid, target in clips.items():
    print(exp + src[sid.replace('anim.salamander.', '')] + '.anim', target)
EOF
while read -r SRC DST; do
  if [ -f "client/Unity/$SRC" ]; then
    $PY tools/genra/unity_clip.py promote "$SRC" --over "$DST" > /dev/null && echo "promoted $SRC"
  else
    echo "missing $SRC (stays on its stand-in)"
  fi
done < /tmp/salamander-promote.txt
python3 tools/blender/salamander_rig/apply_kit.py
cat > /tmp/salamander-integrate.json <<EOF
{"commands":[["eval_file","--file","$D/bind_catalog.eval.cs"],["sloparena.character.inspect","--target","salamander"],
 ["sloparena.character.cook","--target","salamander"],["sloparena.character.verify","--target","salamander"]],"restore":[]}
EOF
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch /tmp/salamander-integrate.json
