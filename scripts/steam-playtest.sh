#!/usr/bin/env bash
# Preview or upload a Windows release folder to the SLOPArena Playtest depot.
# Usage: STEAM_BUILD_USER=name STEAMCMD=/path/to/steamcmd scripts/steam-playtest.sh <version> [--upload]
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 || ( $# == 2 && $2 != --upload ) ]]; then
  echo "usage: steam-playtest.sh <version> [--upload]" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONTENT="$ROOT/build/release/SLOPArena-$1"
SCRIPT="$ROOT/build/steampipe/app_build_5325920.vdf"
STEAMCMD="${STEAMCMD:-steamcmd}"

if [[ ! -f "$CONTENT/SLOPArena.exe" || ! -d "$CONTENT/SLOPArena_Data" ]]; then
  echo "error: no extracted Windows release at $CONTENT; run scripts/build-release.sh $1 first" >&2
  exit 1
fi
if [[ ! -x "$STEAMCMD" ]] && ! command -v "$STEAMCMD" >/dev/null 2>&1; then
  echo "error: SteamCMD not found; set STEAMCMD to the SDK ContentBuilder/builder_linux/steamcmd.sh path" >&2
  exit 1
fi
if [[ -z "${STEAM_BUILD_USER:-}" ]]; then
  echo "error: set STEAM_BUILD_USER to a Steamworks account with build permissions" >&2
  exit 1
fi

mkdir -p "$ROOT/build/steampipe/output"
PREVIEW=1
if [[ ${2:-} == --upload ]]; then PREVIEW=0; fi
cat > "$SCRIPT" <<EOF
"AppBuild"
{
    "AppID" "5325920"
    "Desc" "SLOPArena Playtest $1"
    "Preview" "$PREVIEW"
    "ContentRoot" "$CONTENT"
    "BuildOutput" "$ROOT/build/steampipe/output"
    "Depots"
    {
        "5325921"
        {
            "FileMapping"
            {
                "LocalPath" "*"
                "DepotPath" "."
                "Recursive" "1"
            }
            "FileExclusion" "*_BurstDebugInformation_DoNotShip/*"
            "FileExclusion" "*.pdb"
        }
    }
}
EOF

if [[ $PREVIEW == 1 ]]; then
  echo "Preview only: manifest and logs in build/steampipe/output; no upload."
else
  echo "Uploading to Playtest app 5325920, depot 5325921; no branch will be set live."
fi
"$STEAMCMD" +login "$STEAM_BUILD_USER" +run_app_build "$SCRIPT" +quit
