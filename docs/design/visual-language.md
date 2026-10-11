# SlopArena Visual Language

**Status:** living guide  
**Scope:** shared visual identity, 3D art intent, UI presentation, marketing, community/Workshop surfaces, and presentation copy  
**Graphic reference:** [SlopArena Web](https://github.com/Binoui/SlopArena-web) and its [live landing page](https://binoui.github.io/SlopArena-web/) are the strongest existing marketing expression, not a template for every surface or proof of release availability.

## Purpose

SlopArena needs to look like the same game across menus, results, trailers, web pages, Workshop documentation, social images, and future creator tools. This guide defines the reusable visual grammar behind those surfaces.

It does not prescribe one layout or shader. Reconcile weaker existing choices when that strengthens the identity; this unreleased solo-developed project is not bound to preserve every incumbent treatment.

The separate [Art and Asset Conventions](../contributing/conventions.md) remain authoritative for 3D character rendering, source assets, animation naming, licensing, and package hygiene. The two guides meet at readability, palette, silhouette, and presentation tone.

## Approved direction and implementation status

The direction reconciled on 2026-09-30 is **low-poly underground toy-fight-club, with DIY internet-game energy**. SlopArena is not on Steam yet. Release copy must describe actual access rather than imply a public storefront or available Steam demo.

The shared graphic anchors are the **Archivo Black name mark and menu declarations**, **Action Yellow `#FFCC22`**, warm Paper/dark Ink, and Orange impact. This is an approved direction, not a claim that every surface implements it. Native UI still uses Baloo 2 and older yellow shades in places; the website still uses chartreuse and misleading Steam availability copy. This documentation reconciliation does not change fonts, styles, assets, or release copy in the running products.

The owning `DESIGN.md` files specify each surface's realization; Impeccable surface briefs and JSON sidecars mirror those decisions. They do not establish competing brand guides.

## Current native pre-match implementation

The Unity implementation lives in `Assets/UI/SlopArena.uss` and the Main Menu, Server Browser,
Lobby Room, Character Select, and Stage Select UXML screens. The menu uses the bundled
Baloo 2 display face through `Assets/Fonts/Baloo2.asset`; utility text retains Unity's UI
face. Ink, paper, yellow, and orange carry the fight-flyer identity without tactical grit
or invented social activity. Baloo is current implementation evidence, not the approved
display direction for future reconciliation.

Settings is the first page-by-page pack-inspired layout pass. The frontend and match
settings entry points keep their existing navigation and preferences; both mount the
same `Assets/Resources/UI/SettingsOverlay.uxml` shell, with category controls generated
from the current settings service. `SettingsOverlay.uss` supplies the tracked base
style. Locally, `Assets/Resources/UI/LocalPack/PackSkin.uss` applies recolored FPS UI Pack
sprites to Settings; Home uses tracked neutral USS rather than baked-color sprites.
Credit: Lynda Mc Donald (Loudeyes Games). The license permits use in projects
with credit but prohibits redistributing the assets themselves. That directory
is excluded through local `.git/info/exclude`; clean checkouts use project-owned
fallback styles until an asset-distribution plan is agreed.

- **Persistent top bar:** The yellow Archivo wordmark remains the Home
  control; TRAINING / SOLO / ONLINE retain their order. The authored wallpaper
  continues behind the transparent bar; navigation has local paper backing
  and a visible ink border.
  Active mode uses an ink-on-yellow field and ink underline; exposed identity
  and utility labels use readable paper-surface roles rather than pale text.
  Existing display-name/modal/session guards and Back/Leave routes remain.
  Settings, identity, and Menu stay on the right; semantic connection colors
  change only for contrast, not presence logic.
- **Main Menu:** Training, Solo, and Online keep their original routes and
  display-name gate, with paper Training/Solo controls and yellow Online.
  The approved 2026-10-01 layout separates left mode buttons, center artwork,
  and right announcements instead of stacking controls over the illustration.
  Original 1438×810 art remains fully visible in an aspect-fitted ink/paper
  frame; yellow attachment strips and an announcement heading add print character.
  The larger poster uses available page height independently of chat size.
  The user-authored `Assets/UI/FrontendPaperBackground.png` is shared behind
  every frontend shell page, not substituted for the 3D gameplay arenas.
  `#F0EEE8` controls/header fields, exact `#1B1A18` ink, Archivo declarations,
  and `#FFCC22` actions remain; no local licensed sprites are required for Home.
  Fighter selection continues to read the admitted cooked catalog.
- **Chat:** Hide and resize form one top-right control row; there is no maximize.
  Hidden chat reopens with CHAT at bottom-left. Account entry stays in the shell.
  Paper frame/channel tabs and ink text contrast with dark history/input fields.
  Active channel and available SEND are yellow; unavailable controls remain
  readable and disabled. Direct retains conversation and mute management.
  Shared user-resized dimensions persist across menus, gameplay, scenes, and launches within viewport bounds.
  Chat is an absolute overlay everywhere: it never reserves page space or
  moves/resizes the composition underneath, including Home's poster.
  Selection pages keep default bottom-left chat space free of essential controls:
  fighter P1/P2, Player/CPU configuration, and stage participants/actions
  sit in the bottom middle/right. This layout never tracks resized chat dimensions.
  User-enlarged overlap remains permitted; do not rebalance controls dynamically.
  Frontend and gameplay keep their established left-side anchoring.
  Match entry still closes interactive chat input; disconnected and send
  feedback render inside the message history instead of below the composer.
- **Online:** browsing, hosting, and Join by Address converge on the registered room
  session before fighter and stage selection. Address entry accepts IPv4 plus an
  optional port; it still requires the room directory. It is not an offline bypass.
- **Solo and Training:** fighter select uses matching title-only headers:
  `SOLO // SELECT YOUR FIGHTER` and `TRAINING // SELECT YOUR FIGHTER`.
  The participant summary, step/status, and SELECT STAGE are grouped beneath
  the roster.
  In both modes, P1 and P2 are selectable editing slots; the roster assigns
  the chosen fighter to the active slot. The charcoal config panel names that
  slot and its PLAYER/CPU type, keeps PLAYER/CPU edit shortcuts, and shows
  difficulty buttons only for CPU. The active slot and its roster fighter
  share yellow emphasis; the inactive slot remains neutral. Returning from
  Stage Select preserves both fighters and difficulty. Training uses the
  selected CPU fighter and difficulty, persistent Idle/Attack/Shield/Avoid
  training controls, separate damage/position resets, and no-win match rule.
  Fighter cards use face-focused crops from the four character splash illustrations:
  quiet charcoal idle frames, a thicker yellow selected edge and brighter name
  strip, with both local-mode summaries echoing that selected treatment.
  Full illustrations live in `Assets/UI/SplashArt/`; their 512×512 crops
  replace `Assets/Resources/UI/Portraits/` and also appear in the gameplay HUD,
  stage-select summary, and results.
  Fighter selection opens the shared Stage Select page. The Training arena is
  first and preselected when admitted; otherwise the first available stage is
  selected. Enter Training loads the chosen stage with the existing local tools.
  Stage Select places participant cards and the stage action side by side
  in the bottom middle/right, leaving the default bottom-left chat area clear.
  Compact density drops secondary header copy; the admitted stage list remains
  scrollable when more stages are admitted or the window is smaller.
  Chat's absolute overlay has no role in the stage page's geometry; players
  choose its dimensions even when it covers page content.
  Training and Solo place the player at the first baked spawn and the first NPC
  at the second; added Training NPCs use subsequent authored spawns, cycling
  those NPC spawns when needed. NPC respawns retain the selected marker's
  position, height, and facing.
- **Stages:** cards show rendered previews of the actual project prefabs. Fighter
  portraits use crops of the supplied splash illustrations, not prefab renders.
- **Recovery:** focus is visible; controller Back closes address entry before leaving the
  browser. Escape, gamepad Start, or the top-right gear opens the frontend menu on any
  pre-match page, including over address entry and chat. Resume or a second Escape/Start
  returns to the same page; Settings returns to the menu when closed; Quit Game exits
  immediately. Mandatory identity entry and active Settings confirmations/remaps keep
  their own cancel behavior. Directory, host, and room failures present a retry or a
  route back rather than an indefinite connecting label. Empty player slots are not
  presented as connected players.

Results and in-match HUD presentation are outside this pre-match refresh.

## One-sentence definition

> A ridiculous underground fight club built from scavenged low-poly characters, held together by bold DIY graphics. Cool enough to main. Stupid enough to remember.

## Core tensions

SlopArena should hold these pairs at the same time:

| Keep | In tension with |
| --- | --- |
| Cool fighting imagery | Stupid, dry jokes |
| Rigid geometry | Imperfect placement |
| Strong information hierarchy | Cheap photocopied texture |
| Arcade confidence | Honest prototype energy |
| Bold character silhouettes | Restrained decoration |
| Aggressive impact | A welcoming game made for friends |

If one side takes over, the identity weakens. Pure cool becomes generic esports branding. Pure jokes become disposable meme UI. Pure disorder becomes difficult to use.

## Shared invariants

- The same Archivo Black SlopArena name mark across game and marketing; its plate, scale, and placement adapt to the surface.
- Chunky, readable fighter identities with specific silhouettes, signature colors, weapons, and clear move tells.
- Cool first, joke second: a legitimate fighting-game composition with increasingly ridiculous details.
- Paper/Ink graphic construction, scarce Action Yellow, Orange impact, hard boundaries, and physical print offsets.
- Authored imperfection around a stable hierarchy and interaction geometry. Wonky art is allowed; unclear controls and random spacing are not.
- Real gameplay, honest status, and useful copy. Solo-development and unreleased status are facts, not apologies or fabricated launch claims.

“Toy” describes the scavenged construction attitude, not mandatory plastic materials, literal toy characters, cute proportions, or cozy worlds. “Underground” describes an improvised competition, not a requirement that every arena be grimy or behind a warehouse.

The reference axes have separate jobs: Smash-like readability; low-poly indie kitsch for construction; skate/punk DIY for graphic language; PS2 multiplayer weirdness for premises; internet shitpost culture for secondary details. Do not apply all five as competing treatments on every screen.

## Principles

### 1. Information first

The user should immediately understand what happened and what they can do next. Use scale, contrast, grouping, and short labels before adding decoration.

A screen may look crooked. Its hierarchy must not be crooked.

### 2. Deliberate imperfection

Use slight rotations, offset shadows, pasted cutouts, scribbles, uneven rules, and wonky decorative shapes as controlled accents. Controls, spacing, and reading order remain deliberate; most elements align to a clear grid.

Imperfection should feel authored, not randomized. Do not rotate every card or apply noise to every surface.

### 3. One loud gesture

Give each graphic composition one dominant declaration: a result, character name, call to action, status, or event. Supporting information is smaller and quieter. In gameplay, the relevant state or event takes priority; a decorative headline must not compete with the fight.

Avoid a screen where every label competes at headline size.

### 4. Cool first, joke second

The overall silhouette should work as a fighting-game image before the user reads the joke. Humor lives in details, annotations, loading text, status copy, and secondary labels.

Do not use irony to apologize for weak presentation.

### 5. Characters are graphic material

Treat character renders like cutouts pasted into a fight flyer. They may overlap frames, extend beyond the canvas, sit behind type, or enter at a slight angle.

Preserve the face, pose, weapon, and gameplay silhouette. Cropping should add force, not hide identity.

### 6. Texture supports hierarchy

Paper grain, photocopy noise, halftone, stamps, and rough edges should stop clean areas from feeling sterile. They must never reduce gameplay readability or obscure small text.

### 7. Scavenged sources, deliberate result

Asset-pack collage is part of the identity, not something to disguise. Curate different low-poly kits through compatible scale, lighting, material response, palette/value hierarchy, and composition. The result should feel like an authored junkyard fight club, not a vendor demo scene.

Favor chunky simplified geometry, exaggerated readable proportions, strong character colors, and big shapes over realism or hyperdetail. Cheap source assets are allowed; accidental composition is not. AI assistance is a production method, not a visual style: reject generic diffusion gloss and incoherent detail.

## Foundation

### Palette

The approved shared graphic palette retains the website's Paper/Ink/Orange foundation and replaces its chartreuse action ink with the native Home yellow:

| Token | Value | Role |
| --- | --- | --- |
| Paper | `#DED8C9` | Primary warm background |
| Ink | `#171814` | Text, borders, dark fields |
| Action Yellow | `#FFCC22` | Primary graphic actions, selection, availability |
| Orange | `#F05B35` | Combat energy, highlights, numbering |

Supporting neutrals adapt to the surface: marketing and native frontend are paper-led. Settings also uses paper outer framing and category navigation, retaining a calm dark field for dense utility controls. Match overlays, chat history/input, and HUD remain restrained ink fields. The approved authored frontend wallpaper may be warmer than solid control paper; exact `#1B1A18` ink is not warmed or inverted. Texture never reduces text or state legibility.

Character signature colors, team colors, gameplay category colors, and semantic status colors may extend the palette. Yellow is not a recoloring mandate for characters, stages, or the blue special-ability slots. Exact local neutral and Orange values belong in the owning `DESIGN.md`; do not invent a new decorative theme per screen.

#### Color discipline

- Paper and Ink carry most of the composition.
- Action Yellow marks primary action, current selection, or availability; confirmation and failure also need explicit semantic information.
- Orange marks impact, combat, and expressive interruption; it is not a second competing primary-action color.
- Use one accent as dominant in a region; using Yellow and Orange equally everywhere removes their meaning.
- Preserve accessible text contrast. Small text belongs on calm, high-contrast fields.
- Do not rely on color alone for gameplay or status information.

### Typography

The visual system uses a common name mark and two typographic roles:

1. **Name mark and declarations:** Archivo Black supplies the shared SlopArena lettering and bold menu/marketing declarations. The yellow native name plate may remain as a container; its lettering is not a separate brand.
2. **Utility:** Space Mono supplies short marketing instructions, metadata, labels, and dry annotations. Native short labels may use a mono treatment where practical; dense settings, body copy, and HUD information use a restrained readable sans.

Baloo 2 remains in the current native implementation but is not the target display voice. Do not imply Archivo is already bundled in Unity. Do not make every label display type or force monospace where it harms scanning.

#### Typographic behavior

- Prefer uppercase for short display and utility labels.
- Use extreme scale contrast in marketing and celebratory compositions; use a tighter, readable hierarchy in task-heavy menus and HUD.
- Keep body copy short and give it comfortable line height.
- Tighten display tracking; give tiny utility labels slightly wider tracking.
- Use a rotated or shadowed word sparingly to create one focal interruption.
- Avoid fake handwritten fonts for every joke. A real annotation may be handwritten; the system remains typographic.

### Shape and construction

The system is built from ordinary shapes used assertively:

- thick rectangular borders;
- offset hard shadows;
- numbered sections;
- circles as framing or annotation devices;
- rules dividing information;
- rectangular stickers and status strips;
- cutout imagery crossing boundaries.

Corners are usually square. Rounded cards, glass panels, soft shadows, and glossy gradients should be rare exceptions with a clear functional reason.

### Texture

Preferred texture vocabulary:

- warm paper grain;
- photocopy noise;
- coarse halftone;
- slightly misregistered color;
- stamped or taped accents;
- rough masking around character cutouts.

Keep texture subtle on interactive and information-dense surfaces. Never bake critical text into a noisy texture.

## Composition grammar

### Grid first, disruption second

Start with a clear grid. Establish primary, secondary, and utility zones. Then break the grid once or twice through overlap, rotation, cropping, or an annotation.

### Borders and shadows

Use borders to state ownership and grouping. Use hard offset shadows to give important actions or posters physical presence.

- Keep shadow direction consistent within one surface.
- Do not add soft ambient shadows to every element.
- Interactive shadows may collapse or shift on press.
- Borders must remain readable at the target game resolution.

### Rotation

Typical rotation should be subtle: approximately one to three degrees. Larger angles are reserved for stickers, stamps, or clearly decorative fragments.

Do not rotate paragraphs, settings controls, or dense information.

### Layering

A useful default stack is:

1. paper field or dark ink field;
2. structural borders and section geometry;
3. large headline;
4. character or gameplay imagery;
5. small labels, status, and annotations;
6. restrained texture over or under the composition.

Maintain enough separation that all interactive states remain legible.

## Imagery

### Character renders

- Prefer poses with a readable action line and strong silhouette.
- Use project-rendered or approved character imagery rather than generic fighting imagery.
- Cut characters cleanly from the background; roughness may be added at the mask edge afterward.
- Let characters frame information rather than automatically becoming a symmetrical versus poster.
- Avoid overfilling every surface with the full roster.
- Splash illustration must preserve the actual model's proportions, costume, weapon, signature colors, and chunky forms. Amplify the fighter, not a glossy cinematic version of a different game.

### Gameplay media

Gameplay footage is evidence, not wallpaper. Preserve readable fighters, stage boundaries, hit effects, and HUD state. Use the [Visual Presentation Baseline](../visual-baseline.md) when comparing presentation changes.

Frames around gameplay may use the poster language, but the footage itself should remain clear.

### Icons and marks

Use simple geometric icons with strong weight. Prefer arrows, crosses, circles, underlines, and compact symbols over elaborate outlined icon sets. Icons should feel printed or constructed from the same rules as the typography.

Crude symbols and an occasional deliberately bad bomb drawing are welcome when they have a specific job and remain recognizable. They are not permission for ambiguous icons, a pile of unrelated fonts, or a generic skull/hazard/graffiti decoration kit.

## Motion

Motion should behave like physical graphic material:

- cards snap, stamp, slide, or slightly overshoot;
- offset shadows compress on press;
- labels may appear as pasted strips;
- transitions should be short and decisive;
- combat events may use harsher scale and positional impact than navigation.

Avoid constant floating, glossy easing, particle decoration behind menus, and slow cinematic transitions for routine actions.

Respect reduced-motion settings outside gameplay. Motion must reinforce state change rather than merely prove that the UI is alive.

## Voice and writing

The voice is short, direct, self-aware, and slightly stupid. It should sound confident enough that the joke does not become an apology.

### Good patterns

- `IT WILL BREAK.`
- `PROBABLY SAFE.`
- `NO BALANCE GUARANTEED.`
- `MADE WITH QUESTIONABLE DECISIONS.`
- `GET IN THE SLOP.`
- `TELL ME WHAT BROKE.`

### Writing rules

- State the useful information first.
- Keep jokes short enough to scan.
- Use dry understatement more often than exclamation marks.
- Let system status be honest.
- Prefer specific failure language over generic `Something went wrong`.
- Do not interrupt repeated gameplay flows with a new joke every time.

### Avoid

- `Experience the ultimate battle.`
- `Master unique heroes.`
- `Enter an epic arena.`
- forced lore voice;
- generic esports aggression;
- excessive meme references;
- jokes that obscure a required action;
- edgy anarchy language used as a substitute for personality.

## Applying the language

### In-game menus

Use the shared Archivo name mark and declarations, Action Yellow, paper-led frontend framing with readable ink text, calm dark utility fields, hard-edged controls, and restrained pasted layers. Match overlays remain restrained over gameplay. Navigation/hit targets remain stable; selection, confirmation, disabled state, and focus outrank decoration. Chat is an absolute, user-resized overlay, never a reason to rebalance the page underneath.

### Character select

Let character color and silhouette carry identity inside the shared Paper/Ink system. Use asymmetry and overlaps around the stable selection grid, not inside the control logic.

### HUD

Gameplay clarity outranks the poster treatment. The local HUD groups orange normals `1–4` and blue specials `A/E/R/F` into two close-set diamonds at the bottom center. Four clipped-square slots per diamond retain authored move icons and cooldown overlays. Printed paper keyboard tabs follow effective bindings; physical number-row keys retain their digits even on AZERTY. Controller face-button artwork uses a readable dark field; missing artwork falls back to text. Specials show the corresponding face button while actual gameplay input still requires LB. Binding prompts switch after gameplay input. Static frame art never encodes gameplay state; brief activation/readiness feedback remains functional rather than decorative.

Default controller layout is physical on both diamonds: top Y = normal `3` / LB+Y special `E`; left X = normal `2` / LB+X special `R`; right B = normal `4` / LB+B special `F`; bottom A = normal `1` / LB+A special `A`. This mapping applies to every character on ground and in air. Keyboard controls and explicit saved rebindings remain unchanged.

Player cards use portraits, identity badges/edges, large readable percentages, and an explicit LIVES count with solid living tickets and outlined, struck-through lost tickets. Training omits lives; up to eight tickets fit beside the count and higher counts use numbers only. Critical numbers stay readable after elimination. The independent target bracket/pointer remains visible for a lock when `Show overhead damage` is OFF; only its percentage tab/backing disappear, without changing its anchor or player-panel percentages. The setting defaults ON and persists independently of gameplay lock policy. All percentages share white below 40%, orange from 40–89%, and red at 90%+; player identity and marker colors retain their separate meaning. Reduced flashing suppresses card hit flashes. Readouts stay aligned and texture-free, with no menu wallpaper or chat-dependent positioning.

Kenney Input Prompts 1.5 (CC0) supplies optional controller artwork. For a local build, copy `Xbox Series/xbox-series_sheet_default.{png,xml}` to `client/Unity/Assets/Resources/InputPrompts/xbox.{png,xml}`. The `InputPrompts` directory and its Unity metadata are gitignored; a clean checkout uses controller text until those assets are provisioned. Keyboard tabs are always text and need no sprite sheet. The atlas is loaded only when controller artwork is requested. Its supplied PNG rows are inverted relative to the XML rows; `InputPromptAtlas` accounts for this in its UVs. Import the sheet without NPOT scaling. Gameplay authority, camera/stage art, chat, and move packages are unchanged by the HUD treatment.

### Results

Results are a strong candidate for the full language: one dominant outcome, oversized winner/placement type, character cutouts, compact stats, and short contextual copy.

### Ability Lab and creator tools

Use the visual identity for shell, section hierarchy, status, empty states, and previews. Editing controls must remain tool-like, aligned, and predictable. Creator UX should not imitate a chaotic poster at the expense of authoring speed.

### Website, trailers, and social images

These Persuade surfaces may use the highest texture, overlap, cropping, and typographic contrast. Retain the website's Archivo Black/Space Mono flyer construction, with Action Yellow replacing chartreuse in the approved direction. Real characters and gameplay distinguish it from generic brutalist grunge. Feedback forms and access instructions remain calm and usable. The live landing page is reference evidence, not authority for its inaccurate Steam claims.

### Stages and world decoration

Translate the attitude rather than pasting UI onto the world. A clear fighting space sits inside one memorable, improvised premise: rooftops, industrial junk, strange pools, night cities, or a child's tabletop can all belong. Curate landmarks, scale, lighting, and background contrast around the fighters. Strong character colors need not make the world cute or cozy. Signs, cheap materials, playful sponsorships, and specific jokes may support the premise; generic graffiti, random props, grime, or anarchy symbols are not substitutes for one.

## What SlopArena is not

- cyberpunk neon;
- polished esports branding;
- generic graffiti or anarchy imagery;
- soft SaaS cards and glassmorphism;
- grey Unity prototype UI;
- random rotations everywhere;
- grunge texture without hierarchy;
- cartoon meme overload;
- photorealistic military aggression;
- generic asset-pack showcase scenes with stock UI;
- cute/cozy styling as the default tone;
- “AI slop” gloss, incoherent hyperdetail, or realism for its own sake;
- a parody that is embarrassed to be a real fighting game.

## Source-of-truth hierarchy

When references disagree, use this order:

1. Functional requirements, gameplay authority, accessibility, and gameplay/interaction readability.
2. This living guide for shared identity and graphic grammar; [Art and Asset Conventions](../contributing/conventions.md) own 3D presentation and asset practice, and [Stage Concepts](stage-concepts.md) own stage composition.
3. The owning project's `DESIGN.md` for the surface realization and exact target tokens; Impeccable briefs and sidecars mirror it.
4. Approved examples and actual implementation evidence. The website is the strongest existing marketing reference, not a universal layout or a release-status source.
5. Moodboards and external references.

Existing code and assets establish what is implemented, not what must be preserved. Keep approved direction separate from current behavior; never describe a documentation decision as a shipped change. Update this guide when an explicitly approved direction or successful surface establishes a shared rule, rather than copying one-off accidents or adding a competing guide.

## Agent brief

Use this instruction when asking an agent to create or revise a SlopArena visual surface:

> Read `docs/design/visual-language.md`, the owning `DESIGN.md`, and art conventions for 3D work. Use the website's existing flyer construction as graphic evidence, not a layout mandate. Preserve the scavenged low-poly fight-club identity: cool first, joke second; curated asset-pack collage; readable silhouettes; shared Archivo name mark and declarations; Paper/Ink, Action Yellow `#FFCC22`, and Orange impact; hard geometry with controlled DIY imperfection; useful, dry copy. Adapt expression to the surface: 3D art is character/combat-first, menus are Operate, HUD reports state, marketing is Persuade. Gameplay readability and interaction state take priority over decoration. Distinguish approved direction from unchanged implementation and never infer Steam availability.

For implementation tasks, also require the agent to inspect the existing scene/component and reuse established project tokens and controls before introducing new ones.

## Review checklist

Before approving a new surface, ask:

- Is the primary information obvious within one second?
- Is there one dominant visual gesture?
- Does the screen feel cool before the user reads the joke?
- Is imperfection controlled rather than random?
- Are Paper and Ink doing most of the work?
- Do Action Yellow and Orange still have distinct jobs?
- Are display and utility typography used for different roles?
- Can characters and gameplay still be read at the actual target size?
- Are controller, keyboard, hover, focus, disabled, loading, and error states clear where applicable?
- Would removing the texture leave a strong composition?
- Does the copy give useful information before personality?
- Does the result feel like SlopArena rather than generic brutalism or generic grunge?
- Could this belong to a ridiculous underground fighting game made from scavenged low-poly characters, while looking cool enough to main somebody?
- Can I immediately read what that fighter is doing?
- Does splash art describe the actual model rather than promise another visual world?
- Are approved direction, current implementation, and actual release availability stated separately?

## Maintaining the reference

Add canonical examples only after they have shipped or been explicitly approved. For each example, record:

- the surface and purpose;
- a screenshot or stable link;
- the rule it demonstrates;
- any intentional deviation;
- the project version or commit.

Prefer a small set of strong examples over a large undifferentiated moodboard.

### Approved visual board — rough edges, clear decisions

**Reference:** [Offline visual board](references/sloparena-visual-identity/visual-reference-board.html) and [rooftop composition study](references/sloparena-visual-identity/visual-reference-study.png), retained verbatim from the user's [SlopArena visual identity export](https://drive.google.com/file/d/1pxXADuEctjnT7L8FNTpxq3Hvf5uF0Peu/view). Approved as visual reference on 2026-09-30. Source version: unversioned user export `sloparena-visual-identity-export.zip`; not a project build or gameplay capture.

- **Purpose:** demonstrate how the shared identity translates into graphic/world composition, palette/type specimens, and interactive UI treatment.
- **Adopt:** rough fight-flyer framing around calm controls; chunky world geometry with one memorable landmark; Archivo declarations, mono annotations, readable sans information; scarce yellow selection; hard press shadows and a short outcome stamp.
- **Keep illustrative:** the rooftop is not a mandatory stage template or proof of collision, recovery, or gameplay-camera readability. The tab labels, page layout, readiness example, and motion timings are specimens, not approved changes to navigation or gameplay.
- **Palette boundary:** Action Yellow `#FFCC22` is established. The board's Dark Ink `#1B1A18`, Warm Paper `#F2E8D5`, and Impact Orange `#F46B35` are proposed starter shades, not replacements for owning-project tokens.
- **Typography boundary:** the rooftop image's tall angular lettering illustrates headline attitude, not a second canonical wordmark. The shared name mark remains Archivo Black.
- **Authority and scope:** the board includes the exported written direction for context; this living guide and the owning `DESIGN.md` remain authoritative. Do not import another guide or agent instructions from the export. This reference does not claim shipped changes or make visual reconciliation a Steam-release requirement.

