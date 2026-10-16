# Visual Presentation Baseline

**Purpose:** Fixed gameplay-camera evidence for comparing later visual work. This baseline records the same match situations across a representative bright and dark arena without changing simulation, combat timing, collision, or presentation behavior.

## Capture contract

- **Camera:** normal gameplay camera from the active match scene; target lock off; default gameplay orbit, pitch, distance, FOV, and follow smoothing; no Scene view, free camera, debug camera, hitbox display, or cinematic framing.
- **Resolution:** capture the rendered game window at 1920×1080; include all on-screen HUD panels and UI elements visible during normal play.
- **Fighters:** FightGuy (P1, blue) versus FightGuy (P2, red), default scale and materials. Use the same character pair for every row.
- **Stages:** `Slop Court` (bright representative arena) and `After Hours` (dark representative arena). If either stage is unavailable in the current build, record the exact substitute and keep it unchanged for the whole capture pass. Slop Court was removed on 2026-10-16: existing `slop_court` evidence is historical, and new passes use `Splash Deck` (`splash_deck`) as the bright substitute.
- **HUD/state:** normal gameplay HUD visible; debug hitboxes, trajectory ribbons, editor gizmos, and console overlays hidden unless a row explicitly says otherwise.
- **Evidence naming:** `visual-baseline/<stage>/<sequence>.png` and, for moving evidence, `visual-baseline/<stage>/<sequence>.mp4`. Stage path keys: `Slop Court` → `slop_court`; `After Hours` → `after_hours`.
- **Metadata:** every image/video must retain the stage, fighters, camera conditions, build/commit, and reproduction steps recorded below. A capture without metadata is not baseline evidence.

**Capture status:** the manifest is ready, but the evidence index remains incomplete until the gameplay captures are taken and linked. Do not treat this document as complete while any row is `_pending_`.

## Dedicated capture scene

Use `client/Unity/Assets/Scenes/VisualBaselineCapture.unity` for the gameplay rows.
It reuses the normal `TrainingMatch` camera, renderer, HUD, and simulation path while
fixing FightGuy vs FightGuy, target lock off, and an idle NPC so the initial spacing is
repeatable. Change only the serialized `TrainingMatch` arena override between
`splash_deck` (bright) and `after_hours` (dark); do not save gameplay state or alter
simulation constants. Capture the results row from the existing `Results` scene after
the normal match flow rather than adding a second results implementation.

Stage art review (not baseline evidence) reuses this scene through the
`sloparena.stage.views.*` commands in the
[stage-authoring workflow](../skills/sloparena-stage-authoring/SKILL.md#procedural-presentation-and-visual-review):
they set the arena override in memory, disable domain reload for Play Mode and
restore the Editor afterwards, so the scene file is never edited.

## Capture matrix

Each row is captured once in each stage (`Slop Court` and `After Hours`). Use the same camera conditions and fighter setup in both stages. For event rows, capture a short sequence (2 seconds before the event and 2 seconds after) and mark the event frame/time in the metadata.

| ID | Situation | Evidence | Reproduction | Expected camera condition |
|---|---|---|---|---|
| VB-01 | Neutral spacing | PNG + 4 s video | Start both fighters grounded and idle, separated by roughly one dash distance; do not press input. | Stable follow; both silhouettes and HUD panels visible. |
| VB-02 | Dash | PNG at peak dash + video | From neutral spacing, dash P1 toward P2, then from P2 toward P1; one pass each direction. Mark the selected pass and event frame. | Keep both fighters framed; no manual camera correction during dash. |
| VB-03 | Jump | PNG near apex + video | From neutral, perform one full jump and return to ground without pressing LMB, RMB, or special attack buttons. | Preserve ground reference and airborne silhouette. |
| VB-04 | Landing | PNG on landing frame + video | Jump from the default starting platform, then land back on that same platform without using fast-fall; finish grounded before the next row. | Show feet, landing surface, and both fighters. |
| VB-05 | Light hit | PNG at contact + video | At low damage, land FightGuy light ground normal g1 once. | Contact remains visible without hiding the victim or HUD. |
| VB-06 | Heavy hit | PNG at contact + video | At low damage, land FightGuy heavy ground normal g4 once. | Brief event remains readable while both fighters stay identifiable. |
| VB-07 | Launch | PNG immediately after launch + video | At approximately 60% victim damage, land a launcher (g3) and let the victim fly. | Keep launch direction, victim, stage edge, and landing route visible. |
| VB-08 | KO | PNG at KO + video | Raise victim damage, land a horizontal finisher (g4), and record the stock-loss/KO moment. | Do not crop the KO direction or stock UI. |
| VB-09 | Respawn | PNG after materialization + video | Complete the KO flow, then capture the victim's respawn before movement input. | Keep respawning fighter, invulnerability read, and remaining fighter visible. |
| VB-10 | Results | PNG + 6 s video | Finish the match after the KO and wait for the results surface. | Normal results framing; record any transition timing. |

## Evidence index

The table must be populated before merge. Paths are repository-relative or attached artifact links.

| ID | Bright: `Slop Court` | Dark: `After Hours` | Build/commit | Notes |
|---|---|---|---|---|
| VB-01 | _pending_ | _pending_ | `73d1fe9` (recorded capture) | The earlier PNG is unavailable in the repository; recover it or repeat the capture. |
| VB-02 | _pending_ | _pending_ | _pending_ | |
| VB-03 | _pending_ | _pending_ | _pending_ | |
| VB-04 | _pending_ | _pending_ | _pending_ | |
| VB-05 | _pending_ | _pending_ | _pending_ | |
| VB-06 | _pending_ | _pending_ | _pending_ | |
| VB-07 | _pending_ | _pending_ | _pending_ | |
| VB-08 | _pending_ | _pending_ | _pending_ | |
| VB-09 | _pending_ | _pending_ | _pending_ | |
| VB-10 | _pending_ | _pending_ | _pending_ | |

## Per-capture metadata template

```text
ID: [VB-01..VB-10]
Evidence path/link: [PNG and video paths or artifact URLs]
Captured at (UTC): [ISO-8601 timestamp]
Build/commit: [build identifier and commit]
Arena + bright/dark classification: [Slop Court/bright or After Hours/dark]
Fighters and player colors: [FightGuy P1 blue / FightGuy P2 red]
Camera: [normal gameplay camera; target lock off; FOV/distance/pitch/yaw]
HUD/debug overlays: [normal HUD; debug overlays off]
Starting positions and damage: [positions and percentages]
Reproduction steps: [exact steps from the matrix]
Event frame/time: [frame count or HH:MM:SS.ms]
Observed presentation notes: [short observation]
Known deviations or blockers: [none or exact deviation]
```

### Captured evidence

#### VB-01 — Slop Court bright

- Evidence: `docs/evidence/visual-baseline/slop_court/vb-01-neutral-spacing.png`
- Availability: this PNG was not committed and is absent from the checkout. Recover it or repeat the capture before using this row as baseline evidence.
- Captured in the Unity gameplay surface at commit `73d1fe9`.
- Arena: Slop Court, bright representative arena.
- Fighters: FightGuy P1 blue and FightGuy P2 red.
- Camera: `MainCamera`, normal gameplay camera, target lock off, default framing.
- HUD/debug overlays: normal HUD visible; debug hitboxes and editor overlays off.
- Reproduction: reset `Arena_Offline`, enter play mode, wait for both fighters to settle on the starting platform, capture the rendered game window.
- Observed: both fighters grounded and visible; HUD panels visible; stage edge and foreground geometry partially occlude the lower center.
- Limitation: this is a still capture only. The remaining matrix rows and the dark-arena counterpart still require gameplay capture.

## Comparison rules

Later visual tickets compare the same ID, stage, fighter pair, camera settings, and event timing. A before/after claim must link both baseline evidence and the new capture. If gameplay setup changes, create a new baseline revision instead of silently replacing these records.
