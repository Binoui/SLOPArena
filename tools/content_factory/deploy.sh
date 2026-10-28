#!/bin/sh
# Copy the content factory to alfred and restart it. The database, media and config.json (the T3 webhook URL)
# live in ~/content-factory/data on alfred and are never copied back.
set -eu
cd "$(dirname "$0")"
host=${FACTORY_HOST:-alfred}
ssh "$host" 'mkdir -p content-factory/app content-factory/data'
scp -q server.py index.html ../../client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf "$host:content-factory/app/"
scp -q compose.yml "$host:content-factory/compose.yml"
ssh "$host" 'cd content-factory && docker compose up -d --force-recreate --quiet-pull 2>&1 | tail -3'
