#!/usr/bin/env bash
# client/Web/assets/ is exported locally and not tracked. CI gets it as a bundle on the
# web-assets release, pinned by the sha256 in client/Web/assets.lock.
#   assets.sh upload   bundle the local assets/, upload it and rewrite assets.lock
#   assets.sh fetch    download the pinned bundle into assets/ and verify it
set -euo pipefail
web="$(cd "$(dirname "$0")/.." && pwd)"
tag=web-assets
lock="$web/assets.lock"

case "${1:-}" in
  upload)
    tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
    tar --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner -C "$web" -cf - assets | gzip -n > "$tmp/bundle.tar.gz"
    sha="$(sha256sum "$tmp/bundle.tar.gz" | cut -d' ' -f1)"
    if [ "$(cat "$lock" 2>/dev/null)" = "$sha" ]; then echo "assets unchanged ($sha)"; exit 0; fi
    mv "$tmp/bundle.tar.gz" "$tmp/web-assets-$sha.tar.gz"
    gh release view "$tag" >/dev/null 2>&1 || gh release create "$tag" --prerelease --latest=false \
      --title "Web demo assets" --notes "Bundles of client/Web/assets, pinned by client/Web/assets.lock."
    gh release upload "$tag" "$tmp/web-assets-$sha.tar.gz"
    echo "$sha" > "$lock"
    echo "uploaded web-assets-$sha.tar.gz; commit client/Web/assets.lock"
    ;;
  fetch)
    sha="$(cat "$lock")"
    tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
    gh release download "$tag" --pattern "web-assets-$sha.tar.gz" --dir "$tmp"
    echo "$sha  $tmp/web-assets-$sha.tar.gz" | sha256sum -c --quiet
    rm -rf "$web/assets"
    tar -xzf "$tmp/web-assets-$sha.tar.gz" -C "$web"
    echo "assets $sha"
    ;;
  *) echo "usage: $0 upload|fetch" >&2; exit 2 ;;
esac
