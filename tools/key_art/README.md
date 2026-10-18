# Key art derivation

Turns generated key art (see [Key art prompts](../../docs/design/key-art.md)) into every
derived image: the game's splashes, portraits, Home poster, Arcade cards, wallpaper and
After Hours billboard; the Steam store, library and community set; and the website's
portraits and trailer poster. Only Pillow is needed.

A set file lists the originals and each output. [`set-2026-10-18.json`](set-2026-10-18.json)
is the current set; its originals are the owner's local images in `~/Downloads/sloparena/newart/`.

```bash
python3 tools/key_art/key_art.py build tools/key_art/set-2026-10-18.json            # everything
python3 tools/key_art/key_art.py build tools/key_art/set-2026-10-18.json --only steam
python3 tools/key_art/key_art.py build tools/key_art/set-2026-10-18.json --root game /tmp/check  # dry run
```

Roots: `game` is `client/Unity/Assets`, `steam` the upload folder, `web` a SlopArena-web
checkout beside this one, `repo` this repository (read-only sources such as the bomb icon).
An output is a byte copy, or a `crop` (`[left, top, right, bottom]` in source pixels) resized
to `size` (the crop must already have that aspect), optionally with a `mark`, an `icon`
background or the typeset `logo`. Steam files embed an sRGB profile.

## Name mark

The name is **SLOPArena** on one line, in the bundled Archivo Black: Ink lettering over an
off-register Orange print. Never stack SLOP / ARENA or set SLOPARENA in capitals; the capitals
spell the shorthand SLOPA. A `mark` gives the centre, size and angle on a plate in the source
image (`"tagline": true` adds a `GET IN THE SLOP.` strip; base Steam capsules carry only the
name). `logo` draws the website's torn, dotted yellow plate with the name, on transparency.
`namemark` stamps the mark on any other image:

```bash
python3 tools/key_art/key_art.py namemark in.png out.png --centre 812 488 --size 112 --angle 3.2
```

## Checks

Portraits: keep the face horizontally centred, since the tall fighter-select card shows only
the middle ~60% of the square, and compare all four at 64 px. Look at the Steam small capsule
at its real 462x174: the mark must stay legible.

Files changed outside Unity: import exactly them through the gateway's `import_asset`
(one command per file, `--confirm true`) instead of refreshing the whole project, under your
own lease ([coordination](../../docs/contributing/unity-editor-coordination.md)). The website
needs `scripts/social-preview.html` re-rendered after a portrait change, as its comment says.
