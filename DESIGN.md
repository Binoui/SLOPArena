---
name: SLOPArena
description: Bold DIY graphics for an unreleased low-poly underground toy-fight-club.
colors:
  ink: "#1B1A18"
  deep-ink: "#161513"
  paper: "#F0EEE8"
  action-yellow: "#FFCC22"
  combat-orange: "#E05C2A"
  online-green: "#4CDD88"
  utility-surface: "#252422"
  utility-border: "#7B7872"
  highlight: "#FFFFFF"
  hud-ink: "#1B1A18"
  hud-special-blue: "#39A9FF"
  fightguy-ki-blue: "#39A9FF"
  fightguy-ki-light: "#A8DEFF"
  fightguy-ki-core: "#F0FAFF"
  fightguy-ki-edge: "#1764D9"
  bonk-steel: "#B8B2A8"
  bonk-impact: "#EEE8DD"
  bonk-iron: "#67635D"
typography:
  display:
    fontFamily: "Archivo Black"
    fontWeight: 700
    letterSpacing: "-4px"
  label:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "12px"
    fontWeight: 700
    lineHeight: 1.1
    letterSpacing: "2px"
  hud-move-name:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "12px"
    fontWeight: 700
  hud-modifier:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "18px"
    fontWeight: 700
  hud-target-number:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "22px"
    fontWeight: 700
  hud-target-number-wide:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "18px"
    fontWeight: 700
  hud-target-percent:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "12px"
    fontWeight: 700
  hud-countdown-word:
    fontFamily: "Archivo Black"
    fontSize: "48px"
  hud-countdown-number:
    fontFamily: "Archivo Black"
    fontSize: "122px"
  hud-end-headline:
    fontFamily: "Archivo Black"
    fontSize: "68px"
  hud-end-outcome:
    fontFamily: "Archivo Black"
    fontSize: "26px"
  hud-end-winner:
    fontFamily: "Archivo Black"
    fontSize: "22px"
  hud-end-shared-name:
    fontFamily: "Archivo Black"
    fontSize: "14px"
rounded:
  none: "0px"
spacing:
  sm: "8px"
  md: "12px"
  lg: "18px"
components:
  button-primary:
    backgroundColor: "{colors.action-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "18px"
    height: "56px"
  button-hot:
    backgroundColor: "{colors.action-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "18px"
    height: "56px"
  status-pill:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    rounded: "{rounded.none}"
    padding: "8px 12px"
  hud-move-column:
    rounded: "{rounded.none}"
    width: "68px"
    height: "113px"
  hud-normal-tab:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.hud-ink}"
    width: "64px"
    height: "34px"
  hud-special-key:
    backgroundColor: "{colors.hud-ink}"
    textColor: "{colors.paper}"
    width: "64px"
    height: "64px"
  hud-move-hover:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.hud-ink}"
    typography: "{typography.hud-move-name}"
    width: "240px"
    height: "26px"
  hud-target-lock:
    width: "100px"
    height: "58px"
  hud-target-readout:
    backgroundColor: "{colors.hud-ink}"
    textColor: "{colors.paper}"
    typography: "{typography.hud-target-number}"
    rounded: "{rounded.none}"
    padding: "0px 6px"
    width: "76px"
    height: "28px"
  hud-countdown-ready:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.hud-ink}"
    typography: "{typography.hud-countdown-word}"
    rounded: "{rounded.none}"
    width: "268px"
    height: "84px"
  hud-countdown-number:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.hud-ink}"
    typography: "{typography.hud-countdown-number}"
    rounded: "{rounded.none}"
    width: "148px"
    height: "156px"
  hud-countdown-start:
    backgroundColor: "{colors.action-yellow}"
    textColor: "{colors.hud-ink}"
    typography: "{typography.hud-countdown-word}"
    rounded: "{rounded.none}"
    width: "468px"
    height: "106px"
  hud-end-declaration:
    backgroundColor: "{colors.action-yellow}"
    textColor: "{colors.hud-ink}"
    typography: "{typography.hud-end-headline}"
    rounded: "{rounded.none}"
    padding: "8px 14px 12px 14px"
    width: "460px"
    height: "226px"
  hud-end-shared-declaration:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.hud-ink}"
    typography: "{typography.hud-end-headline}"
    rounded: "{rounded.none}"
    padding: "8px 14px 12px 14px"
    width: "460px"
    height: "226px"
  hud-end-outcome:
    backgroundColor: "{colors.hud-ink}"
    textColor: "{colors.paper}"
    typography: "{typography.hud-end-outcome}"
    padding: "0px 8px"
    height: "38px"
---

# Design System: SLOPArena

**Authority and status:** [Visual Language](docs/design/visual-language.md) owns the shared identity; [Art and Asset Conventions](docs/contributing/conventions.md) own 3D presentation and assets. This document specifies the native approved target, mirrored by `.impeccable/design.json` and its surface brief. The unreleased solo-developed game is not on Steam. The player-facing identity rollout is implemented across Home, shared shell, fighter/stage selection, Results, browser/lobby, Settings, pause/Training controls, chat, and entry/confirmation dialogs. Gameplay HUD now combines portrait fight cards and prominent lives with four paired move keys displaying all eight moves, plus an independent target marker with optional damage. Functional colors and gameplay authority remain. Creator/Ability Lab tools, website migration, connected-room acceptance, and release-copy changes are not claimed.

## Overview

**Creative North Star: "Low-poly underground toy-fight-club, with DIY internet-game energy."**

SLOPArena's interface and imagery feel like an improvised fighting world assembled from scavenged pieces: chunky low-poly forms, exaggerated silhouettes, and a curated collage of assets. “Toy” describes that construction metaphor, not a requirement for literal plastic or cute styling. Slightly crappy on purpose, but extremely readable.

**Cool first, joke second.** Treat the website as the strongest existing graphic reference, not a fully approved implementation. Use authored asymmetry, hard edges, and controlled print offsets to create personality without compromising stable interaction geometry or clear player actions.

**Approved visual reference:** inspect the [offline board's interface and type specimens](docs/design/references/sloparena-visual-identity/visual-reference-board.html), approved on 2026-09-30. Use the aligned controls, hard press depth, clear selection/readiness treatment, and declaration/annotation/information hierarchy as target-treatment evidence. Its tab labels, layout, timings, and starter neutral/orange shades are illustrative—not approved navigation changes, replacement tokens, or shipped Unity UI. [Shared reference boundaries](docs/design/visual-language.md#approved-visual-board--rough-edges-clear-decisions) govern what to copy. This reference does not make the identity migration a release prerequisite.

**Key Characteristics:**
- Underground fight-club energy with DIY internet-game construction.
- Chunky low-poly silhouettes and curated asset-pack collage, harmonized through scale, lighting, material response, palette/value hierarchy, and composition—not a compulsory universal shader.
- Bold Archivo Black name mark and target display voice, with readable utility type.
- Character forms remain recognizable and gameplay-readable.
- Dry, self-aware copy never obscures an action or status.

## Colors

The palette uses local paper and ink values, with Action Yellow reserved for primary actions and selection. Combat Orange remains a local accent; do not replace it with a shared orange value.

### Primary
- **Action Yellow** (`#FFCC22`): Graphic primary actions and selection emphasis.
- **Combat Orange** (`#E05C2A`): Native local impact and expressive emphasis.

### Secondary
- **Online Green** (`#4CDD88`): Live/online status only. Pair with text or structure; never make it the sole status channel.

### Neutral
- **Paper** (`#F0EEE8`): Frontend canvas/header and light control/card fields; readable text on dark panels.
- **Ink** (`#1B1A18`): Dark text/framing and calm recessed regions; never globally inverted to make a light page.
- **Deep Ink** (`#161513`): Recessed fields, hard backing, and neutral HUD outlines.
- **Utility Surface** (`#252422`): Dense utility fields and secondary controls.
- **Utility Border** (`#7B7872`): Quiet boundaries and inactive structure.
- **Highlight** (`#FFFFFF`): Hover/focus lift only; do not replace Paper as the default light field.

**One palette (2026-10-18):** Menu, Settings, Arcade and Tutorial stylesheets use only these values; the older yellow `#FFC821` and the purple-black inks/greys (`#17131B`, `#0D0D14` and their tints) were replaced by Action Yellow, Ink, Deep Ink, Utility Surface and Utility Border.

**Implemented paper/ink roles:** The frontend is paper-led, with the user-authored `Assets/UI/FrontendPaperBackground.png` behind every shell page. Shared `--shell-page`, page-ink/muted/warning/error roles distinguish the outer surface from dark content fields. Existing `--shell-canvas` retains exact ink `#1B1A18`; surface/recess/rule remain `#252422`, `#161513`, `#7B7872`. Paper controls remain `#F0EEE8`, Action Yellow `#FFCC22`. Settings, chat, match overlays, and HUD keep their calm dark-field and functional-color semantics; 3D arenas are not wallpapered or recolored.

**Paired HUD roles:** Paper normal tabs stand on Combat Orange offset plates; Ink special keys stand on HUD Special Blue plates. Holding the effective special modifier turns the special plates and connector Action Yellow, and a key that becomes ready pops with a brief Action Yellow edge. These category accents remain separate from character artwork and the existing damage tiers.

### Named Rules
**The One Accent Rule.** Action Yellow and Combat Orange may both exist on a screen, but one accent owns each region.

**The Paper-on-Ink Rule.** Small text and gameplay status belong on calm, high-contrast fields. Texture and decoration never carry critical information.

## Typography

**Display Font:** Archivo Black is implemented on player-facing menu, selection, outcome, Settings, pause, dialog declarations/actions, and short HUD fighter names. Chat uses it only for short actions. Settings rows, message bodies, percentages, stocks, cooldowns, bindings, and other dense utility remain readable sans or quiet utility mono.
**Body Font:** Native utility sans-serif remains allowed, especially where readability or dense information benefits.
**Label/Mono Font:** Marketing pairs Archivo Black with Space Mono. Short native utility labels may use mono where practical; dense settings and HUD text should stay readable sans.

**Home and shared-shell treatment:** Mode buttons form the left column, original artwork sits fully visible in an aspect-fitted paper/ink frame at center, and announcements form the right column. The poster uses available page height without reserving chat space; two yellow attachment strips and a yellow announcement heading connect it to the printed world. The authored background continues behind the transparent top bar; individual navigation controls retain paper backing and an ink border for legibility over black marks, with yellow selected modes. Calm page-header fields remain. Native evidence: `.impeccable/review/paper-shell-unity-20261001/`, `.impeccable/review/selection-chat-header-20261001/`, and `.impeccable/review/chat-contrast-controls-20261001/`.

**Chat overlay rule:** Chat is an absolute user-sized bottom-left overlay in every scene. The frontend social host is a workspace sibling, not part of the page's lower row; gameplay was already absolute. Resize and hide never reserve page space or reposition the content underneath. Author selection pages with the bottom-left clear: fighter P1/P2, Player/CPU configuration, and stage participants/actions belong in the bottom middle/right. This is a fixed responsive layout, not a reaction to chat dimensions. User-enlarged overlays may still cover content; do not clamp their size or shrink Home's poster. Shared saved dimensions, channel/input ownership, modal guards, and viewport limits remain.

**Chat controls and contrast:** Paper outer frame, paper/ink channel tabs, dark message/input fields with paper rules, yellow active channel and available SEND. Unavailable SEND/server chat remain visibly disabled and readable. Hide and resize share one top-right row; maximize and its expanded-state/focus/host code are removed. Hidden chat's CHAT reopen button stays at the bottom-left. Conversation/mute management remains in the existing Direct view, without a second sidebar.

**Stage-select treatment:** Existing admitted preview assets and the scrolling grid remain; previews sit four per row at roughly 16:9 (236 logical px tall, 230 compact) instead of a thumbnail strip. Full-opacity previews have paper name strips; selected stages keep a yellow strip and frame, while focus shows the yellow frame over a paper strip. Solo opens with its last Solo stage, or the first card, already selected, so START SOLO is visible at once. The header drops the subtitle that repeated the right-hand chip, matching Fighter Select's height. Local participant paper fields and deeper green readiness text preserve state meaning and contrast. Back and confirmation use the shared physical control language. Stage-only styles now live together in `FrontendShell.uss`, not as competing overrides in two sheets.

**Results treatment:** Keep the winner-left/standings-right composition, actual snapshot bindings, ranking colors, portrait assets, and local/online return semantics. Archivo outcome/winner/player declarations and readable statistics take priority; empty bursts/grid, fake feed annotations, redundant eyebrow, and speculative broadcast footer were removed. No winner, score, duration, or shared-victory data is invented. Native evidence for both surfaces: `.impeccable/review/stage-results-20261001/`, including a recorded genuine Solo snapshot reused for before/after rendering.

**Online-flow treatment:** Preserve room discovery/create columns, roster order, actual membership/phase/capacity/IDs, host/leader gating, and network routes. Paper room/player name fields and form/dialog cards sit on near-neutral ink; Action Yellow owns create/join/continue actions. Room-mode name-only children are styled alongside legacy class-based rows. The address modal's mounted wrapper fills the shell without intercepting the page while closed; native scrolling keeps Editor-only tools reachable. Identity and frontend join confirmation share paper cards, dark fields, and physical controls. Scan and connection feedback (loading, empty, unreachable) sits inside the dark room list and roster panels beside REFRESH or RETRY CONNECTION, clear of the bottom-left chat. Open roster slots are quiet OPEN SLOT rows rather than name plates, and an unnamed room shows ROOM plus a six-character code instead of its GUID. Text fields are a single framed box. Native evidence: `.impeccable/review/online-flow-20261001/`. Steam was unavailable: actual connection failures and local interactions were checked, not connected-room admission. Explicitly labelled layout specimens are not live-room evidence.

**Settings, match overlays, and chat:** Settings now uses paper outer framing, ink Archivo title, bordered paper category tabs, yellow selection, and a quiet dark scroll/control field. Every control uses one row: an uppercase bold label at the left and its slider/value, checkbox, dropdown or REMAP at the right; help notes indent under their row, and tab actions (APPLY DISPLAY, RESET CONTROLS) are right-aligned buttons. Keyboard and Mouse list only actions they can bind; stick dead zone, stick look speed and vibration appear only for Controller. Scrollbars are thin, without arrow buttons. Focus follows the shared rule (a selected tab stays yellow with a heavier frame); outer status text is ink, not yellow on paper. All five categories retain actual values, controls, scrolling, remap, reset, and display-revert behavior. Migrated controls out-specify optional local licensed-study images without editing those excluded assets. Training/Solo pause uses the shared modal card with RESUME first; Training utility stays quieter ink, its toggles right-aligned like Settings. Gameplay join dialogs preserve input/modal/pause ownership. Chat retains channel/history/draft/mute/resize and hide/reopen behavior; maximize is removed. Native evidence: `.impeccable/review/remaining-ui-20261001/`, `.impeccable/review/settings-paper-20261001/`, and `.impeccable/review/chat-contrast-controls-20261001/`.

**Fight-card HUD:** Stable 330×120 player cards use larger 100×120 portraits, slightly offset portrait/identity frames, 54 px utility percentages, and a dedicated LIVES count plus filled/outlined-struck tickets. Training omits stocks; 5–8 use tighter ticket spacing and larger stock counts remain count-only. Eliminated portraits quiet down without fading critical numbers. CPU cards read CPU (CPU 2, CPU 3 for added Training CPUs), never an internal entity id. A brief paper KO stamp sits beside the affected card, switching to ink/red OUT on the last life; it replaces the duplicated particle-text/toast stock-loss message and keeps the card's identity accent. The move deck is an icon-only row of keys; fixed grounded move names appear in a small runtime hover label rather than permanent text. Bindings, modifier lettering, and cooldown seconds retain readable utility type. Reduced flashing suppresses damage kicks/flashes and the ready key's pop while keeping sunk cooldown keys and stock-loss information. The independent target marker and existing damage tiers remain. Portrait/stock baseline: `.impeccable/review/fight-card-hud-20261001/`; preceding HUD evidence: `.impeccable/review/hud-punch-20261013-twd77uy1/`; paired-deck evidence: `.impeccable/review/paired-hud-20261013/`; the move keys were accepted in game on 2026-10-17 without a recorded capture. Controlled specimens are presentation proof, not online gameplay acceptance.

**Character:** Heavy, blocky declarations paired with clear utility typography. Display type makes one statement at a time; utility type carries actions, status, and annotations.

### Hierarchy
- **Display** (Archivo Black target): Main title declaration; size and tracking are surface-specific, not a universal fixed 148px prescription.
- **Headline** (bold, screen-specific): Results, fighter names, and major state changes. Keep it materially larger than metadata.
- **Title** (bold, screen-specific): Section and flow titles such as Character Select or Lobby Room.
- **Body** (regular/bold, screen-specific): Short explanatory copy, status messages, and join instructions. Keep it brief and readable.
- **Label** (bold, `12px`, `2px` tracking, uppercase): Kicker text, status strips, player metadata, roster roles, and technical annotations.

### Named Rules
**The Declaration Rule.** Give each composition one loud phrase. Supporting labels are smaller, quieter, and more useful.

**The Joke Is Metadata Rule.** Humor belongs in annotations, status copy, or secondary labels. It never replaces the label for a required action.

## Layout

Screens begin with a stable rectangular grid: primary action zone, content or roster zone, status zone, and utility zone. The grid is then disrupted once or twice with controlled rotation, overlap, crop, or a burst shape. Keep interactive controls aligned even when surrounding poster elements are crooked.

The current Unity UI uses proportional screen anchoring for major regions, with fixed internal dimensions for buttons, HUD billboards, and slots. Common observed rhythm values are `8px`, `12px`, `14px`, and `18px`; dense HUD elements use tighter internal padding than menus. Gameplay HUD information stays stable while the camera moves: player billboards stack predictably, the four-column move-key deck sits at the lower edge, and overhead damage remains spatially attached to fighters.

Frontend page headers share one padded paper strip (`92px`, `64px` compact) with `38px` titles (`28px` compact), so BACK never sits on the strip's corner and Fighter → Stage Select keeps the same height. Compact top-bar text stays at `12px`; the top-bar key hint is a small Paper chip.

The move-key deck is centered by default: four `68px` columns with `8px` gaps, about `304px` in logical UI units (roughly 16% of a 1920-wide viewport at default scale). Its lower margin is `18px`. When the deck would intersect stacked player cards, it moves right and scales to the available width. The 1366×768 and UI140 clearance checks were recorded for the earlier tickets and have not been re-captured for the keys.

Do not rotate dense paragraphs, settings controls, or critical status. Rotation belongs to decorative notes, tape labels, poster groups, and cutout material.

## Elevation & Depth

**Poster layering** is the depth model. SLOPArena does not depend on soft ambient card shadows or glossy glass. Depth comes from a dark field, pasted paper blocks, thick borders, offset title shadows, clipped character portraits, overlapping bursts, and short state translations. At rest, surfaces are mostly flat; physicality appears through hard edges and deliberate misregistration.

### Shadow Vocabulary
- **Offset title shadow:** Hard orange or deep-ink duplicate behind a display declaration; creates poster print depth.
- **Structural bottom edge:** Thick bottom border on primary controls and HUD cards; makes controls feel stamped and pressable.
- **Outline layer:** Deep-ink text outline on damage and combat readouts; protects legibility over active gameplay.
- **Cutout overlap:** Portraits and character imagery may cross panel boundaries to provide depth without a soft shadow.

### Named Rules
**The Hard Edge Rule.** Use borders, offsets, and layers before blur, glow, or ambient shadow. Soft depth is an exception requiring a functional reason.

## Shapes

Corners are square by default (`0px`). Forms are rectangular, assertive, and constructed from borders rather than rounded containers. Use thick outlines, left or bottom accent bars, tape-like labels, circles or bursts as framing devices, and clipped character portraits.

Rotation is authored and restrained: typically under three degrees for grouped content, with larger angles reserved for decorative bursts or notes. Interactive controls use a strong dark border, a heavier bottom edge, and a short press translation. Dense HUD geometry may use tiny offsets and thin dividers, but its alignment remains stable.

Move keys are square physical buttons. Each `64×34` Paper normal tab has a `2px` Ink outline and stands on a `3px` Combat Orange offset plate; each `64×64` Ink special key has a `2px` Paper outline and stands on a `4px` HUD Special Blue plate. Depth carries state: a key sinks flush while its move runs or cools down, refills from the bottom behind a centred countdown, then pops back up. Keyboard special bindings sit on small tilted Paper stickers. Action art fills the face without a separate icon frame.

## Components

### Buttons
- **Character:** Tactile and declarative; a button should feel stamped into the poster.
- **Shape:** Square corners, `2px` dark border, and a `6px` bottom edge on pre-match controls.
- **Primary:** Action Yellow background, Ink text, uppercase bold label; stable interaction geometry is more important than any fixed menu sizing.
- **Main-menu actions:** Training/Solo use paper controls; Online uses Action Yellow. Existing mode order, artwork, and routes remain.
- **Hot:** Host/join primary actions use Action Yellow; Combat Orange remains a gameplay/impact accent, not a competing primary-action field.
- **Hover / Focus (one rule, 2026-10-18):** Yellow means primary action or current selection, never focus. On paper surfaces a focused paper control turns Ink with Paper text and an Action Yellow frame; a yellow control (primary action, selected tab or choice) keeps its yellow and takes a heavier Ink frame. On dark panels (Settings rows, room lists, the Training panel) controls keep their fill and gain an Action Yellow frame, since turning Ink would sink them into the panel. Cards use their frame (yellow for stages and Arcade, white for fighters) and selection adds the strip or check. Nothing moves upward on hover, focus or selection.
- **Active:** Translate downward by about `3px` so press has a physical response.
- **Disabled:** A muted Utility Border fill with Paper text, not reduced opacity; the name mark stays full strength when a guarded page disables Home.
- **Secondary:** Paper field with Ink text; retain the same border and geometry so hierarchy comes from color, not a different control language.

### Modal cards
Frontend MENU, in-match PAUSED, Settings and its confirmations share one card: Paper, a `3px` Ink frame with a `6px` right and `8px` bottom print offset, a left-aligned Archivo title, the yellow primary first (RESUME, then RETRY when offered, SETTINGS, LEAVE MATCH, QUIT GAME) and Paper secondaries. A destructive confirmation (RESET ALL SETTINGS) opens on CANCEL.

### Chips
- **Style:** Flat Ink or Paper strips with uppercase bold text, wide tracking, and a left accent bar when they represent status.
- **State:** Action Yellow indicates action/selection; Online Green indicates live presence; Combat Orange indicates hot or combat state. Add text or structural change so color is not the only cue.

### Cards / Containers
- **Corner Style:** Square.
- **Background:** Ink for broadcast/status panels; Paper for labels and light cutouts; Utility Surface for dense HUD slots.
- **Shadow Strategy:** Hard borders, offset layers, and structural accent edges. No default soft shadow.
- **Border:** `2px` to `4px` Deep Ink, with `5px` to `8px` accent edges for important cards.
- **Internal Padding:** `8px` to `18px` depending on density.

### Inputs / Fields
- **Style:** Rectangular, high-contrast, and compact. Keep the field readable against the panel rather than decorating the input itself.
- **Focus:** Action Yellow border or clear field lift; preserve the same focus treatment as buttons.
- **Error / Disabled:** Use explicit status copy and readable utility colors with inactive structure; do not make required disabled labels legible only through opacity.

### Navigation

Navigation stays stable: the current persistent Training/Solo/Online tabs and flow-specific Back/Leave paths are implementation evidence, not a decorative collage. Preserve predictable routes and active-session guards; this visual direction does not approve a navigation redesign. Labels remain short, uppercase, and explicit.

### Solo Start Stamps

Solo presents one Archivo declaration at a time: a Paper READY strip, square Paper 3/2/1 stamps, then a wider Action Yellow SLOP IT OUT. Use the countdown word and number roles, square HUD Ink borders, and a hard Combat Orange print duplicate offset `3px` down and right. The temporary stamps do not intercept input or veil the arena.

A fixed `480×190` logical anchor starts at 32% of HUD height. Live parent geometry fits the stamps to available space and keeps them clear of player cards. Authored tilt stays static: READY/start −4°, 3 −7°, 2 +4°, and 1 −3°. Each lands over roughly `120ms`, then clears through a `0.16s` fade and `18px` right slide. Reduced flashing removes scale, slide, and fade while retaining tilt, lettering, and beats; pause freezes visible age with the Solo timer. Reused clicks sharpen before the existing heavier start impact, all on the UI mixer bus.

**The Solo Beat Rule.** Let the existing Solo timer own each start beat; fit one crisp stamp clear of player cards, keep its words visible with reduced flashing, and clear the final declaration while play proceeds.

The existing admission delay remains 300 ticks: READY holds for two seconds; 3/2/1 begin at 180/120/60 remaining ticks; SLOP IT OUT begins at zero and clears after `0.72s` while gameplay runs. Native Solo on Log Off is recorded in `.impeccable/review/solo-countdown-20261014/verification.json`, `source-and-captures.json`, and `native-final/`: 22 valid captures across 1920×1080 UI100, 1366×768 UI100/UI140, and reduced flashing show complete lettering and card/deck clearance. The scoped finish review is **ship**, with zero shipping compile/runtime errors. Native audio playback and routing were observed; physical output, connected PvP, and packaged-player behavior remain unverified.

### Solo Finish Poster

Solo closes with SLOP SETTLED. on an Action Yellow declaration and a Paper winner portrait slip. An Ink/Paper outcome strip says YOU WIN! or CPU WINS!; the portrait and name come from the actual Solo result. Shared victory instead uses a Paper DOUBLE K.O.! declaration, SHARED VICTORY, and both existing roster portraits. Shared names use two explicit lines in the shared-name role, a `34px` field, and a `140px` portrait row so the longest FightGuy/FightGuy pairing remains readable. The end headline, outcome, and winner roles keep declaration and identification distinct.

The two-part `640×226` poster has a `166×198` portrait slip, `14px` gap, and square `460×226` declaration. Its `640×240` logical anchor sits at 32% of HUD height; live parent geometry fits and shifts it right to clear player cards, reserving entrance scale and tilted corners. The poster tilts −3° and the slip +4°. The headline's hard orange duplicate sits `3px` down/right. One brief scale/vertical slam settles into a readable hold; reduced flashing keeps the fitted static poster. The ending clears any start stamp, ignores picking, leaves the arena visible, and clears at two seconds. The existing Results coroutine still waits two seconds. The heavier reused impact plays on the UI bus at pitch `0.82`; physical output has not been auditioned.

**The Solo Finish Rule.** Let the existing Solo result own the ending; pair one completion declaration with the factual outcome and winning portrait/name, show both fighters for shared victory, and retain the two-second Results handoff.

Current evidence: `.impeccable/review/solo-end-20261014/verification.json`, `source-and-captures.json`, and `review-fix/`. All six shipping source hashes and 17 capture hashes match. Fifteen labelled native Training presentation fixtures cover outcome branches, 1920×1080/1366×768, UI100/UI140, reduced flashing, all four portraits, and the longest shared names. Two actual Solo captures show ordinary movement causing three natural falls, Shared CPU victory, and settled Results; observed handoff of about `2.35s` includes frontend settling after the unchanged two-second delay. The full review required the shared-name fit correction; `fix-verdict.md` records **ship** for that fix only, rather than a fresh whole-surface review. Shipping compile and console have zero errors; the helper was removed and Editor/settings/config/viewport/prior Results restored. No new rasters were introduced. Natural local win/shared outcomes, physical audio, packaged-player behavior, and connected PvP remain untested.

### Fighter Ground Markers

Four broken Paper arcs with dark edging and a soft contact shadow locate each fighter on the arena floor. A short Action Yellow notch identifies the local fighter; other markers remain neutral. The outline stays small and steady (0.8904 m outer diameter), with no expansion, rotation, pulse, or glow. Its centre stays open beneath the fighter silhouette.

The marker tracks current X/Z over the sampled floor, becoming clearer as capsule feet rise (outline alpha 0.12–0.95 across 0.05–0.75 m). It quiets on landing and disappears without supporting floor or below the sampled platform. This is a ground-position cue, separate from target-lock and aimed-destination feedback. Local Training/Solo fighters receive it alongside other renderers. Native Training evidence for all four fighters at 1920×1080 and 1366×768 is in `.impeccable/review/ground-marker-20261013/verification.json` and `native-final/`; connected PvP and packaged-player rendering remain unverified.

**The Ground Anchor Rule.** Keep a small, steady, open-centre floor cue under each fighter; strengthen its outline as the feet lift, and hide it without supporting floor. A soft contact shadow is a functional depth exception, not a default panel treatment.

### Locked Target Indicator

A compact, flat HUD Ink tag carries the authoritative damage value above the existing red brackets and pointer. The readout has no heavy border or text outline. Bold utility numbers use the target-number role, switching to the smaller wide-number role at 1000 and above; the smaller percent unit sits 3 logical pixels lower with a 2-pixel left gap. Number and unit share the HUD's existing white, orange, and red damage tiers.

The fixed target-lock host keeps its dimensions and anchor when overhead damage is hidden. Its vertical anchor clears the live head plus 0.2 m, capsule top, and all eight projected bounds corners of enabled body, accessory, and weapon meshes. Particles and line/trail renderers do not contribute. Actual panel conversion preserves logical spacing across UI scale; targets behind the camera hide and recover when their projection is valid. Selection and lock mechanics, opacity, and the existing red glyph remain.

**The Target Clearance Rule.** Keep the locked-target host above the highest projected head, capsule, or enabled body/accessory/weapon mesh edge, with a 10 px logical gap below the host. Keep horizontal alignment on the fighter root and preserve the anchor when overhead damage is hidden.

Native Training evidence for all four fighters, idle/hit/airborne poses, shared damage values, both native viewports, UI100/UI140, opacity, and damage-hidden states is in `.impeccable/review/target-ui-20261013/verification.json` and `review-fix/`. The scoped Bonk silhouette-clearance fix received a ship disposition. Connected PvP, packaged-player rendering, and physical controls remain unverified.

### HUD Billboards

Player identity, damage, and lives live in stable left-side fight cards with larger portraits, identity accents, percentage readouts, and explicit stock counts/tickets. Stock loss uses one adjacent KO/OUT stamp per affected card. Training starts with a compact behavior/shortcut readout; pause expands its controls in place, and the session toggle can keep them expanded during play. The locked-target pointer/brackets and shared damage-tier colors remain.

The local move deck exposes all eight moves in four columns, ordered `1/A`, `2/R`, `3/E`, `4/F`. Each column places its normal tab above its special key. The control legend keeps its grounded names and artwork while airborne. Active move availability, cooldowns, and accepted-use feedback still follow simulation state; no selector or caption is added. Missing abilities read UNAVAILABLE. On controller, each tab shows the effective face button once for both halves, and an `LB +` connector left of the special row shows the effective modifier. Shoulder bindings use prominent live LB/RB lettering rather than a padded shoulder sprite. Other controller bindings use available atlas artwork or text fallback. On keyboard, each tab shows the effective normal binding and each special key carries its binding sticker; physical number-row keys retain their digits on AZERTY.

**The Stable-Pair Rule.** Keep all eight controls visible, preserve each normal/special relationship, and keep the same names and artwork through jumps. Physical controls remain adapters rather than a second move identity.

Accepted activations press their artwork for `0.15s`, including repeated activations of the same slot. Cooldowns show seconds and a thin `4px` category-colored bar; completion produces one `0.42s` READY tag. Reduced flashing suppresses scaling/translation while preserving status text. Holding the actual modifier highlights the seam and special names; release restores their normal treatment. Chat input, binding capture, Settings, and pause suppress held-state feedback.

Native evidence lives in `.impeccable/review/paired-hud-20261013/`: `verification.json` and `verify-accepted.log` cover all four kits on ground/in air, pairing, effective rebindings, held/pause guards, repeated activation, cooldown/READY expiry, and name/layout fit. `review-fix-1/` and `review-fix-1.log` contain the fresh six-capture viewport matrix after the live modifier-lettering fix. Virtual-controller checks and controlled four-player specimens over real Training do not establish physical-controller, connected-PvP, or packaged-player acceptance. The HTML plate-reference gate cannot resolve dynamic Unity sprite loads; `.impeccable/build/state.json` remains hero/open and is not a tool pass.

**Wibou identity accent (approved 2026-10-02):** Matrix/hacker green belongs to Wibou's ability artwork and existing sword VFX. Flat icon blades/arcs use the authored trail's base tint, `#91FF69`. Current samples add no HDR glow or code-rain texture and do not change the global menu palette. Normal/special category accents and damage-tier colors keep their functional meanings. Grounded icons read as G1 forward-down, G2 forward-up, G3 vertical-up, and G4 downward hit, using dominant active-hitbox motion only. Inspected samples live in `.impeccable/review/wibou-grounded-blade-icons-20261002/`; runtime binding is not claimed.

**FightGuy ki identity (approved 2026-10-03; direction only):** Physical strikes lead; ki amplifies them. Ki Blue `#39A9FF` owns flat ability icons and the dominant colored body of energy. Light energy `#A8DEFF` supplies inner streaks, hot core `#F0FAFF` stays small and concentrated, and deep edge `#1764D9` defines bright energy without a purple hue. Keep the blue visible rather than washing the effect into white or pale cyan.

FightGuy's energy is compressed, directional, and punchy: Q/Ki Shot has a dense rounded head and a short swept-back tail; melee uses compact tapered arcs following fists and feet; contact uses a brief pressure burst or flattened ring that disappears quickly. Stronger moves gain weight and a stronger core rather than indiscriminate particles or lingering glow. Avoid ice shards, frost, snow-like particles, crystalline facets, lingering clouds, and lightning branches. Flat icons use the identity blue and distinct move silhouettes; glow belongs to VFX. These character-local colors do not replace HUD category frames, damage tiers, or global UI accents. Current ice-derived projectile assets remain unchanged; runtime appearance and arena readability are not yet verified against this direction.

FightGuy special-effect grammar (approved 2026-10-03; revised by visual feedback 2026-10-11):

- **E — Rising Dragon / rise:** Concentrated blue-white energy on the punching fist leads the ascent. A tapered upward streak trails below the fist along its actual motion; a subordinate short takeoff accent may reinforce lift. Contact adds a compact fist burst; whiffs retain the movement trail without contact feedback. The revised pass adds a brief body lift accent, with the fist retaining the strongest core. Avoid a lifting beam.
- **R — Cyclone Kick / rotate:** An upright, open ki spiral and rising wind wrap the legs and torso into a compact moving tornado. The kicking foot owns the strongest streak. Gaps preserve pose readability; no solid tornado cone or lingering cloud. Ribbons travel with him and clear when the spin ends.
- **F — Fist of Fury / strike:** Brief energy wraps and short dense trails alternate with the punch sequence. Actual connections add compact blue-white bursts; misses retain limb accents only. The right-foot finisher receives a heavier accent and one decisive contact burst. The revised pass adds a subordinate soft blue power-up glow through the punch sequence and kick; limb and accepted-contact accents remain strongest.

Blue carries motion; a small white core carries force. Future icon subjects are uppercut, rotating kick, and empowered strike, distinguished by silhouette rather than glow. These remain the acceptance direction for the first implementation; packaged-client and PvP readability still require review.

FightGuy selected VFX starting points (agreed 2026-10-03; first package implementation 2026-10-11):

- **Q / Ki Shot:** Keep the current projectile unchanged. Add `CFXR4 Plasma Shoot (Blue)` at the hands, directed along the shot, at the projectile spawn (tick 8), extended into recovery for 32 ticks; not while aiming and not attached to the traveling projectile.
- **F / Fist of Fury:** Use the compact `CFXR3 Hit Ice A (Air)` adaptation for every FightGuy attack contact, smaller for repeated punches and stronger for the final kick; F also carries a subordinate soft Power Aura glow. Misses retain limb accents without contact bursts. Start from the existing effect; reduce steam, lingering stars, or other layers only if repeated hits obscure the fighters.
- **R / Cyclone Kick:** The first camera-facing ICE spiral read as a slash. The revised pass uses an upright, owned 3D helical ribbon plus `CFX_Tornado_Straight` wind, preserving gaps around the fighter and clearing when the spin ends.
- **E / Rising Dragon:** The fist-led upward trail uses an owned adaptation of `CFXR3 Ice Shield`, retaining the glow and a short trail beneath the punching hand.

The revised implementation adds package-local release, rising-fist, spiral, strike-wrap, empowered-hit, and power-aura prefabs. Q/E/R/F use authoritative timeline cues; all ground/air attack contact bursts require accepted Shared hits and scale with accepted damage. Attached cues clear on attack end, interruption, replacement, or owner teardown. Current F has seven punch windows and one kick; accents follow those windows. Ability Lab captures show all four effects; this is a starting pass for visual review, not final player acceptance.

The source assets are installed under `Assets/~AnimationPacks/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/` and `Assets/JMO Assets/Cartoon FX (legacy)/CFX Prefabs/`; R owns its ribbon mesh/material locally. Ice-labelled sources are acceptable if their resulting motion and appearance serve the ki direction. Verify placement, scale, and timing first; do not blanket-recolor the existing effects upfront. The accepted FightGuy icon review exports are under the workspace's `_bmad-output/fightguy-ability-icons/specials-review/`; their PNGs are installed through the HUD Resources path, including the revised circular-kick R and multistrike F.

**Bonk steel identity (approved 2026-10-03):** Warm steel `#B8B2A8` owns flat ability icons and the main character-local effect direction, distinct from Manki's orange combustion. Pale stone `#EEE8DD` supplies brief impact highlights; dark iron `#67635D` supports definition but is not the primary icon fill. Existing orange model accents may remain small details, not Bonk's identifying color. Favor weight, metal, and physical impact: broad short blade sweeps, dust, and chipped-metal fragments rather than orange energy trails. Global UI, HUD category frames, and damage-tier colors stay unchanged. Icons are installed; model recolor and VFX adaptation remain direction only.

Bonk normal artwork lives under the workspace's `_bmad-output/bonk-ability-icons/normals-review/`: eight ground/air icons use a mesh-derived greatsword silhouette and a dominant cutting-phase arrow, not full looping paths, raw pose kinks, or knockback direction. Accepted specials under `_bmad-output/bonk-ability-icons/specials-review/` depict shoulder bash, leaping sword slam, charging double slash, and blade storm. Repeated blade symbols are motion echoes, not extra equipped weapons or guaranteed contacts.

The accepted 36 transparent PNGs for Wibou, FightGuy, Manki and Bonk remain installed at `Assets/Resources/Icons/{CharacterClass}/{IconName}.png`. Optional `Air/{IconName}.png` artwork overrides the airborne image; otherwise the HUD uses that airborne ability's base icon key. Bonk has four distinct aerial normal icons; the other three characters intentionally share input-slot artwork across ground/air. Textures preserve sRGB/alpha with bilinear filtering, clamp, no mipmaps and no compression. The HUD caches artwork during character setup and changes the image only when needed; authored character colors remain separate from category rules and damage tiers.

Grounded HUD move art uses one locked linocut icon set under `Assets/Resources/UI/AbilityCards/`. Each icon shows the striking limb, weapon or projectile and its effect rather than the whole fighter, in Ink or Paper plus the fighter's accent. Tab icons (normals) are filled solid to read at 30 px; key icons (specials) keep their carving at 56 px. That folder's README records the style template, references and review step; `tools/hud_icon_prep.py` snaps colours, normalises fill and embeds each exact prompt as `impeccable:prompt`. All four fighters' grounded sets are installed. Airborne sprites and original icons remain as authored assets and fallback. Sprites import at a maximum of 256 pixels; source alpha is preserved.

## Do's and Don'ts

### Do:
- **Do** establish the grid before adding crooked poster gestures.
- **Do** use Ink, Paper, Action Yellow, and Combat Orange as semantic roles rather than equal decoration.
- **Do** make focus, selected state, disabled state, and confirmation more obvious than texture.
- **Do** treat character renders as graphic material while preserving face, weapon, and gameplay silhouette.
- **Do** preserve actual model proportions, costume, weapon, signature colors, and chunky forms in splash art; amplification must not promise a glossy cinematic version of another game.
- **Do** keep gameplay HUD texture restrained and information stable while the camera moves.
- **Do** use short, honest, self-aware copy after the useful information.
- **Do** preserve readability for damage, stocks, cooldowns, player identity, and match flow.

### Don't:
- **Don't** use rounded cards, glassmorphism, soft ambient shadows, or glossy gradients as the default language.
- **Don't** rotate dense information, settings controls, or required actions.
- **Don't** let texture, jokes, or character art obscure gameplay state.
- **Don't** use color as the only indicator of player identity, readiness, error, or status.
- **Don't** turn every element into a sticker; one loud gesture is stronger than total disorder.
- **Don't** replace the fighting-game silhouette with generic esports aggression, cyberpunk neon, or meme overload.

Current mini-strip evidence: `.impeccable/review/paired-hud-20261013/mini-fixed/`. The user explicitly rejected airborne artwork/name swapping and requested a much smaller HUD; 136×104 icon pairs replace permanent name fields.
