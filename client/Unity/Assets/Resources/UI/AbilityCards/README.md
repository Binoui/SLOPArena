# HUD move icons

`HUDManager` loads these sprites as `UI/AbilityCards/{CharacterClass}/{iconId}` for the move keys,
where `iconId` comes from each slot in the package's `character.json`. FightGuy's and Bashie's slot 1
use `icon.2`. Nothing else loads these sprites. The HUD shows each fighter's grounded set as a fixed
legend. `Air/` variants and the older `Icons/` art remain as assets and fallback. Each PNG embeds its
exact generation prompt as `impeccable:prompt` and carries no gameplay authority.

## Locked style: glyph moves (2026-10-28)

The style was chosen by the owner on 2026-10-28 from a three-way trial on Sir Bonk's Q, R and F, judged
inside the keys at real size. It replaced the 2026-10-17 linocut set, which turned to grey noise at key
size and did not match the bright toon game.

Each icon is a flat pictogram: one solid silhouette plus one accent shape for the motion. It shows
whatever explains the move. That means the whole fighter when the body performs it (a lunge, kick,
leap, spin, roll or slam), and the hand and prop only when a thrown object is the move. As the owner
put it, "if you just show a sword or a horse you don't visually explain it."

| | Normal (tab) | Special (key) |
|---|---|---|
| Shown at | 30 px on a paper tab | 56 px on an ink key |
| Silhouette | ink `#1B1A18`, filled solid | paper `#F0EEE8` |
| Accent | fighter colour, motion arc, speed lines or burst only | same |

Fighter accents: Sir Bonk coral `#E8524A`, FightGuy `#39A9FF`, Manki `#E05C2A`, Wibou `#91FF69`,
Bashie aqua `#22CCD0`, Salamander violet `#B57BFF`. Never use Action Yellow `#FFCC22`. The HUD
reserves it for held and ready states.

The subject always comes from the live model. Each move is rendered from its bound clip, so the
icons follow model and prop swaps instead of a prompt's guess.

## Making or redoing a set

[`tools/hud_icons/hud_icons.py`](../../../../../../tools/hud_icons/hud_icons.py) runs every step from
[`tools/hud_icons/decks.json`](../../../../../../tools/hud_icons/decks.json). For each package, that
file holds the folder, accent and fighter line. For each slot, it holds the frame, subject and
direction, plus any clip, tick or rig pack override. Work goes to ignored `tmp/hud-icons/<package>/`.

1. `render <package>` plays each grounded move's clip on the catalog prefab through GenRA's Unity
   renderer, on a magenta backdrop. It writes `sheet.png`, a contact sheet of both views. It needs the
   GenRA venv and rig packs (see the [GenRA README](../../../../../../tools/genra/README.md)) and your
   own gateway lease. Props that only some moves show need their rig pack in `decks.json`, for example
   Manki's aerosol (`manki-unity-aerosol`) or Sir Bonk's hobby horse (`bonk-unity-sirjoust`). Pick the
   frame that reads as the move and record it as `"frame": "view:tick"`.
2. `refs <package>` cuts those frames out as `refs/<slot>.png`.
3. `job <package>` writes `job.md`, with one filled prompt per slot. Generate one image per slot with
   the slot's ref as Image 1 and the installed FightGuy `icon.r.png` as the style anchor (Image 2).
   Save the results as `gen/<slot>.png` beside `gen/<slot>.prompt.txt`. The Codex image tool through a
   T3 child task works: give it `job.md`. Keep one generation per icon. Regenerate only for text,
   frames, backing shapes or scenery, or when the move does not read at 30 px. In that case, put the
   accent shape that must carry the move into the subject. Bashie's shell normals needed a ring, a
   loop and a diagonal.
4. `prep <package>` cleans each image with [`hud_icon_prep.py`](../../../../../../tools/hud_icon_prep.py).
   The cleanup snaps colours to the palette, crisps edges, fills normals solid, exports 256 px and
   embeds the prompt.
5. `board <packages...>` draws the deck at real size and 3× over a stage frame. Review it there. If
   two icons blur together at real size, regenerate one.
6. `install <package>` overwrites the sprites by `iconId`, keeping the existing `.meta` GUIDs. Images
   stay 256 px, so the existing 256×256 sprite slices still apply. A new fighter's files get a
   `.meta` copied from an installed icon with a fresh GUID. Import exactly the printed paths under
   your own lease (`AssetDatabase.ImportAsset`). Check that `Resources.LoadAll<Sprite>` returns one
   sprite per icon. Stale slices outside the canvas produce no sprites and make the HUD fall back to
   the older art.

Keep PNGs and `.meta` files together and preserve alpha and the sprite import settings
(256 px maximum). Raw generations, renders and boards stay outside Git.
See the [visual contract](../../../../../../DESIGN.md).
