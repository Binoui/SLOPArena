# HUD move icons

`HUDManager` loads these sprites as `UI/AbilityCards/{CharacterClass}/{IconName}` for the
move keys; nothing else uses them. The HUD shows each fighter's grounded set as a fixed
legend. `Air/` variants and the older `Icons/` art remain as assets and fallback. Each PNG
embeds its exact generation prompt as `impeccable:prompt` and carries no gameplay authority.

## Locked style: linocut moves (2026-10-17)

Chosen from a 16-image test (cut-paper, sticker, stencil, linocut) judged inside the keys
at real size. Every icon shows **the move, not the fighter**: the striking limb, weapon or
projectile plus its effect, cropped tight, with the motion travelling a clear direction
(forward moves right, risers up, slams down).

| | Normal (tab) | Special (key) |
|---|---|---|
| Shown at | 30 px on a paper tab | 56 px on an ink key |
| Main shapes | ink `#1B1A18`, carving filled solid | paper `#F0EEE8`, carving kept |
| Accent | fighter colour, motion/effect only | fighter colour, motion/effect only |

Fighter accents: FightGuy `#39A9FF`, Manki `#E05C2A`, Wibou `#91FF69`, Bonk `#B8B2A8`.
Never use Action Yellow `#FFCC22`; the HUD reserves it for held and ready states.

Status: all four fighters' grounded sets use this style (2026-10-17). The `Air/` variants still
use the previous full-body illustrations; the HUD does not show them.

## Making a set

1. Generate 1024×1024 transparent PNGs with the template below, filled verbatim. Attach
   FightGuy's `icon.a.png`, `icon.r.png`, `icon.e.png` and `icon.f.png` as style references
   and end the prompt with: "The reference images show the required style only; do not
   copy their subjects." Keep one generation per icon; regenerate only for text, frames,
   scenery, a full body or a face.
2. Clean each image (ImageMagick 7 required); add `--solid` for normals:

   ```bash
   python3 tools/hud_icon_prep.py raw/fightguy-1.png FightGuy/icon.1.png \
       --accent '#39A9FF' --solid --manifest raw/manifest.json
   ```

   It snaps colours to the palette, crisps edges, normalises the fill, exports 256 px and
   embeds the prompt from the generation manifest.
3. Review the whole set inside the keys at real size over a stage frame before installing.
   If two icons blur together at 40 px, regenerate one.
4. Overwrite the PNGs in place, preserving `.meta` GUIDs and sprite IDs. If the image
   dimensions change, update the existing sprite slice in Unity's Sprite Editor to the
   full new canvas (`x: 0, y: 0, width: 256, height: 256` for prepared icons), then apply.
   Reimport and verify `Resources.LoadAll<Sprite>` returns one sprite per icon; stale
   slices outside the new canvas produce no sprites and make the HUD use the older art.

```text
Game ability icon for a 56-pixel HUD key in SLOPArena, a scrappy underground fight-club platform fighter. Square 1024x1024 canvas, transparent background.
Subject: {SUBJECT}
Show only the move: crop tight on the striking limb, weapon or projectile. At most a hand, forearm or shoulder of the fighter, never a full body, never a face.
Direction: the motion clearly travels {DIRECTION}.
Style: hand-carved linocut stamp print: chunky gouged shapes with slightly irregular edges and a few bold carving marks, printed flat.
Palette: {PALETTE} Flat colour only: no gradients, no shading, no other colours.
Composition: one dominant shape group filling about 85% of the canvas, centred by visual weight, with the effect streak carrying the direction. Large, simple shapes that still read as a 40-pixel silhouette.
No text, letters, numbers, frames, borders, badges, background scenery or drop shadows.
```

`{PALETTE}` for normals: `main shapes in ink #1B1A18 (they sit on a light paper background); {accent colour} for the {effect} only.`
For specials: `main shapes in paper #F0EEE8 (they sit on a dark ink background); {accent colour} for the {effect} only.`

Keep PNGs and `.meta` files together and preserve alpha and the sprite import settings
(256 px maximum). Raw generations, manifests, mockups and review captures stay outside Git.
See the [visual contract](../../../../../../DESIGN.md).
