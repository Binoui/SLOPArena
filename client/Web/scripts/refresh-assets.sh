#!/usr/bin/env bash
# Bring client/Web/assets up to date with Unity and pin it for CI, in one go:
#   1. re-export every fighter whose rig, clips or converter changed (asset-sources.py), through a read-only
#      gateway snapshot of the running Unity Editor (about a minute per fighter),
#   2. re-export the effects (no Editor needed; CI also does this on every deploy),
#   3. upload the bundle and rewrite assets.lock + assets.sources.json.
# Then commit client/Web/assets.lock and client/Web/assets.sources.json.
#
#   refresh-assets.sh              stale fighters only
#   refresh-assets.sh nyx bonk     also force these fighters
#   refresh-assets.sh --dirty      include fighters whose Unity sources have uncommitted changes
#
# Fighters with uncommitted Unity changes are skipped by default: the Editor shows the working tree,
# and the public demo should show what is on main.
set -euo pipefail
cd "$(dirname "$0")/../../.."
PY="${PY:-artifacts/genra-20261014/.venv/bin/python}"
sources=client/Web/scripts/asset-sources.py

dirty_ok=0; forced=()
for arg in "$@"; do
  case "$arg" in
    --dirty) dirty_ok=1 ;;
    -*) echo "unknown option $arg" >&2; exit 2 ;;
    *) forced+=("$arg") ;;
  esac
done

mapfile -t fighters < <({ python3 "$sources" stale; printf '%s\n' "${forced[@]}"; } | sed '/^$/d' | sort -u)
if [ "${#fighters[@]}" -gt 0 ] && [ "$dirty_ok" = 0 ]; then
  mapfile -t dirty < <(python3 "$sources" dirty "${fighters[@]}")
  if [ "${#dirty[@]}" -gt 0 ]; then
    echo "skipping (uncommitted Unity changes; commit them or pass --dirty): ${dirty[*]}"
    mapfile -t fighters < <(printf '%s\n' "${fighters[@]}" | grep -vxF -f <(printf '%s\n' "${dirty[@]}"))
  fi
fi

if [ "${#fighters[@]}" -gt 0 ]; then
  echo "re-exporting fighters: ${fighters[*]}"
  "$PY" client/Web/tools/fighter_glb.py all "${fighters[@]}"
  for f in "${fighters[@]}"; do
    python3 -c 'import json,sys; r=json.load(open(sys.argv[1])); sys.exit(0 if r["passed"] else f"{sys.argv[1]}: export failed its checks (skin error or missing clips)")' \
      "client/Web/assets/fighters/$f.report.json"
  done
  python3 "$sources" stamp "${fighters[@]}"
else
  echo "fighter models are current"
fi

node client/Web/scripts/vfx-reference.mjs --write
node client/Web/scripts/export-vfx.mjs > /dev/null
client/Web/scripts/assets.sh upload
git status --short client/Web/assets.lock client/Web/assets.sources.json
