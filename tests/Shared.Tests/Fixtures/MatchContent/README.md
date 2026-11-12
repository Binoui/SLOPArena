# Online map compatibility fixture

Captured on 2026-11-12 by running the unmodified Shared project from `origin/main`
commit `b9276e5806a3e1c8896b558b7d2079ebb2e81f48` through a temporary console app.
The app loads the committed seven-package roster, builds its catalog, and serializes schema 2
with arena `slop_pit` and digest consisting of 64 lowercase `d` characters.
`schema2-origin-main.json` contains the exact UTF-8 bytes without a trailing newline.
`schema2-origin-main.sha256` contains `SteamMatchDescriptor.HashContent` without a newline.

`PackageFighterContentTests.OnlineSchema2MatchesOriginMainBytesAndHash` compares both results.
Updating this fixture requires an intentional online content change and a fresh baseline capture.
