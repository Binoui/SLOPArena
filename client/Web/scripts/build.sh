#!/usr/bin/env bash
# Publishes the trimmed WASM sim the page loads from client/Web/publish/.
set -euo pipefail
cd "$(dirname "$0")/../sim"
dotnet publish -c Release -o ../publish -p:PublishTrimmed=true
