---
version: 1
slug: "client-unity-assets-scripts-runtime-combat-targetlockindicator-cs"
primary_target: "client/Unity/Assets/Scripts/Runtime/Combat/TargetLockIndicator.cs"
related_targets: ["client/Unity/Assets/UI/HUD.uxml", "client/Unity/Assets/UI/HUD.uss"]
---

# Locked target presentation

Mode: Operate. Scope: the optional locked-target percentage tag and its screen-space clearance in native Unity gameplay. This refines the incumbent visual world. The user approved presentation and anchoring changes while keeping selection and lock mechanics.

## Direction contract — 2026-10-13

THESIS: Make the target percentage compact and readable, with the lock above the presented fighter.
OWN-WORLD: A flat HUD Ink field, bold utility damage number, small percent unit, shared damage-tier colors, and the existing red lock glyph.
STORY: Read which fighter is locked, then its authoritative damage when the optional overhead readout is enabled.
FIRST VIEWPORT: Actual Training gameplay at 1920×1080 and 1366×768; the marker clears body and weapon silhouettes at UI100 and UI140.
FORM: A flat 76×28 logical readout within a fixed 100×58 host; no heavy border or text outline.

The approved direction is `.impeccable/review/target-ui-20261013/direction.md`. No new visual world, seed, comp, raster, or imagery was created.

## Shipped artifact

The readout uses HUD Ink (`#1B1A18`), reducing the previous 88×34 tag to 76×28 logical units. The host reduces from 100×72 to 100×58. Utility numbers are bold 22 px, switching to 18 px at 1000 and above; the percent unit is bold 12 px, offset 3 px down and 2 px from the number. Both labels call the same damage-color function as player cards: white below 40%, orange at 40–89%, and red at 90% and above. The value comes from Shared state.

The head and renderer references are cached when the target changes. Each frame, the vertical anchor takes the highest valid screen projection from the live head plus 0.2 m, capsule top, and eight bounds corners of enabled active MeshRenderer/SkinnedMeshRenderer body, accessory, and weapon geometry. Particle and line/trail renderers are excluded. Horizontal positioning stays on the fighter root to avoid pose/weapon sway. The host bottom sits 10 logical pixels above that projection through the actual panel screen conversion. Missing head/mesh data uses the capsule fallback; behind-camera targets hide and recover once projection is valid.

The existing red brackets and pointer retain their geometry and styles byte-for-byte. Hiding overhead damage removes only the tag; marker bounds and anchor remain fixed. Existing visibility, opacity, and UI-scale preferences remain. Clear/unlock hides the host, and initial 65535% values populate correctly. No Shared, package, map, ground-marker, or paired-deck changes belong to this refinement. Native C#/UI Toolkit geometry requires no raster provenance assets.

## Final evidence and limits

`verification.json` and `source-and-captures.json` under `.impeccable/review/target-ui-20261013/` record matching current source hashes and 15 new actual Training captures in `review-fix/`. They cover FightGuy, Manki, Wibou, and Bonk, idle/hit/airborne poses, damage values 0/40/90/97/100/107% changed through existing Training controls and observed from Shared, both native resolutions, UI100/UI140, opacity20/100, and hidden overhead damage. Presentation edge checks cover behind-camera recovery, missing-head/mesh fallback, initial maximum-value population, and clear/unlock without altering gameplay state. The minimum recorded visible-geometry clearance is 9.346191 logical pixels.

The initial finish reviewer found Bonk's helmet/raised-club overlap despite head-bone clearance. The final scored fix disposition is **ship**, confirming that the bounded mesh-clearance correction resolves that finding at UI100, UI140, and with damage hidden, while retaining the other roster clearances. This disposition records the scoped fix review.

Native compilation and runtime verification recorded zero errors. The Editor returned to a clean stopped Frontend with a free settled lease; restored preferences are overhead damage OFF, UI100, and target opacity100. Connected PvP, packaged-player rendering, and physical controls remain untested. Manual follow-up lives in the gitignored root `TESTING-UNITY.md`.
