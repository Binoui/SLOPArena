# Key art: images to replace and their generation prompts

**Status:** installed 2026-10-18 from images the owner generated with these prompts ("this came out really well"): the game art, the Steam store and library set (uploaded by the owner) and the website. See [Installed set](#installed-set-2026-10-18).  
**Inputs:** [Visual language](visual-language.md), the live models rendered on 2026-10-18, and the owner identity lines in [FightGuy](../characters/fightguy.md), [Manki](../characters/manki.md), [Wibou](../characters/wibou.md) and [Sir Bonk](../characters/bonk.md).

## Installed set (2026-10-18)

[`tools/key_art/set-2026-10-18.json`](../../tools/key_art/set-2026-10-18.json) names the owner's
originals (local, `~/Downloads/sloparena/newart/`) and every image derived from them, with its
crop, size and name mark; `python3 tools/key_art/key_art.py build <set>` rewrites them all
([README](../../tools/key_art/README.md)).

**Name mark.** The game's name is set as **SLOPArena** on one line in Archivo Black: Ink lettering
over an off-register Orange print on the art's yellow plates, or on the website's torn yellow plate
for the logo. Never stack it as SLOP / ARENA or set it in capitals as SLOPARENA: the capitals spell
the shorthand SLOPA. On large marks (the Steam logo and capsules, the Home poster, the billboard and the website nameplate) the bomb from the game icon stands in for the O, its fuse and spark rising above the line; the owner picked this on 2026-10-18. Live UI text such as the game's top bar and the website header keeps the plain O. The tool typesets the mark; generated images carry no lettering.

| Target | Source | How |
| --- | --- | --- |
| `Assets/UI/SplashArt/<Name>.png` | `<name>_splash.png` (P1-P4; Sir Bonk: `sir_bonk_splash.png`), 1086x1448 | as generated |
| `Resources/UI/Portraits/<Name>.png` | the splashes | 512x512 crops, face centred for the tall fighter-select card, which shows only the middle ~60% of the square |
| `Assets/UI/MainMenuBackground.png` | `sir_bonk_wide.png` (P5 with Sir Bonk edited in), 1672x941 | name mark and a `GET IN THE SLOP.` strip on the plate, 1438x810 |
| `Assets/Stages/after_hours/Presentation/Neon/AfterHoursFightNight.png` | `sir_bonk_tall.png` (P6 with Sir Bonk edited in), 1024x1536 | name mark on the plate, lower square, 1254x1254 |
| `Assets/UI/Arcade/SlopballPoster.png` | `sir_bonk_slopball.png` (P8 with Sir Bonk edited in), 1983x793 | as generated |
| `Assets/UI/Arcade/CoursesPoster.png` | `course.png` (P9), 1983x793 | as generated; replaces the card's flat green field and its ONE MAP PER FIGHTER label |
| `Assets/UI/FrontendPaperBackground.png` | `Warm newsprint with torn edge scraps.png` (P10), 1672x941 | as generated |
| Steam store and library (`steam` outputs) | P5, P6, P7 (`... wide newsprint showdown-2.png`, retouched as `hero_steam_clean.png`, then Sir Bonk edited in without crowns as `sir_bonk_hero_clean.png`), P10 | capsules with the name mark; text-free hero upscaled to 3840x1240; transparent 1280x312 logo; README and previews beside them |
| Steam community icon and client `.ico` | `Assets/UI/SlopArenaIcon.png` | the bomb on Action Yellow; replaces the X-eyed crowned smiley |
| SlopArena-web portraits, nameplate and trailer poster | the portraits; the logo; P5 without the mark | the shadowless logo is the hero nameplate and the social card's; the trailer's play button sits on the empty plate |

Steam rejected the first library hero upload ("appears to contain text or a logo") although nothing was lettered on it. [`tools/key_art/clean_hero.py`](../../tools/key_art/clean_hero.py) removes the two likeliest marks: a letter-like glyph the generator drew on Wibou's pendant and the crown emblem on Bonk's loincloth. The set builds the hero from that retouched original. The top bar's wordmark reads `SLOPArena` too. The two Arcade cards and the wallpaper keep their
5:2 and 16:9 proportions (`nPOTScale: None`). The Home poster and wallpaper were local-only before;
they are versioned now, and the replaced versions are kept in the ignored
`artifacts/key-art-import-20261018/`. The P5 variant `-2` and the other hero candidate
(`... split paper flyer-1.png`) are unused. Store screenshots 01, 02, 07 and 08 were re-shot with the
new portraits and billboard; see [Store screenshots](../steam/store-screenshots.md).

## Why the current art has to go

- Every character image predates the model swaps. FightGuy still wears the old red face mask and topknot; v2 has a bare face, a cocky grin, a nose band-aid and 肉 on his gi. Manki still has the open tongue-out grin; v3 smiles with his mouth closed. Wibou wears a long coat he no longer has. Bonk is drawn from the generated concept rather than the model. **2026-10-28:** the Sir Bonk splash (P4, a new generation) and portrait are redone on the live v2 model. In P5-P8 old Bonk was swapped for Sir Bonk with Codex image edits of the approved originals (the other fighters and the logo plates kept); the pre-swap originals stay in `~/Downloads/sloparena/newart/` under their old names.
- The look is the generic dark grunge splash: black-and-orange splatter, stadium lights, crowd silhouettes and glossy painterly rendering. The visual language rejects exactly that ("AI slop gloss", generic graffiti, grunge without hierarchy), while the menus, website and fighter VFX have moved to Paper/Ink, Archivo and flat comic stickers.
- The Steam store and library art use a brushy crowned `SLOPARENA` wordmark and an X-eyed smiley icon. The shared name mark is Archivo Black (in-game top bar, website), and the app icon is the photocopied bomb.

## What we need

| Image | Where it is used | Size | Made by |
| --- | --- | --- | --- |
| FightGuy, Manki, Wibou and Sir Bonk splashes | `Assets/UI/SplashArt/<Name>.png`; source of every portrait; reused in marketing | 3:4, at least 1086x1448 | P1-P4 |
| Portraits (4) | `Resources/UI/Portraits/<Name>.png`: fighter select, HUD fight cards, stage select, results, Solo end card, Arcade fighter picker; website fighter cards | 512x512 | crop of P1-P4 |
| Home poster | `Assets/UI/MainMenuBackground.png`, centre of the Home screen | 1438x810 | P5 + type |
| Steam main, header and small capsules; library header | store page and library | 1232x706, 920x430, 462x174, 920x430 | crops of P5 + logo |
| Steam vertical capsule and library capsule | store page and library | 748x896, 600x900 | P6 + logo |
| Steam library hero | library page, logo overlaid by Steam | 3840x1240, no text | P7 |
| Steam library logo | library page | 1280 wide, transparent | typeset, not generated |
| Slopball card | `Assets/UI/Arcade/SlopballPoster.png`, Arcade mode card | 1600x640 (5:2) | P8 |
| Slop Rally card | `Assets/UI/Arcade/CoursesPoster.png`, Arcade mode card | 1600x640 (5:2) | P9 |
| Paper wallpaper (optional) | `Assets/UI/FrontendPaperBackground.png`; Steam page background | 1672x941; 1438x810 | P10 |
| After Hours billboard | `Assets/Stages/after_hours/Presentation/Neon/AfterHoursFightNight.png`, in-world poster | 1254x1254 | square crop of P5 + name mark |
| Steam community and client icons | Steam | 184x184; .ico | the existing bomb icon |
| Website social preview | SlopArena-web `public/social-preview.png` | 1200x630 | rebuilt from the new portraits |

Not generated: stage thumbnails (`Resources/UI/Stages/`, rendered from the real prefabs by the stage tooling), the HUD move icons (the locked glyph set, generated from renders of the live models by `tools/hud_icons/`), and the Steam screenshots (real captures; see [Store screenshots](../steam/store-screenshots.md)).

Order: P1-P4 first (portraits are the most visible art in the game), then P5 (Home poster and most capsules), P8-P9 (Arcade cards), P6-P7 (remaining Steam art), and P10 only if the wallpaper should match.

## How to generate

1. **Attach the model sheets.** Each fighter in a prompt needs its sheet from [`references/key-art/`](references/key-art/): front, 3/4, back, face and props, rendered from the live models. The text describes the model, but the sheet is what keeps it on-model.
2. **Make the style anchor first.** Run P1 (FightGuy) with the [visual reference study](references/sloparena-visual-identity/visual-reference-study.png) (`visual-reference-study.png`) attached for palette and print construction. Iterate until the owner approves one image; that image is the style anchor.
3. **Attach the anchor to everything else.** Every later prompt expects it as an extra image and asks the generator to match its paper, print texture, cutout edge, shadow, lighting and rendering.
4. **Generate without text.** Add the Archivo Black name mark, taglines and labels afterwards. The brand mark stays the real font, and base Steam capsules can carry only the game name. The only allowed in-image words are props from the game: `#1`, `BONK!`, the question mark and Wibou's glyphs.
5. **One conversation per series.** In ChatGPT or Gemini, keep the four splashes in one chat so the look carries over; if it drifts, start a new chat and re-attach the anchor.
6. **Midjourney:** shorten the style block, pass the model sheet as `--oref` and the anchor as `--sref`, and add `--ar 3:4 --no text, letters, watermark`.
7. **Group art fallback.** If a generator cannot keep four fighters on-model at once, cut the fighters out of the approved splashes and paste them onto a generated flyer. That is the cutout look anyway.

Every prompt below is complete: paste it as is, with the listed images attached in the listed order.

## Shared blocks

Every prompt below repeats these blocks word for word. When the direction changes, edit the block here and in every prompt that contains it.

### House style

```text
STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.
```

### Fighters

Colours are sampled from the lit renders; sides are the character's own (Sir Bonk's colander is on his right elbow, Manki winks his left eye, Wibou's kunai holster is on his left thigh).

```text
FIGHTGUY
The underground champion: a classic shoto martial artist, genuinely skilled, very proud of it, a bit dim, never mean. Muscular and broad-shouldered with tan skin; a faceted low-poly model with visible triangle facets. Square heroic jaw, narrow cocky eyes and a wide toothy grin; a small white band-aid across the bridge of his nose; a red headband worn low across his brow with long tails; big spiky black hair falling to his upper back. Royal-blue (#2C53AE) sleeveless gi top with ragged armholes and white crossed lapels, a large white 肉 kanji on the back, a red belt knotted at the front with long hanging ends, baggy white gi trousers gathered below the knee, bare feet, red cloth wraps on both hands and wrists. No face mask, no topknot.
```

```text
MANKI
The mad bomber monkey: chaotic, fights with gadgets and explosions, everybody's favourite. Small and wiry (about three quarters of FightGuy's height), with a big head, large round cream ears, spiky orange (#E06E30) fur and three black horn-like spikes on top of his head. Cream face with a red bandit mask painted around the eyes: his right eye is a huge white googly eye with a tiny black pupil, his left eye is squeezed shut in a wink under thick black angry eyebrows, and a long, sly, closed-mouth grin runs cheek to cheek (no teeth, no tongue). Open ragged charcoal vest over a bare cream chest, brown belt with a small gold buckle, ragged charcoal shorts, black diamond-shaped patches on his orange shins, oversized charcoal hands and monkey feet, a long orange tail with a black tip. Smooth stylized 3D with spiky fur tufts. Gadgets: a chunky yellow-and-red striped bazooka with blue-grey metal bands and a rocket tip; a round black bomb with a short brown fuse and a white swirl mark; a yellow aerosol can with a red banner label.
```

```text
WIBOU
The ironic mall ninja: a cringe Neo from The Matrix with a katana, who takes himself completely seriously. A lean man slightly taller than FightGuy. Charcoal low-poly fedora with a band, rectangular sunglasses with smoky brown-to-clear lenses, messy shoulder-length brown hair, an angular jaw and a deadpan, far-too-serious face. Fitted black crew-neck T-shirt, a small glossy blue teardrop pendant on a black cord, a silver studded belt with a square buckle, black fingerless gloves with dark-red wrist bands, loose black cargo trousers with a kunai holster strapped to his left thigh, chunky grey-and-white sneakers. Weapons: a plain katana with a black wrapped hilt, a round guard and a straight steel blade; steel kunai with black-wrapped ring handles. Signature effect: hacker-green (#91FF69) dot-matrix katakana glyphs and square green pixels. No coat, no cape, no fox ears or tails.
```

```text
SIR BONK
The self-made knight: he knighted himself, built his armour out of household junk and believes every word of it. Goofy, good-natured and brave, not a hulking brute: broad shoulders and strong tan arms, but an ordinary build, about 1.5 times FightGuy's height. His head is completely enclosed in a chipped teal-blue (#3E7C95) bucket helm: a flat top with a stiff fan of straw broom bristles standing up as a crest, a narrow horizontal eye slit with two small round cream eyes peering out, a vertical seam down the front, and the bucket's thin wire handle looped under his chin as a chin strap. Two steel pot lids worn as pauldrons, each with its little handle on top and a cream crown painted on teal. On his chest a plate of wooden barrel staves held with rivets and rope knots, a cream crown painted across it. A teal tabard with a big drippy cream crown, cream side panels with ragged hems, a mustard-yellow (#D4A238) sash knotted at his left hip with long hanging ends, a short cream cape down his back. A steel kitchen colander strapped to his right elbow, cream cloth wraps and leather bands on both forearms, bare tan fists. Dark charcoal trousers, round tin knee plates on leather straps, big tan cuffed leather boots with chunky soles. Weapon: a plank greatsword made of one wooden board, its tip cut to a chipped point, two red paint stripes near the tip, two iron bands where a short wooden plank crossguard is nailed on, a red cloth-wrapped grip and a round wooden knob for a pommel. Chunky hand-painted stylized 3D. Signature effects: a red comic balloon burst, flat cartoon dust puffs, cracked ground, and a small red question mark over his head when he is confused.
```

## Prompts

### P1. FightGuy splash

- **Deliver:** 3:4, at least 1086x1448 (1536x2048 preferred)
- **Used for:** Assets/UI/SplashArt/FightGuy.png, source of the FightGuy portrait
- **Attach, in order:** `fightguy-model-ref.jpg`; `visual-reference-study.png` (first run only, until an image is approved)

```text
IMAGE
Character splash art of FightGuy, portrait 3:4 (1536x2048 if the tool offers it; otherwise 2:3, cropped to 3:4 afterwards). One fighter, knees-up or full body; the feet may leave the frame.

REFERENCES
Image 1 is FightGuy's model sheet: match him exactly. Image 2 is the project's visual reference board: take only its palette, paper and print construction, not its lettering, bomb drawing or layout.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

FIGHTGUY
The underground champion: a classic shoto martial artist, genuinely skilled, very proud of it, a bit dim, never mean. Muscular and broad-shouldered with tan skin; a faceted low-poly model with visible triangle facets. Square heroic jaw, narrow cocky eyes and a wide toothy grin; a small white band-aid across the bridge of his nose; a red headband worn low across his brow with long tails; big spiky black hair falling to his upper back. Royal-blue (#2C53AE) sleeveless gi top with ragged armholes and white crossed lapels, a large white 肉 kanji on the back, a red belt knotted at the front with long hanging ends, baggy white gi trousers gathered below the knee, bare feet, red cloth wraps on both hands and wrists. No face mask, no topknot.

POSE
A low, wide shoto stance turned three-quarters to the viewer, both palms thrust forward at waist height around a charged ki ball: a flat comic sphere, bright blue with a white core and a thick ink outline. He turns his face to the camera with a smug, toothy grin, posing for his own poster instead of watching his opponent. Headband tails and long hair whip back with the motion.

BACKGROUND
Warm newsprint paper. Behind him, one big orange (#F05B35) sunburst of hard-edged rays; its flat centre sits behind his head and the rays break into halftone toward the edges.

JOKE
A die-cut sticker of a yellow foam "#1" finger is slapped on the flyer beside his shoulder: he brought his own fan merchandise.

CROP RULES
The head is centred horizontally, its centre about 30% down from the top. Nothing crosses the face. The area directly behind the head is a calm, flat field: no halftone, tape or effects. A square crop of head and shoulders must still read as a portrait at 64 px, and the bottom 15% of that square may later be covered by a name strip.

TEXT ALLOWED
Only "#1" on the foam finger.
```

### P2. Manki splash

- **Deliver:** 3:4, at least 1086x1448
- **Used for:** Assets/UI/SplashArt/Manki.png, source of the Manki portrait
- **Attach, in order:** `manki-model-ref.jpg`; style anchor (the approved P1)

```text
IMAGE
Character splash art of Manki, portrait 3:4 (1536x2048 if the tool offers it; otherwise 2:3, cropped to 3:4 afterwards). One fighter, knees-up or full body; the feet may leave the frame.

REFERENCES
Image 1 is Manki's model sheet: match him exactly, including the closed-mouth grin and the wink. Image 2 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

MANKI
The mad bomber monkey: chaotic, fights with gadgets and explosions, everybody's favourite. Small and wiry (about three quarters of FightGuy's height), with a big head, large round cream ears, spiky orange (#E06E30) fur and three black horn-like spikes on top of his head. Cream face with a red bandit mask painted around the eyes: his right eye is a huge white googly eye with a tiny black pupil, his left eye is squeezed shut in a wink under thick black angry eyebrows, and a long, sly, closed-mouth grin runs cheek to cheek (no teeth, no tongue). Open ragged charcoal vest over a bare cream chest, brown belt with a small gold buckle, ragged charcoal shorts, black diamond-shaped patches on his orange shins, oversized charcoal hands and monkey feet, a long orange tail with a black tip. Smooth stylized 3D with spiky fur tufts. Gadgets: a chunky yellow-and-red striped bazooka with blue-grey metal bands and a rocket tip; a round black bomb with a short brown fuse and a white swirl mark; a yellow aerosol can with a red banner label.

POSE
Mid-leap toward the viewer, body twisted: the striped bazooka braced on one shoulder and aimed past the camera, the other hand tossing the round black bomb up beside (never in front of) his head, its short fuse fizzing with a flat cartoon spark. Tail curled in a loose spiral, oversized hands and feet spread wide. Googly eye wide open, other eye winking, long sly closed-mouth grin.

BACKGROUND
Warm newsprint paper. Behind him, one big flat ink-black cartoon explosion silhouette, rimmed with a few flat comic fire puffs in orange and yellow and orange halftone; its flat black centre sits behind his head. Small cartoon junk tumbles around him as flat stickers with ink outlines: a bolt, a hex nut, a spring and a gear.

JOKE
The fuse has almost burned down right next to his ear and he could not care less. One smudge of soot on his cheek.

CROP RULES
The head is centred horizontally, its centre about 30% down from the top. Nothing crosses the face. The area directly behind the head is a calm, flat field: no halftone, tape or effects. A square crop of head and shoulders must still read as a portrait at 64 px, and the bottom 15% of that square may later be covered by a name strip.

TEXT ALLOWED
None.
```

### P3. Wibou splash

- **Deliver:** 3:4, at least 1086x1448
- **Used for:** Assets/UI/SplashArt/Wibou.png, source of the Wibou portrait
- **Attach, in order:** `wibou-model-ref.jpg`; style anchor (the approved P1)

```text
IMAGE
Character splash art of Wibou, portrait 3:4 (1536x2048 if the tool offers it; otherwise 2:3, cropped to 3:4 afterwards). One fighter, knees-up or full body; the feet may leave the frame.

REFERENCES
Image 1 is Wibou's model sheet: match him exactly (T-shirt, no coat). Image 2 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

WIBOU
The ironic mall ninja: a cringe Neo from The Matrix with a katana, who takes himself completely seriously. A lean man slightly taller than FightGuy. Charcoal low-poly fedora with a band, rectangular sunglasses with smoky brown-to-clear lenses, messy shoulder-length brown hair, an angular jaw and a deadpan, far-too-serious face. Fitted black crew-neck T-shirt, a small glossy blue teardrop pendant on a black cord, a silver studded belt with a square buckle, black fingerless gloves with dark-red wrist bands, loose black cargo trousers with a kunai holster strapped to his left thigh, chunky grey-and-white sneakers. Weapons: a plain katana with a black wrapped hilt, a round guard and a straight steel blade; steel kunai with black-wrapped ring handles. Signature effect: hacker-green (#91FF69) dot-matrix katakana glyphs and square green pixels. No coat, no cape, no fox ears or tails.

POSE
A low, wide iai stance, three-quarters to the viewer: the katana drawn and held out behind him in one long horizontal line, blade level; three kunai fanned between the fingers of his other hand at chest height, held to the side and clear of his face. Fedora brim low, chin down, the sunglasses catching a tiny green reflection of code. Deadpan and impossibly serious: play the cringe completely straight, because the outfit is the joke.

BACKGROUND
Warm newsprint paper. Behind him, one tall hacker-green (#91FF69) panel printed with columns of black dot-matrix katakana glyphs, like falling code run through a cheap photocopier; the panel is plain green directly behind his head. One crisp, flat green crescent slash with an ink edge sweeps across the lower half, and a few square green pixels drift off the panel's edge.

CROP RULES
The head is centred horizontally, its centre about 30% down from the top. Nothing crosses the face. The area directly behind the head is a calm, flat field: no halftone, tape or effects. A square crop of head and shoulders must still read as a portrait at 64 px, and the bottom 15% of that square may later be covered by a name strip.

TEXT ALLOWED
Only the decorative dot-matrix katakana glyphs; no Latin letters or numbers.
```

### P4. Sir Bonk splash

- **Deliver:** 3:4, at least 1086x1448
- **Used for:** Assets/UI/SplashArt/Bonk.png, source of the Sir Bonk portrait
- **Attach, in order:** `bonk-model-ref.jpg`; style anchor (the approved P1); in-game pose renders of the live model (optional)
- **Approved 2026-10-28:** take B of three (`~/Downloads/sloparena/newart/sir_bonk_splash.png`), generated with Codex imagegen. Known flaw: the colander is drawn on his left arm (the model has it on the right elbow).

```text
IMAGE
Character splash art of Sir Bonk, portrait 3:4 (1536x2048 if the tool offers it; otherwise 2:3, cropped to 3:4 afterwards). One fighter, knees-up or full body; the feet may leave the frame.

REFERENCES
Image 1 is Sir Bonk's model sheet: match him exactly, including the bucket helm, the straw crest, the pot-lid pauldrons and the barrel-stave chest plate. Image 2 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly. Image 3 shows the same model posed in the game (he is the right figure of each pair): use it for proportions and for how he holds the plank.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

SIR BONK
The self-made knight: he knighted himself, built his armour out of household junk and believes every word of it. Goofy, good-natured and brave, not a hulking brute: broad shoulders and strong tan arms, but an ordinary build, about 1.5 times FightGuy's height. His head is completely enclosed in a chipped teal-blue (#3E7C95) bucket helm: a flat top with a stiff fan of straw broom bristles standing up as a crest, a narrow horizontal eye slit with two small round cream eyes peering out, a vertical seam down the front, and the bucket's thin wire handle looped under his chin as a chin strap. Two steel pot lids worn as pauldrons, each with its little handle on top and a cream crown painted on teal. On his chest a plate of wooden barrel staves held with rivets and rope knots, a cream crown painted across it. A teal tabard with a big drippy cream crown, cream side panels with ragged hems, a mustard-yellow (#D4A238) sash knotted at his left hip with long hanging ends, a short cream cape down his back. A steel kitchen colander strapped to his right elbow, cream cloth wraps and leather bands on both forearms, bare tan fists. Dark charcoal trousers, round tin knee plates on leather straps, big tan cuffed leather boots with chunky soles. Weapon: a plank greatsword made of one wooden board, its tip cut to a chipped point, two red paint stripes near the tip, two iron bands where a short wooden plank crossguard is nailed on, a red cloth-wrapped grip and a round wooden knob for a pommel. Chunky hand-painted stylized 3D. Signature effects: a red comic balloon burst, flat cartoon dust puffs, cracked ground, and a small red question mark over his head when he is confused.

POSE
Three-quarter view from slightly below. Sir Bonk stands tall and solemn, chest out, the plank greatsword held upright in his right fist in front of his right shoulder, its flat side resting on his own pot-lid pauldron as if he is dubbing himself, his left fist planted on his hip. His helmet tilts up a little, chin lifted, enormously proud.

BACKGROUND
Warm newsprint paper. Behind him, a big flat Action Yellow (#FFCC22) rosette shape with a thick ink outline, like a prize ribbon from a county fair, its two tails hanging down behind his legs; a red (#E2463A) comic balloon burst peeks from behind the rosette at the lower left with the word "BONK!" in chunky cream comic letters, a red outline and an ink drop shadow.

JOKE
The cream crown on his tabard is clearly hand-painted and still dripping, and one fresh drip has run onto his boot.

CROP RULES
The helmet and its straw crest are centred horizontally, the helmet about 28% down from the top, against calm paper or a calm flat shape, not against the red burst. Nothing crosses the helmet. The square portrait crop around the helmet shows the helmet, the crest, both pot-lid pauldrons and the top of the barrel-stave chest plate.

TEXT ALLOWED
Only "BONK!".
```

### P5. Key art, wide (all four)

- **Deliver:** 16:9, at least 1920x1080
- **Used for:** Home poster Assets/UI/MainMenuBackground.png (1438x810); Steam main capsule, header capsules, small capsule; After Hours billboard; website hero
- **Attach, in order:** `fightguy-model-ref.jpg`; `manki-model-ref.jpg`; `wibou-model-ref.jpg`; `bonk-model-ref.jpg`; style anchor (the approved P1); approved splashes (optional, after the anchor)

```text
IMAGE
Wide key art with all four fighters, 16:9, at least 1920x1080. It is a master: it gets cropped into the Home menu poster and several Steam capsules.

REFERENCES
Images 1-4 are the model sheets for FightGuy, Manki, Wibou and Sir Bonk: match each fighter exactly. Image 5 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly. Any further images are approved splashes of the same fighters, for faces and attitude only.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

FIGHTGUY
The underground champion: a classic shoto martial artist, genuinely skilled, very proud of it, a bit dim, never mean. Muscular and broad-shouldered with tan skin; a faceted low-poly model with visible triangle facets. Square heroic jaw, narrow cocky eyes and a wide toothy grin; a small white band-aid across the bridge of his nose; a red headband worn low across his brow with long tails; big spiky black hair falling to his upper back. Royal-blue (#2C53AE) sleeveless gi top with ragged armholes and white crossed lapels, a large white 肉 kanji on the back, a red belt knotted at the front with long hanging ends, baggy white gi trousers gathered below the knee, bare feet, red cloth wraps on both hands and wrists. No face mask, no topknot.

MANKI
The mad bomber monkey: chaotic, fights with gadgets and explosions, everybody's favourite. Small and wiry (about three quarters of FightGuy's height), with a big head, large round cream ears, spiky orange (#E06E30) fur and three black horn-like spikes on top of his head. Cream face with a red bandit mask painted around the eyes: his right eye is a huge white googly eye with a tiny black pupil, his left eye is squeezed shut in a wink under thick black angry eyebrows, and a long, sly, closed-mouth grin runs cheek to cheek (no teeth, no tongue). Open ragged charcoal vest over a bare cream chest, brown belt with a small gold buckle, ragged charcoal shorts, black diamond-shaped patches on his orange shins, oversized charcoal hands and monkey feet, a long orange tail with a black tip. Smooth stylized 3D with spiky fur tufts. Gadgets: a chunky yellow-and-red striped bazooka with blue-grey metal bands and a rocket tip; a round black bomb with a short brown fuse and a white swirl mark; a yellow aerosol can with a red banner label.

WIBOU
The ironic mall ninja: a cringe Neo from The Matrix with a katana, who takes himself completely seriously. A lean man slightly taller than FightGuy. Charcoal low-poly fedora with a band, rectangular sunglasses with smoky brown-to-clear lenses, messy shoulder-length brown hair, an angular jaw and a deadpan, far-too-serious face. Fitted black crew-neck T-shirt, a small glossy blue teardrop pendant on a black cord, a silver studded belt with a square buckle, black fingerless gloves with dark-red wrist bands, loose black cargo trousers with a kunai holster strapped to his left thigh, chunky grey-and-white sneakers. Weapons: a plain katana with a black wrapped hilt, a round guard and a straight steel blade; steel kunai with black-wrapped ring handles. Signature effect: hacker-green (#91FF69) dot-matrix katakana glyphs and square green pixels. No coat, no cape, no fox ears or tails.

SIR BONK
The self-made knight: he knighted himself, built his armour out of household junk and believes every word of it. Goofy, good-natured and brave, not a hulking brute: broad shoulders and strong tan arms, but an ordinary build, about 1.5 times FightGuy's height. His head is completely enclosed in a chipped teal-blue (#3E7C95) bucket helm: a flat top with a stiff fan of straw broom bristles standing up as a crest, a narrow horizontal eye slit with two small round cream eyes peering out, a vertical seam down the front, and the bucket's thin wire handle looped under his chin as a chin strap. Two steel pot lids worn as pauldrons, each with its little handle on top and a cream crown painted on teal. On his chest a plate of wooden barrel staves held with rivets and rope knots, a cream crown painted across it. A teal tabard with a big drippy cream crown, cream side panels with ragged hems, a mustard-yellow (#D4A238) sash knotted at his left hip with long hanging ends, a short cream cape down his back. A steel kitchen colander strapped to his right elbow, cream cloth wraps and leather bands on both forearms, bare tan fists. Dark charcoal trousers, round tin knee plates on leather straps, big tan cuffed leather boots with chunky soles. Weapon: a plank greatsword made of one wooden board, its tip cut to a chipped point, two red paint stripes near the tip, two iron bands where a short wooden plank crossguard is nailed on, a red cloth-wrapped grip and a round wooden knob for a pommel. Chunky hand-painted stylized 3D. Signature effects: a red comic balloon burst, flat cartoon dust puffs, cracked ground, and a small red question mark over his head when he is confused.

SCALE
Keep the in-game size relationships: Sir Bonk is the tallest, about 1.5 times FightGuy's height; Wibou is slightly taller than FightGuy; Manki is the smallest, about three quarters of FightGuy.

LAYOUT
A fight-night flyer. In the centre, one big torn Action Yellow (#FFCC22) paper plate, about 40% of the width and 28% of the height, rotated about -2 degrees and left completely empty: a logo is pasted there later. Four cutouts overlap its edges like a tournament poster:
- FightGuy, large, on the left, firing his blue ki ball toward the plate; his face in the upper-left third.
- Sir Bonk, the tallest, on the right, plank greatsword raised toward the top-right corner; his helmet about 20% down from the top.
- Manki leaping across the top, just above the plate, a lit bomb in hand.
- Wibou crouched low in front of the bottom-right edge of the plate, katana drawn in one long horizontal line.
Each signature effect stays small and flat: the ki ball, the bomb spark, one green crescent slash, dust puffs at Sir Bonk's boots.

SAFE AREAS
Every face sits inside the middle 80% of the height (a 920x430 crop removes the top and bottom tenth). No face in the bottom-left 35% x 35%: the chat overlay covers it on the Home screen. The plate stays empty and clearly readable.

JOKE
A small red question mark floats over Sir Bonk's straw crest.

TEXT ALLOWED
Only the question mark.
```

### P6. Key art, tall (all four)

- **Deliver:** 2:3, at least 1024x1536
- **Used for:** Steam library capsule (600x900) and vertical capsule (748x896)
- **Attach, in order:** `fightguy-model-ref.jpg`; `manki-model-ref.jpg`; `wibou-model-ref.jpg`; `bonk-model-ref.jpg`; style anchor (the approved P1); approved P5 (optional)

```text
IMAGE
Tall key art with all four fighters, 2:3, at least 1024x1536. Master for two Steam capsules; one of them crops about 10% off the top and bottom.

REFERENCES
Images 1-4 are the model sheets for FightGuy, Manki, Wibou and Sir Bonk: match each fighter exactly. Image 5 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly. If a wide key art image is attached, match its flyer, plate and treatment.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

FIGHTGUY
The underground champion: a classic shoto martial artist, genuinely skilled, very proud of it, a bit dim, never mean. Muscular and broad-shouldered with tan skin; a faceted low-poly model with visible triangle facets. Square heroic jaw, narrow cocky eyes and a wide toothy grin; a small white band-aid across the bridge of his nose; a red headband worn low across his brow with long tails; big spiky black hair falling to his upper back. Royal-blue (#2C53AE) sleeveless gi top with ragged armholes and white crossed lapels, a large white 肉 kanji on the back, a red belt knotted at the front with long hanging ends, baggy white gi trousers gathered below the knee, bare feet, red cloth wraps on both hands and wrists. No face mask, no topknot.

MANKI
The mad bomber monkey: chaotic, fights with gadgets and explosions, everybody's favourite. Small and wiry (about three quarters of FightGuy's height), with a big head, large round cream ears, spiky orange (#E06E30) fur and three black horn-like spikes on top of his head. Cream face with a red bandit mask painted around the eyes: his right eye is a huge white googly eye with a tiny black pupil, his left eye is squeezed shut in a wink under thick black angry eyebrows, and a long, sly, closed-mouth grin runs cheek to cheek (no teeth, no tongue). Open ragged charcoal vest over a bare cream chest, brown belt with a small gold buckle, ragged charcoal shorts, black diamond-shaped patches on his orange shins, oversized charcoal hands and monkey feet, a long orange tail with a black tip. Smooth stylized 3D with spiky fur tufts. Gadgets: a chunky yellow-and-red striped bazooka with blue-grey metal bands and a rocket tip; a round black bomb with a short brown fuse and a white swirl mark; a yellow aerosol can with a red banner label.

WIBOU
The ironic mall ninja: a cringe Neo from The Matrix with a katana, who takes himself completely seriously. A lean man slightly taller than FightGuy. Charcoal low-poly fedora with a band, rectangular sunglasses with smoky brown-to-clear lenses, messy shoulder-length brown hair, an angular jaw and a deadpan, far-too-serious face. Fitted black crew-neck T-shirt, a small glossy blue teardrop pendant on a black cord, a silver studded belt with a square buckle, black fingerless gloves with dark-red wrist bands, loose black cargo trousers with a kunai holster strapped to his left thigh, chunky grey-and-white sneakers. Weapons: a plain katana with a black wrapped hilt, a round guard and a straight steel blade; steel kunai with black-wrapped ring handles. Signature effect: hacker-green (#91FF69) dot-matrix katakana glyphs and square green pixels. No coat, no cape, no fox ears or tails.

SIR BONK
The self-made knight: he knighted himself, built his armour out of household junk and believes every word of it. Goofy, good-natured and brave, not a hulking brute: broad shoulders and strong tan arms, but an ordinary build, about 1.5 times FightGuy's height. His head is completely enclosed in a chipped teal-blue (#3E7C95) bucket helm: a flat top with a stiff fan of straw broom bristles standing up as a crest, a narrow horizontal eye slit with two small round cream eyes peering out, a vertical seam down the front, and the bucket's thin wire handle looped under his chin as a chin strap. Two steel pot lids worn as pauldrons, each with its little handle on top and a cream crown painted on teal. On his chest a plate of wooden barrel staves held with rivets and rope knots, a cream crown painted across it. A teal tabard with a big drippy cream crown, cream side panels with ragged hems, a mustard-yellow (#D4A238) sash knotted at his left hip with long hanging ends, a short cream cape down his back. A steel kitchen colander strapped to his right elbow, cream cloth wraps and leather bands on both forearms, bare tan fists. Dark charcoal trousers, round tin knee plates on leather straps, big tan cuffed leather boots with chunky soles. Weapon: a plank greatsword made of one wooden board, its tip cut to a chipped point, two red paint stripes near the tip, two iron bands where a short wooden plank crossguard is nailed on, a red cloth-wrapped grip and a round wooden knob for a pommel. Chunky hand-painted stylized 3D. Signature effects: a red comic balloon burst, flat cartoon dust puffs, cracked ground, and a small red question mark over his head when he is confused.

SCALE
Keep the in-game size relationships: Sir Bonk is the tallest, about 1.5 times FightGuy's height; Wibou is slightly taller than FightGuy; Manki is the smallest, about three quarters of FightGuy.

LAYOUT
The same fight-night flyer, stacked vertically. Across the middle, one empty torn Action Yellow (#FFCC22) plate, about 80% of the width and 18% of the height, rotated about -2 degrees: a logo is pasted there later. Above it, Sir Bonk tall at the upper right with the plank greatsword over his shoulder, and Manki leaping in at the upper left with the bazooka. Below it, FightGuy charging his blue ki ball at the lower left and Wibou crouched with his katana at the lower right. The cutouts overlap the plate's edges but never cover its centre.

SAFE AREAS
All faces between 12% and 88% of the height. The plate stays empty and clearly readable.

TEXT ALLOWED
None.
```

### P7. Steam library hero

- **Deliver:** 3840x1240 (31:10), no text
- **Used for:** Steam library hero (Steam overlays the logo separately)
- **Attach, in order:** `fightguy-model-ref.jpg`; `manki-model-ref.jpg`; `wibou-model-ref.jpg`; `bonk-model-ref.jpg`; style anchor (the approved P1); approved P5 (optional)

```text
IMAGE
A very wide banner, 31:10 (final 3840x1240). If the tool cannot make it, generate 21:9 and extend both sides by outpainting.

REFERENCES
Images 1-4 are the model sheets for FightGuy, Manki, Wibou and Sir Bonk: match each fighter exactly. Image 5 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

FIGHTGUY
The underground champion: a classic shoto martial artist, genuinely skilled, very proud of it, a bit dim, never mean. Muscular and broad-shouldered with tan skin; a faceted low-poly model with visible triangle facets. Square heroic jaw, narrow cocky eyes and a wide toothy grin; a small white band-aid across the bridge of his nose; a red headband worn low across his brow with long tails; big spiky black hair falling to his upper back. Royal-blue (#2C53AE) sleeveless gi top with ragged armholes and white crossed lapels, a large white 肉 kanji on the back, a red belt knotted at the front with long hanging ends, baggy white gi trousers gathered below the knee, bare feet, red cloth wraps on both hands and wrists. No face mask, no topknot.

MANKI
The mad bomber monkey: chaotic, fights with gadgets and explosions, everybody's favourite. Small and wiry (about three quarters of FightGuy's height), with a big head, large round cream ears, spiky orange (#E06E30) fur and three black horn-like spikes on top of his head. Cream face with a red bandit mask painted around the eyes: his right eye is a huge white googly eye with a tiny black pupil, his left eye is squeezed shut in a wink under thick black angry eyebrows, and a long, sly, closed-mouth grin runs cheek to cheek (no teeth, no tongue). Open ragged charcoal vest over a bare cream chest, brown belt with a small gold buckle, ragged charcoal shorts, black diamond-shaped patches on his orange shins, oversized charcoal hands and monkey feet, a long orange tail with a black tip. Smooth stylized 3D with spiky fur tufts. Gadgets: a chunky yellow-and-red striped bazooka with blue-grey metal bands and a rocket tip; a round black bomb with a short brown fuse and a white swirl mark; a yellow aerosol can with a red banner label.

WIBOU
The ironic mall ninja: a cringe Neo from The Matrix with a katana, who takes himself completely seriously. A lean man slightly taller than FightGuy. Charcoal low-poly fedora with a band, rectangular sunglasses with smoky brown-to-clear lenses, messy shoulder-length brown hair, an angular jaw and a deadpan, far-too-serious face. Fitted black crew-neck T-shirt, a small glossy blue teardrop pendant on a black cord, a silver studded belt with a square buckle, black fingerless gloves with dark-red wrist bands, loose black cargo trousers with a kunai holster strapped to his left thigh, chunky grey-and-white sneakers. Weapons: a plain katana with a black wrapped hilt, a round guard and a straight steel blade; steel kunai with black-wrapped ring handles. Signature effect: hacker-green (#91FF69) dot-matrix katakana glyphs and square green pixels. No coat, no cape, no fox ears or tails.

SIR BONK
The self-made knight: he knighted himself, built his armour out of household junk and believes every word of it. Goofy, good-natured and brave, not a hulking brute: broad shoulders and strong tan arms, but an ordinary build, about 1.5 times FightGuy's height. His head is completely enclosed in a chipped teal-blue (#3E7C95) bucket helm: a flat top with a stiff fan of straw broom bristles standing up as a crest, a narrow horizontal eye slit with two small round cream eyes peering out, a vertical seam down the front, and the bucket's thin wire handle looped under his chin as a chin strap. Two steel pot lids worn as pauldrons, each with its little handle on top and a cream crown painted on teal. On his chest a plate of wooden barrel staves held with rivets and rope knots, a cream crown painted across it. A teal tabard with a big drippy cream crown, cream side panels with ragged hems, a mustard-yellow (#D4A238) sash knotted at his left hip with long hanging ends, a short cream cape down his back. A steel kitchen colander strapped to his right elbow, cream cloth wraps and leather bands on both forearms, bare tan fists. Dark charcoal trousers, round tin knee plates on leather straps, big tan cuffed leather boots with chunky soles. Weapon: a plank greatsword made of one wooden board, its tip cut to a chipped point, two red paint stripes near the tip, two iron bands where a short wooden plank crossguard is nailed on, a red cloth-wrapped grip and a round wooden knob for a pommel. Chunky hand-painted stylized 3D. Signature effects: a red comic balloon burst, flat cartoon dust puffs, cracked ground, and a small red question mark over his head when he is confused.

SCALE
Keep the in-game size relationships: Sir Bonk is the tallest, about 1.5 times FightGuy's height; Wibou is slightly taller than FightGuy; Manki is the smallest, about three quarters of FightGuy.

LAYOUT
A long fight-night flyer wall with two groups of cutouts. Left group: FightGuy throwing his blue ki ball, with Manki rocketing up behind him on a flat comic jetpack flame. Right group: Wibou in his iai stance, with Sir Bonk towering behind him, plank greatsword on his shoulder. The middle third is quiet paper with one big flat orange (#F05B35) halftone circle and nothing else: Steam places the game logo over the bottom centre.

SAFE AREAS
Faces between 15% and 72% of the height and between 11% and 89% of the width. Nothing important in the bottom 16%, which Steam darkens.

TEXT ALLOWED
None at all: Valve requires the library hero to be text-free. Keep every costume and prop free of symbols, letters and emblems too: draw Wibou's pendant as a plain glossy drop and leave Sir Bonk's tabard, chest plate and pot lids plain teal and cream, without the painted crowns. Steam's automatic check rejects a hero with anything that reads as text or a logo.
```

### P8. Arcade card: Slopball

- **Deliver:** 5:2, 1600x640
- **Used for:** Assets/UI/Arcade/SlopballPoster.png (Arcade mode card; the card frame is about 5:2)
- **Attach, in order:** `bonk-model-ref.jpg`; `scene-night-court.jpg`; style anchor (the approved P1)

```text
IMAGE
Mode-card art, 5:2 (final 1600x640). If the tool cannot make 5:2, generate 21:9 or 16:9 and keep everything important inside a centred 5:2 band.

REFERENCES
Image 1 is Sir Bonk's model sheet: match him exactly. Image 2 shows the real Night Court: keep its layout and colours but restyle it as described. Image 3 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

SIR BONK
The self-made knight: he knighted himself, built his armour out of household junk and believes every word of it. Goofy, good-natured and brave, not a hulking brute: broad shoulders and strong tan arms, but an ordinary build, about 1.5 times FightGuy's height. His head is completely enclosed in a chipped teal-blue (#3E7C95) bucket helm: a flat top with a stiff fan of straw broom bristles standing up as a crest, a narrow horizontal eye slit with two small round cream eyes peering out, a vertical seam down the front, and the bucket's thin wire handle looped under his chin as a chin strap. Two steel pot lids worn as pauldrons, each with its little handle on top and a cream crown painted on teal. On his chest a plate of wooden barrel staves held with rivets and rope knots, a cream crown painted across it. A teal tabard with a big drippy cream crown, cream side panels with ragged hems, a mustard-yellow (#D4A238) sash knotted at his left hip with long hanging ends, a short cream cape down his back. A steel kitchen colander strapped to his right elbow, cream cloth wraps and leather bands on both forearms, bare tan fists. Dark charcoal trousers, round tin knee plates on leather straps, big tan cuffed leather boots with chunky soles. Weapon: a plank greatsword made of one wooden board, its tip cut to a chipped point, two red paint stripes near the tip, two iron bands where a short wooden plank crossguard is nailed on, a red cloth-wrapped grip and a round wooden knob for a pommel. Chunky hand-painted stylized 3D. Signature effects: a red comic balloon burst, flat cartoon dust puffs, cracked ground, and a small red question mark over his head when he is confused.

SCENE
The Night Court: after closing, a floodlit streetball cage inside a dark warehouse. Chain-link fence walls on low sponsor dasher boards, two hard floodlight cones from the ceiling, a teal court with white lines, a dark-red far half beyond a low red net, and a giant winking green smiley-ball mascot mural faintly visible behind the fence. Render the court as a chunky low-poly 3D game scene, like the game itself.

LAYOUT
A torn-paper split: the left 40% is warm flyer paper with orange halftone, the right 60% is the low-poly court, and the tear runs diagonally between them. Sir Bonk's cutout bridges both, swinging the plank greatsword like a baseball bat into a big pale-yellow ball, which flies over the net toward the top right with a flat red comic "BONK!" burst at the point of impact.

JOKE
On the far court, the ball launcher, a black post with a little green screen face, looks alarmed.

TEXT ALLOWED
Only "BONK!". No signage or sponsor text: the card's title is UI.
```

### P9. Arcade card: Slop Rally

- **Deliver:** 5:2, 1600x640
- **Used for:** Assets/UI/Arcade/CoursesPoster.png, the SLOP RALLY card
- **Attach, in order:** `fightguy-model-ref.jpg`; `manki-model-ref.jpg`; `scene-sanctuary.jpg`; `scene-kaboom-quarry.jpg`; style anchor (the approved P1)

```text
IMAGE
Mode-card art, 5:2 (final 1600x640). If the tool cannot make 5:2, generate 21:9 or 16:9 and keep everything important inside a centred 5:2 band.

REFERENCES
Images 1-2 are the model sheets for FightGuy and Manki: match both exactly. Images 3-4 show the real Sanctuary Course and Kaboom Quarry: keep their landmarks and colours but restyle them as described. Image 5 is the style anchor (the approved FightGuy splash): match its paper, print texture, cutout edge, offset shadow, lighting and rendering exactly.

STYLE
An underground fight-night flyer built from scavenged low-poly 3D fighters. Render every fighter as the actual stylized 3D game model shown in its model sheet: chunky simplified shapes, matte hand-painted textures, polygon facets left visible where the model has them, and exactly the same proportions, face, costume, colours and props. Nothing is redesigned. Light the 3D render with one hard warm key light from the upper left and a soft cool fill: crisp shadows, no gloss, glow, bloom or rim-light halo.
Cut each fighter out like a paper cutout with a thin, slightly uneven white edge, and paste it onto the flyer with a solid near-black offset shadow (down and to the right, no blur) and a thin orange outline peeking out slightly off-register behind it.
The flyer is warm newsprint paper (#DED8C9) printed in near-black ink (#171814): coarse halftone dots, light photocopy grain, thick square-cornered black rules and borders, one or two strips of masking tape. Build the composition from a few big flat shapes with one dominant gesture; at most one element is rotated, by 1-3 degrees. Effects are flat comic stickers with thick ink outlines and cream highlights, never realistic particles. Orange (#F05B35) marks impact; Action Yellow (#FFCC22) appears only where this prompt asks for it.
Tone: a cool fighting-game image first, one dry joke in a detail second.
No text: no letters, numbers, logos, signatures or watermarks, except the exact words this prompt allows.
Avoid: digital painting, anime splash-art rendering, glossy AAA 3D, realistic skin, fabric or metal detail, lens flares, light rays, smoke clouds, sparks everywhere, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours, stadium lights, crowd silhouettes, black-and-orange fire backgrounds, extra fingers, changed costumes.

FIGHTGUY
The underground champion: a classic shoto martial artist, genuinely skilled, very proud of it, a bit dim, never mean. Muscular and broad-shouldered with tan skin; a faceted low-poly model with visible triangle facets. Square heroic jaw, narrow cocky eyes and a wide toothy grin; a small white band-aid across the bridge of his nose; a red headband worn low across his brow with long tails; big spiky black hair falling to his upper back. Royal-blue (#2C53AE) sleeveless gi top with ragged armholes and white crossed lapels, a large white 肉 kanji on the back, a red belt knotted at the front with long hanging ends, baggy white gi trousers gathered below the knee, bare feet, red cloth wraps on both hands and wrists. No face mask, no topknot.

MANKI
The mad bomber monkey: chaotic, fights with gadgets and explosions, everybody's favourite. Small and wiry (about three quarters of FightGuy's height), with a big head, large round cream ears, spiky orange (#E06E30) fur and three black horn-like spikes on top of his head. Cream face with a red bandit mask painted around the eyes: his right eye is a huge white googly eye with a tiny black pupil, his left eye is squeezed shut in a wink under thick black angry eyebrows, and a long, sly, closed-mouth grin runs cheek to cheek (no teeth, no tongue). Open ragged charcoal vest over a bare cream chest, brown belt with a small gold buckle, ragged charcoal shorts, black diamond-shaped patches on his orange shins, oversized charcoal hands and monkey feet, a long orange tail with a black tip. Smooth stylized 3D with spiky fur tufts. Gadgets: a chunky yellow-and-red striped bazooka with blue-grey metal bands and a rocket tip; a round black bomb with a short brown fuse and a white swirl mark; a yellow aerosol can with a red banner label.

LAYOUT
Two scenes split by a diagonal strip of torn paper held down with tape; one fighter per scene, each a cutout with the flyer treatment over a chunky low-poly 3D scene.
- Left, the Sanctuary Course: FightGuy mid flying kick, smashing a glowing yellow orb target above a stone-tiled shrine terrace on a tall rock pillar: a white shrine with maroon trim and gold roof ornaments, maroon gate posts with gold discs, strings of colourful prayer flags, snowy low-poly peaks and a sea of clouds below.
- Right, Kaboom Quarry: Manki rocketing upward on a flat comic jetpack flame past a red-and-white bullseye target board, as a stack of wooden crates marked with red crosses bursts into a flat cartoon explosion; striped sandstone quarry walls, a turquoise flooded pit, oil-drum pontoons, concrete docks with yellow-and-black hazard edges, and his own giant painted face on the quarry wall.
Keep a calm vertical band along the seam: the card may print a short label over it.

JOKE
A googly-eyed dump truck sunk to its axles in the quarry water.

TEXT ALLOWED
None.
```

### P10. Paper wallpaper (optional)

- **Deliver:** 16:9, at least 1920x1080
- **Used for:** Assets/UI/FrontendPaperBackground.png (1672x941) and the Steam page background (1438x810)
- **Attach, in order:** style anchor (the approved P1, for paper and print only)

```text
IMAGE
A menu wallpaper, 16:9, at least 1920x1080. It also becomes the Steam store page background.

REFERENCES
Image 1 is the style anchor: match its paper and print texture only. It contains a fighter; this image does not.

STYLE
Printed matter from an underground fight-night flyer: warm newsprint paper (#DED8C9) printed in near-black ink (#171814), coarse halftone dots, light photocopy grain, thick square-cornered black rules, strips of masking tape, slightly off-register orange (#F05B35). Flat, printed and physical. Action Yellow (#FFCC22) appears only where this prompt asks for it.
No text: no letters, numbers, logos, signatures or watermarks.
Avoid: digital painting, glossy 3D, lens flares, light rays, smoke, paint splatter, grunge brush strokes, graffiti, neon or cyberpunk colours.

CONTENT
No characters. A wall of warm newsprint paper. The centre 70% stays calm and nearly empty: faint grain, an old fold line, the ghost of a peeled-off flyer. Only toward the edges: torn remnants of older fight flyers, black and orange halftone dot gradients, a few strips of yellow masking tape and staples, orange registration and crop marks, one or two thick black rules cut off by the frame, a corner of a torn Action Yellow (#FFCC22) plate. Everything flat and printed. It sits behind menu buttons and must never compete with them.

TEXT ALLOWED
None.
```

## Derived images (no prompt)

- **Portraits.** Crop each approved splash to a square of head and shoulders, 512x512, into `Resources/UI/Portraits/<Name>.png`. Use the same scale for all four (eye line about 40% from the top) and compare them side by side at 64 px; the fighter-select name strip covers the bottom 15%. Centre the face horizontally: the tall fighter-select card crops the square to its middle ~60%. Copy the same files to SlopArena-web `src/assets/fighters/<name>-portrait.png`.
- **Home poster.** P5 at 1438x810 into `Assets/UI/MainMenuBackground.png`, with the SLOPArena name mark and `GET IN THE SLOP.` on the yellow plate. Faces stay out of the bottom-left, where chat sits.
- **Steam store.** Main capsule (1232x706) and header capsule (920x430) from P5 plus the logo; the small capsule (462x174) is a tight crop around the plate in which the logo fills most of the space; the vertical capsule (748x896) comes from P6 plus the logo; the page background (1438x810) is P10, or a dimmed P5. Base capsules carry only the game name, no tagline.
- **Steam library.** Capsule (600x900) from P6 plus the logo; header (920x430) is the store header; hero (3840x1240) is P7 without text; logo (1280 wide, transparent PNG) is the one-line SLOPArena name mark on the website's torn Action Yellow plate, typeset from `client/Unity/Assets/Fonts/ArchivoBlack-Regular.ttf` like the top bar and the website.
- **After Hours billboard.** A square crop of P6 with the name mark, 1254x1254, replacing `AfterHoursFightNight.png` in place (the wide art's square crop put the mark at the board's lower edge, low in the gameplay camera's view).
- **Icons.** The Steam community icon (184x184) and client icon come from the bomb in `Assets/UI/SlopArenaIcon.png`; the X-eyed crowned smiley retires.
- **Website.** With the new portraits in place, rebuild `public/social-preview.png` from `scripts/social-preview.html`.

Retire with the old set: the brushy crowned wordmark (`sloparena_wordmark_transparent_master.png` and its Steam logo exports) and the brush-lettered `GET IN THE SLOP` art.

## Review checklist

- **On-model, against the sheet.** FightGuy: no mask, nose band-aid, low headband, white trousers, bare feet. Manki: one googly eye, one wink, closed grin, three black head spikes, black-tipped tail. Wibou: T-shirt and no coat, fedora, sunglasses, thigh holster. Sir Bonk: teal bucket helm with a straw crest and eye slit, two pot-lid pauldrons, barrel-stave chest plate, colander on the right elbow, wooden plank with red stripes. Count fingers.
- **Portrait test.** The 512x512 crop reads at 64 px next to the other three, and nothing crosses the face.
- **The look.** Paper and Ink do most of the work, with one dominant shape and one joke. No black-and-orange grunge, glow, light rays or stadium lights.
- **No stray text.** Only the allowed words; no fake logos or signatures.
- **Steam.** The logo is legible at 462x174; the hero has no text; faces stay inside the safe areas.
- **Store notes.** Steam's content survey includes an AI-generated content disclosure; generated store art belongs there. Wibou's blue pendant reads as Dragon Quest's Slime (Square Enix), so keep it small on Steam capsules.

## Reference files

`references/key-art/` holds the four model sheets (Blender renders of the live Unity models with neutral light, rest pose and separately rendered props) and three HUD-free scene references: `scene-night-court.jpg` (the Night Court concept renders), `scene-sanctuary.jpg` and `scene-kaboom-quarry.jpg` (art-pass captures). Rebuild the sheets with [`tools/blender/model_sheet`](../../tools/blender/model_sheet/README.md) after any model or texture change; a sheet from an older model reintroduces the drift this pack fixes.
