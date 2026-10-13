---
version: 1
slug: "client-unity-assets-scripts-runtime-combat-targetindicator-cs"
primary_target: "client/Unity/Assets/Scripts/Runtime/Combat/TargetIndicator.cs"
related_targets: ["client/Unity/Assets/Resources/Shaders/FighterGroundMarker.shader", "client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs", "client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs"]
---

# Fighter ground marker

Mode: Operate. Scope: fighter floor-position and airborne depth cues in native Unity gameplay. This extends the incumbent visual world.

## Approved direction — 2026-10-13

The user approved replacing the filled fighter discs with small broken rings and soft contact shadows, while preserving airborne depth perception. The direction is `.impeccable/review/ground-marker-20261013/direction.md`; its `concept.html` is an approved geometric schematic, not native gameplay evidence.

THESIS: Keep a fighter's floor position readable during jumps with a quiet, small, hollow marker.
OWN-WORLD: Paper arcs, dark edging, one short Action Yellow local notch, neutral opponent marks, and a functional soft contact shadow.
STORY: Read the current floor projection beneath the airborne fighter, then let the outline quiet on landing.
FIRST VIEWPORT: Actual gameplay-camera views at 1920×1080 and 1366×768; stage boundaries and fighter silhouettes remain readable.
FORM: Four stable broken arcs with an open centre; no expanding, rotating, pulsing, or glowing outline.

## Shipped artifact

The shader draws a 0.8904 m outer outline on a flat 1.2 m quad, replacing the prior 1.2 m filled disc. Each marker sits 0.025 m above the floor sampled from the authoritative baked ArenaHeightmap at the renderer's current X/Z. It is not a predicted landing point. Invalid or missing floor hides the marker; feet more than 0.1 m below the sampled platform also hide it.

Grounded outline alpha is 0.12. Airborne visibility uses a smooth capsule-foot-height transition from 0.05 to 0.75 m and reaches outline alpha 0.95. The shadow softens with that same transition: normalized radius 0.75–0.92, peak alpha 0.28–0.16. The outline's radius and orientation stay fixed. Soft depth serves spatial reading here, without changing the flat poster-panel language.

The local fighter receives the short yellow notch. Other fighters use neutral Paper. Training/Solo now supplies the local renderer both at setup and roster rebuild; PvP already supplied its renderers. Target-lock and aimed-destination feedback retain their own cues. Existing CharacterState and ArenaHeightmap drive presentation; no gameplay, package, collision, or Shared authority changes are introduced. A single material is shared by an indicator's quads and destroyed on teardown; the quads have no colliders. Native shader geometry requires no new raster assets or provenance.

## Final evidence and limits

`.impeccable/review/ground-marker-20261013/verification.json` records current source hashes and nine actual Unity Training captures in `native-final/`, covering FightGuy, Manki, Wibou, and Bonk, grounded/airborne/early-jump/landing/ledge views, and both native resolutions. The final verification records correct floor placement, missing-floor hiding, neutral NPC accent, material cleanup, no colliders, and zero shader/C#/runtime errors. The final finish-review disposition is ship with no material fixes. The Editor was restored to a clean stopped Frontend, with a free settled lease.

Connected PvP, packaged-player rendering, physical controls, and NPC roster rebuild are not acceptance evidence. The manual Unity follow-up checklist is in the gitignored root `TESTING-UNITY.md`.
