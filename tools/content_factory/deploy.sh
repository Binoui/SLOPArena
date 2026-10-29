#!/bin/sh
# Copy the Slop Factory to alfred and restart it. The database, media and config.json (the T3 webhook URL)
# live in ~/content-factory/data on alfred and are never copied back; secrets/ is provisioned once by hand.
# The Master console image is built on alfred from the committed HEAD of $CONSOLE_REPO.
set -eu
cd "$(dirname "$0")"
host=${FACTORY_HOST:-alfred}
console_repo=${CONSOLE_REPO:-../../../SlopArena-console}
ssh "$host" 'mkdir -p content-factory/app content-factory/data && test -f content-factory/secrets/caddy.env' ||
  { echo "alfred has no content-factory/secrets: see docs/systems/slop-factory.md" >&2; exit 1; }
scp -q server.py index.html Caddyfile ../../client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf "$host:content-factory/app/"
scp -q compose.yml "$host:content-factory/compose.yml"
git -C "$console_repo" archive HEAD | ssh "$host" 'docker build -q -t sloparena-console:alfred - >/dev/null'
# The app files are bind mounts, so compose sees no change: restart the two services that read them.
ssh "$host" 'cd content-factory && docker compose up -d --build --remove-orphans --quiet-pull 2>&1 | tail -5 &&
  docker compose restart factory front 2>&1 | tail -2'
