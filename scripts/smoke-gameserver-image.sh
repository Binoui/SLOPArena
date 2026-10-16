#!/usr/bin/env bash
# Usage: scripts/smoke-gameserver-image.sh <local-image-tag>
set -euo pipefail
image="${1:?usage: smoke-gameserver-image.sh <local-image-tag>}"
tmp="$(mktemp -d)"
name="sloparena-image-smoke-$$"
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; rm -rf "$tmp"; }
trap cleanup EXIT
cat > "$tmp/server.json" <<'JSON'
{"deploymentProfile":"development","serverName":"Image smoke","region":"EU","port":7777,"maxConcurrentMatches":1,"masterServerUrl":"http://127.0.0.1:1","publicIp":"127.0.0.1","arenaDataDir":"data/arenas"}
JSON
chmod 644 "$tmp/server.json"
docker run -d --name "$name" --read-only --tmpfs /tmp:uid=1654,gid=1654 \
  -v "$tmp/server.json:/run/config/server.json:ro" -p 127.0.0.1::7777 \
  "$image" /run/config/server.json >/dev/null
port="$(docker port "$name" 7777/tcp | sed -n 's/^127\.0\.0\.1:\([0-9]*\)$/\1/p')"
test -n "$port"
response="$tmp/response.json"
for attempt in {1..30}; do
  if curl -fsS --max-time 3 -H 'Content-Type: application/json' \
    -d '{"matchId":"image-smoke","arenaName":"slop_pit","players":[{"steamId":1,"characterClass":"Manki","entityId":1},{"steamId":2,"characterClass":"FightGuy","entityId":2}]}' \
    "http://127.0.0.1:$port/match/start" -o "$response" 2>/dev/null; then break; fi
  sleep 1
done
jq -e '.port == 7777 and (.content.entries | length) > 0' "$response"
diff -u \
  <(docker exec "$name" cat /app/content-cooked/roster/manifest.json | \
    jq -S '[.entries[] | {packageId, cookedContentHash: .requirement.cookedContentHash, packageHash: .requirement.packageHash}] | sort_by(.packageId)') \
  <(jq -S '[.content.entries[] | {packageId: .identity.packageId, cookedContentHash: .identity.cookedContentHash, packageHash: .identity.packageHash}] | sort_by(.packageId)' "$response")
docker logs "$name" 2>&1 | grep -F '[ArenaRegistry] Loaded: slop_pit'
docker exec "$name" test ! -e /app/server.json
docker exec "$name" test -f /app/content-cooked/roster/manifest.json
docker exec "$name" test -f /app/data/arenas/training.arena
docker exec "$name" sh -c 'test "$(id -u)" = 1654'
echo 'Image-only startup, arena load and roster catalog match admission passed.'
