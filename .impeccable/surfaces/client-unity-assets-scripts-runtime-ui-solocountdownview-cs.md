---
version: 1
slug: "client-unity-assets-scripts-runtime-ui-solocountdownview-cs"
primary_target: "client/Unity/Assets/Scripts/Runtime/UI/SoloCountdownView.cs"
related_targets: ["client/Unity/Assets/Scripts/Runtime/UI/HUDManager.cs", "client/Unity/Assets/Scripts/Runtime/UI/UISFX.cs", "client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs", "client/Unity/Assets/UI/HUD.uxml", "client/Unity/Assets/UI/HUD.uss"]
---

# Solo start stamps

Mode: Operate. Approved narrow extension inside the established SlopArena HUD. The audience enters a Solo fight from stage selection and needs to recognize the start without losing sight of the fighters or controls.

## Direction contract — 2026-10-14

THESIS: Turn Solo's countdown into a brief printed fight-poster ritual while preserving its existing five-second admission delay.
OWN-WORLD: Archivo Black, HUD Paper/Ink, offset orange print registration, Action Yellow on the final SLOP IT OUT stamp.
STORY: READY for two seconds; 3 → 2 → 1 at one-second beats; SLOP IT OUT exactly when the existing timer releases play.
FIRST VIEWPORT: Actual Solo on Log Off at 1920×1080 and 1366×768. Compact crisp stamps sit above the fighters; the final line remains complete, with no player-card or move-deck overlap at UI100/UI140.
FORM: The user-approved fight-poster extension uses Paper strips and square number stamps before a wider yellow start stamp. No concept seed applies to this narrow extension. Code-led; no image comp or shipping raster.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

The approved motion is a roughly 120 ms landing, a hold, and a quick clear. Numeric clicks sharpen with 3/2/1, followed by a heavier start impact. Reduced flashing keeps stable beat lettering; Solo pause freezes the display with the existing match timer. Training, PvP, targeting, character content, gameplay mechanics, the ground cue, and the paired move deck remain outside this change.

## Shipped treatment

READY uses a 268×84 Paper field; numeric stamps use 148×156 Paper fields; SLOP IT OUT uses a 468×106 Action Yellow field. HUD Ink borders are 2 px on READY/numbers and 3 px on the final stamp. Archivo Black is 48 px for READY/start and 122 px for numbers, with a hard Combat Orange print duplicate 3 px down and right. These are live labels with no new raster assets.

The fixed 480×190 logical anchor starts at 32% of HUD height. The implementation reads the live HUD parent layout, retains a percentage fallback for first placement, and fits/repositions stamps clear of player cards. Authored static angles are READY/start −4°, 3 −7°, 2 +4°, and 1 −3°. Normal motion settles exponentially from scale 1.16 and −12 px Y over roughly 120 ms, then exits with a 0.16 s fade and an 18 px right slide. Reduced flashing removes scale/slide/fade while retaining tilt, lettering, and the complete beat order. Pause freezes visible age with the match timer.

The existing 300-tick admission delay owns every beat: READY for two seconds, 3/2/1 at 180/120/60 remaining ticks, and SLOP IT OUT at zero. Shared tick remains zero at every start transition; the final stamp clears after 0.72 s while gameplay proceeds. Solo uses the dedicated HUD view; existing Training/PvP callout routes remain outside the extension.

The dedicated countdown AudioSource reuses the UI mixer bus at volume 0.55. The existing UI click sharpens at READY/3/2/1 pitches 0.72/0.9/1.05/1.22, followed by the existing Bonk heavy impact at pitch 1 and one-shot gain 0.65. Native playback, clip loading, pitches, and routing were observed; physical speaker output was not auditioned.

## Verification and disposition

The finish review is **ship**, with no material findings. All 22 required actual Solo captures on Log Off are valid: 1920×1080 UI100, 1366×768 UI100/UI140, and 1366×768 UI100 with reduced flashing. Complete lettering fits, with no player-card or paired-deck overlap. Recorded transition ticks, pause preservation, reduced-flashing behavior, UI-bus audio, and first READY placement match the source. All six shipping source hashes and 22 capture hashes match. The temporary verification helper and metadata were removed; final shipping compile and native runtime console have zero errors.

Evidence: `.impeccable/review/solo-countdown-20261014/verification.json`, `source-and-captures.json`, `finish-review.md`, and `native-final/`. The bounded rules and tokens are recorded in `DESIGN.md`, `.impeccable/design.json`, and `docs/design/visual-language.md`; PRODUCT.md remains factual context.

Settings returned to UI100/reduced flashing OFF. The Editor is stopped on Frontend with a free, settled lease. Physical audio output, packaged-player behavior, and connected PvP remain untested. The documentation check passed across 313 Markdown files.
