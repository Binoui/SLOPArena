---
version: 1
slug: "client-unity-assets-scripts-runtime-ui-soloendcardview-cs"
primary_target: "client/Unity/Assets/Scripts/Runtime/UI/SoloEndCardView.cs"
related_targets: ["client/Unity/Assets/Scripts/Runtime/UI/HUDManager.cs", "client/Unity/Assets/Scripts/Runtime/UI/UISFX.cs", "client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs", "client/Unity/Assets/UI/HUD.uxml", "client/Unity/Assets/UI/HUD.uss"]
---

# Solo finish poster

Mode: Operate. A bounded extension of SlopArena's approved fight-poster HUD. User request: “Good. What about the end of game text ? Can make it more memorable”. Keep the outcome factual and the existing two-second transition to Results.

## Direction contract — 2026-10-14

THESIS: Close the fight with a recognisable winner poster carrying SLOP SETTLED., the actual outcome, and the winning fighter.
OWN-WORLD: Archivo Black, Action Yellow/HUD Ink declaration, Paper portrait slip, orange print registration. Reuse existing roster portraits, not new illustrations.
STORY: SLOP SETTLED. marks completion; YOU WIN! or CPU WINS! and the winner portrait/name identify who won. DOUBLE K.O.! retains the existing shared-victory meaning and shows both fighters.
FIRST VIEWPORT: A compact two-part poster above the fighters, clear of player cards and the move deck, at 1920×1080/1366×768 and UI100/UI140. Actual scene stays visible.
FORM: Code-led narrow extension in the established world; no comp or concept seed. One brief slam, a readable hold, then Results at its original delay. No new raster assets.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Reduced flashing preserves stable lettering. Clear a still-visible start stamp before showing the ending. Outcome mapping uses the existing Solo result; no gameplay, win-rule, Results-page, PvP, or stage changes. Native presentation fixtures must be labelled as previews; distinguish them from naturally observed Solo outcomes.

## Implemented result

`SoloEndCardView` consumes the existing Solo result via `TrainingMatch` and `HUDManager`; it clears the start stamp and presents the winning roster portrait/name beside SLOP SETTLED. and YOU WIN! or CPU WINS!. Shared victory retains its existing meaning with DOUBLE K.O.!, SHARED VICTORY, both portraits, and two explicit full-name lines. Only Solo end presentation changes; Results, PvP, Shared gameplay, and the map remain outside scope.

The 640×226 two-part poster uses a Paper 166×198 slip and a 460×226 Action Yellow declaration separated by 14px, Archivo roles at 68/26/22px, and a 14px shared-name role inside a 34px field below a 140px portrait row. Its live 32%-height anchor fits and shifts clear of player cards and leaves the scene visible. Poster/slip tilts are −3°/+4°, with an orange 3px print duplicate. One brief slam settles into a readable hold; reduced flashing retains the fitted static poster. The original two-second Results coroutine remains; the heavier existing sound uses UI bus pitch 0.82. No new rasters were generated; approved FightGuy, Manki, Wibou, and Bonk portraits are reused.

## Evidence and review

- Latest source/capture receipts: `.impeccable/review/solo-end-20261014/verification.json` and `source-and-captures.json`. All six shipping source hashes and all 17 `review-fix/` PNG hashes were checked and match. Earlier receipts remain historical.
- Fifteen native Training previews are explicitly labelled UI fixtures, covering win/CPU/shared, 1920×1080 and 1366×768, UI100/UI140, reduced flashing, all four portraits, and longest FightGuy/FightGuy shared names. These do not establish natural local win/shared outcomes.
- Two actual Solo captures from the third natural loss run show ordinary movement, three natural falls, Shared CPU win, and settled Results. The existing delay is 2f; observed handoff ~2.35s includes frontend settling. Results was captured after its scheduled reveal settled.
- The full review's disposition was fix for shared-name overflow only. The corrected two-line names fit the 34px field while both portraits remain visible. `fix-verdict.md` records ship for that fix list only, not a fresh whole-surface review. No native Unity detector was available; the craft floor was reviewed.
- Shipping compile and console have zero errors. The temporary helper was removed; stopped Frontend, free settled lease, preferences, match config, viewport, and prior Results were restored.

## Finish and limits

The bounded implementation, native verification, scored fix review, and documentation handoff are complete. `DESIGN.md`, `.impeccable/design.json`, and `docs/design/visual-language.md` add only the Solo end roles/component/rule. The existing fight cards, eight-move compact paired deck, fixed grounded artwork, countdown, ground cue, and targeting decisions remain preserved. No new raster provenance is required.

Native UI-bus playback and pitch 0.82 were observed; physical output was not auditioned. Natural local win/shared outcomes, packaged-player behavior, and connected PvP remain untested.
