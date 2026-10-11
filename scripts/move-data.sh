#!/usr/bin/env bash
# Generate a move data report (frame data + simulated trajectories + combo matrix)
# from the shared sim. Default: FightGuy, 6 percents, docs/generated/fightguy-move-data.md.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tools/MoveDataReport -p:SkipUnityPluginCopy=true -- "$@"
