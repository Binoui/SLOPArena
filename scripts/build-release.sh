#!/usr/bin/env bash
# Build a Windows demo release: SLOPArena-<version>.zip in build/release/.
# Usage: scripts/build-release.sh <version>   e.g. scripts/build-release.sh 0.2.0-demo.1
set -euo pipefail

VERSION="${1:?usage: build-release.sh <version>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/6000.0.78f1/Editor/Unity}"
PROJ="$ROOT/client/Unity"
REL="$ROOT/build/release/SLOPArena-$VERSION"
SA="$PROJ/Assets/StreamingAssets"
verify_roster_payloads() {
  local tree="$1"
  local package_ids
  test -f "$tree/roster/manifest.json"
  cmp "$ROOT/content-cooked/roster/manifest.json" "$tree/roster/manifest.json"
  package_ids="$(jq -er '.entries[].packageId' "$ROOT/content-cooked/roster/manifest.json")"
  while IFS= read -r package_id; do
    test -n "$package_id"
    for package_file in manifest.json character.runtime.json poses.bin client.bindings; do
      test -f "$tree/$package_id/$package_file"
    done
    cmp "$ROOT/content-cooked/$package_id/manifest.json" "$tree/$package_id/manifest.json"
  done <<< "$package_ids"
}

echo "== Verify complete cooked roster =="
verify_roster_payloads "$ROOT/content-cooked"

# Preserve existing ignored StreamingAssets content, including local skeletons.
# Only the staging created by this build is disposable.
git -C "$ROOT" diff --quiet -- client/Unity/ProjectSettings/ProjectSettings.asset \
  || { echo "error: ProjectSettings.asset has uncommitted changes" >&2; exit 1; }
mkdir -p "$ROOT/build" "$SA"
STAGE_BACKUP="$(mktemp -d "$ROOT/build/.release-stage.XXXXXX")"
cp -p "$PROJ/ProjectSettings/ProjectSettings.asset" "$STAGE_BACKUP/ProjectSettings.asset"
STAGE_READY=0
restore_stage() {
  local name
  for name in Server arenas data content content-cooked; do
    if [[ -e "$STAGE_BACKUP/$name" || -L "$STAGE_BACKUP/$name" ]]; then
      rm -rf "$SA/$name"
      mv "$STAGE_BACKUP/$name" "$SA/$name"
    elif (( STAGE_READY )); then
      rm -rf "$SA/$name"
    fi
  done
  if (( STAGE_READY )); then
    cp -p "$STAGE_BACKUP/ProjectSettings.asset" "$PROJ/ProjectSettings/ProjectSettings.asset"
  fi
  rm -rf "$STAGE_BACKUP"
}
trap restore_stage EXIT
for name in Server arenas data content content-cooked; do
  if [[ -e "$SA/$name" || -L "$SA/$name" ]]; then
    mv "$SA/$name" "$STAGE_BACKUP/$name"
  fi
done
STAGE_READY=1
echo "== Shared build =="
dotnet build "$ROOT/src/Shared/" --nologo

echo "== Tests =="
dotnet test "$ROOT/tests/Shared.Tests/" --nologo

echo "== Self-contained Windows server (embedded host-and-play) =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release -r win-x64 --self-contained true -o "$SA/Server"
# The csproj copies server.json (dev defaults, localhost:5000) into publish
# output; both release flows (bundled host-and-play, dedicated compose) pass an
# explicit config path as arg[0], so the shipped file is dead weight AND leaks
# localhost:5000 into the zip (Task 7.2: must appear NOWHERE). Drop it.
rm -f "$SA/Server/server.json"
verify_roster_payloads "$SA/Server/content-cooked"

echo "== linux-x64 server for the mini PC =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release -r linux-x64 --self-contained false -o "$ROOT/build/minipc"
# Same rationale: rsync'ing this onto alfred must not clobber the live
# server.json (real masterServerUrl + publicIp).
rm -f "$ROOT/build/minipc/server.json"
verify_roster_payloads "$ROOT/build/minipc/content-cooked"
echo "== Stage canonical cooked roster =="
mkdir -p "$SA/arenas" "$SA/content-cooked" "$SA/Server/content-cooked"
cp "$ROOT"/data/arenas/*.arena "$SA/arenas/"
cp -R "$ROOT/content-cooked/." "$SA/content-cooked/"
cp -R "$ROOT/content-cooked/." "$SA/Server/content-cooked/"
verify_roster_payloads "$SA/content-cooked"
verify_roster_payloads "$SA/Server/content-cooked"
test ! -e "$SA/content/characters/fightguy/character.json"
test ! -e "$SA/Server/content/characters/fightguy/character.json"
test ! -e "$SA/data/fightguy_skeleton.bin"
test ! -e "$SA/Server/data/fightguy_skeleton.bin"

echo "== Version stamp =="
# The original stamp is restored from the private stage backup on success or failure.
sed -i "s/^  bundleVersion: .*/  bundleVersion: $VERSION/" "$PROJ/ProjectSettings/ProjectSettings.asset"

echo "== Unity Windows player build =="
mkdir -p "$REL"
"$UNITY" -batchmode -quit -projectPath "$PROJ" -buildWindows64Player "$REL/SLOPArena.exe"

echo "== Ship docs + zip =="
cp "$ROOT/docs/release/PLAY_GUIDE.md" "$REL/README.txt"
cp "$ROOT/docs/release/HOST_GUIDE.md" "$REL/HOSTING.txt"
(cd "$REL/.." && zip -r "SLOPArena-$VERSION.zip" "SLOPArena-$VERSION")
restore_stage
trap - EXIT
echo "DONE: build/release/SLOPArena-$VERSION.zip"
