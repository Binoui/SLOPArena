---
version: 1
slug: "client-unity-assets-ui-hud-uxml"
primary_target: "client/Unity/Assets/UI/HUD.uxml"
related_targets: ["client/Unity/Assets/UI/HUD.uss","client/Unity/Assets/Scripts/Runtime/UI/HUDManager.cs"]
---

# Paired gameplay move HUD
Mode: Operate. Scope: the local-player ability deck in Unity desktop gameplay.

Approved comp: `.impeccable/mocks/decision/hud-rethink-20261013/move-tickets.png`. User selected “I like the paired move design” and explicitly confirmed all eight moves remain visible. No ground/air selector or caption. Fight-card portraits remain the surrounding identity.

## Direction contract
THESIS: Pair each normal with the special reached by the same controller face button; replace eight independent icon boxes with four continuous tickets.
OWN-WORLD: Paper normals over ink specials, restrained orange normal rule and blue special seam, Archivo names, angular flat card silhouettes, and clear bindings separated from action art.
STORY: Read the button, recognize the normal above, and read its LB special below. Names, artwork and timers follow the active semantic package projection.
FIRST VIEWPORT: Four equal columns at bottom center, ordered normal 1/2/3/4 with special A/R/E/F below; one live modifier cue joins the special tier. Names and action artwork fill the two halves, while the stage stays visible above. Match the approved comp’s roughly 54 percent screen width at default scale.
FORM: Structure 7, paired move tickets, from surface seed 24e2c346. Native bindings, cooldowns, and existing reduced-flashing behavior govern the controls; no gameplay mapping changes.
SIGNATURE: Holding the actual modifier highlights the seam and special tier; accepted semantic activations press their artwork and cooldown completion produces the brief READY tag.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Asset boundary: action artwork is replaceable presentation; core text and controls remain live UI. Preserve all roster characters and airborne semantic names, cooldowns, bindings and distinct existing air artwork. This work does not alter gameplay packages or cooked content.

## Implementation and documentation — 2026-10-13

- The current UXML/USS/HUDManager artifact implements four 248×288 logical angular tickets with 144-unit tiers, 12-unit spacing, Paper normals, Ink specials, orange top rules, and one shared blue seam. The deck is centered at roughly 54% of a 1920-wide viewport; `FitMoveDeck` shifts right and scales when stacked portraits would overlap. All eight moves remain visible with no ground/air selector or caption.
- Move names use live `AbilitySpec` ground/air projection in Archivo Black; long names wrap and use the smaller name role. Effective controller headers are shared by each pair. The modifier connector now renders live 22 px LB/RB shoulder lettering, with glyph/text fallback for other bindings; the plus sign, held yellow seam/names, keyboard tabs, and input guards remain.
- Accepted activation presses the artwork for 0.15 s, repeated same-slot activations retrigger it, cooldowns use seconds and a thin bar, and readiness produces a 0.42 s READY tag. Reduced flashing suppresses artwork scaling/translation while retaining status text. Portrait posters, lives/stock stamps, target markers, and damage-tier meanings are preserved.
- The eight UI-only FightGuy PNGs at `Assets/Resources/UI/AbilityCards/FightGuy/` retain native RGBA alpha. Fully decoded PNG metadata matches every generation manifest's exact prompt in `impeccable:prompt`; all eight raster hashes match the recorded verification manifest. Original roster icons, distinct existing air artwork, gameplay packages, and semantic bindings remain.
- Bounded HUD rules and tokens are recorded in `DESIGN.md`, `.impeccable/design.json`, and `docs/design/visual-language.md`; the one stale HUD sentence in `PRODUCT.md` now describes paired tickets. Other identity, token, release-copy, and character-VFX drift is left untouched.
- `verification.json` and `verify-accepted.log` support four-kit ground/air projection, pairing, remapping, modifier guards, repeated activation, cooldown/READY expiry, and name/layout checks. Fresh native captures after the modifier fix are under `review-fix-1/`, with `review-fix-1.log`, covering genuine Training at 1920×1080 and 1366×768 plus controlled four-player controller/held/UI140/Bonk-air specimens. The final finish verdict is recorded separately after that fix is reviewed. Physical hardware, connected PvP, and packaged-player acceptance remain unclaimed.
- The approved visual-reference board remains the native reference; no standalone chosen-world QUALITY BAR card or independent machine seed file was supplied. Seed `24e2c346` is recorded consistently in the selected comp and direction records. The HTML plate-reference scan cannot resolve `Resources.LoadAll<Sprite>`; `.impeccable/build/state.json` remains hero/open with that native exception disclosed, rather than a claimed tool pass.

## Approved compact follow-up — 2026-10-13

The user requested less screen space, kept the paired design for keyboard and controllers, approved refreshing the other three fighter sets, and corrected FightGuy ground 3 to a high kick. This overrides the initial 54-percent sizing promise: four 208×208 logical tickets now total 880 units (about 46% of 1920), reducing ticket area by 39%. Names use Archivo 18/16 with a 40-unit two-line field; the modifier stays 22. All eight moves remain visible.

Forty new/replaced imagegen illustrations are recorded in `.impeccable/plates/compact-roster-20261013/shipping.json`. The shipping folder contains 47 alpha sprites, with ground/air variants for every fighter, exact embedded prompts, and 256-pixel maximum native import. FightGuy ground 3's authoring name/description changed to High Kick without changing its timeline, animation IDs, or mechanics.

The complete native matrix passed: current source/raster hashes, zero console errors, all four kits' ground/air new sprites, name fitting, remaps, modifier and pause guards, accepted feedback, cooldown/READY, and UI140 portrait clearance. `compact-verification.json` and `compact-final.log` record 17 fresh composited captures: genuine Training for all four plus clearly labelled controlled layout specimens. The Editor and input settings were restored; runtime lease is free and settled. The prior web-gate exception remains disclosed.

## Fixed mini-strip follow-up — 2026-10-13

User explicitly requested no airborne slot/name/artwork changes and less screen area again. The latest contract is a fixed grounded icon legend for both input types: four136×104 pairs,52-unit tiers and8-unit gaps, total576×104, about67%less ticket area than the prior880×208 version. All eight controls remain visible with the shared modifier seam. Permanent name fields are hidden; a small runtime pointer-hover label provides the fixed move name. Active cooldowns, availability and accepted-use feedback continue from simulation state. No gameplay mapping or package changes. Existing artwork, including air variants, is retained as authored assets. Native evidence will be recorded in mini-verification.json and mini-fixed/.

## Move keys follow-up — 2026-10-17

The user found the mini-strip's frame and art unsatisfying and chose option A, "arcade keys", from mockups built on the real stage and assets. Each column is a 64×34 Paper normal tab on a Combat Orange plate (colour option A1) over a 64×64 Ink special key on a HUD Special Blue plate; the deck is about 304 units wide. Depth carries state: a key sinks flush while its move runs (`AttackSlot`) and through its cooldown, refills from the bottom behind a centred countdown, and pops back with a brief Action Yellow edge. The ticket drawing, blue rail, READY tag and keyboard SPECIALS tab are gone. The user accepted the keys in game on 2026-10-17; no native capture was recorded.

Icons: a 16-image style test (cut-paper, sticker, stencil, linocut) was judged inside the keys at real size, and the user chose linocut. All four fighters' grounded sets were installed on 2026-10-17 after a per-fighter review at real size; Bonk's tabs 1 and 4 were regenerated once after a blur test showed look-alikes. The style lock, template and cleanup step live in `Assets/Resources/UI/AbilityCards/README.md` and `tools/hud_icon_prep.py`.
