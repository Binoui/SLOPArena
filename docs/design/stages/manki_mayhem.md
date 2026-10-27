---
key: manki_mayhem
display_name: Manki Mayhem
purpose: arcade
players: 2-4
variant: static
competitive_intent: playful
source_scene: client/Unity/Assets/Stages/manki_mayhem/manki_mayhem.unity
arena: data/arenas/manki_mayhem.arena
prefab: client/Unity/Assets/Resources/Stages/manki_mayhem.prefab
---

# Manki Mayhem production contract

Implements the locked [Pirate Broadcast design](manki_mayhem.design.md) and the
readability/authority rules in [Stage Concepts](../stage-concepts.md). It is a
static Mayhem-only Arcade deck with a compiled host origin, play area and deck top.
Shared owns the round loop and SCATTER! hazards (ADR-0044 step 1). Unity runtime
presentation and the Local Play entry follow in step 2.

`tools/blender/manki_mayhem/layout.py` is the approved rectangular deck/spawn shell.
The source scene contains only its collision mesh and empty authoring markers;
`sloparena.stage.bake` supplies Shared/GameServer/client prediction. The cosmetic
Resources prefab is identity-aligned, has no Colliders and owns its complete
local lighting rig. Dressing, StageNeon animation and the empty Manki host anchor
have no gameplay authority.

The procedural [generator](../../../tools/blender/manki_mayhem/README.md) builds the
scene from original meshes/paint and existing roster portraits. It uses the
shared kit and existing Slop Pit crowd primitives, with no new vendor dependency.
Busy booth/screens/galleries frame the neutral, empty concrete deck and calm
recovery void. All background is outside the blast corridor and orbit envelope.

Preflight is the strict layout-to-bake check, `MankiMayhemArenaTests`, the complete
Shared suite, typed stage inspection, mesh/clearance reports, actual gameplay and
named review captures, exposure measurements and a stage-select thumbnail.
Evidence and per-decision conformance live in the design brief's Production and
verification section. Human controller/gamefeel review at two and four players is
pending. The stage stays outside `roster.json` until the maintainer accepts it.

## Second art pass — 2026-10-27

The shell, bake, spawns and collision-source hashes remain unchanged. Presentation
now has orange gallery back walls behind fan/fence silhouettes, layered warehouse
walls/columns/roll-up doors, denser warm bulbs, additive light shafts, analytic CRT
spill and soft recovery-wall material fill. The deck key/fill/material are unchanged;
large top stencils, arrows, tape graphics and continuous top hazard stripes are gone.
Only a faint centre crown/title and small edge ticks remain.

The north scaffold/crate tower has a **20 × 10 m platform at y=6.5** (x −10..10,
z 37.5..47.5). Its **9 × 6.5 m clear host bay** (x −4.5..4.5, z 38.5..45) has 8 m
of headroom; its empty exported `MankiHostAnchor` is **(0, 6.5, 41.5)**. Mic and bomb
pile flank that bay, with tall speaker stacks, front banner and hero floodlights.
Each flanking large screen is surrounded by 26 mismatched small CRTs, including
rolled cabinets, portrait and static feeds.

Final generator: **185,508 / 240,000 triangles**; Unity: **174,420**, 14 renderers,
5 materials, 3 local lights, zero Colliders/missing references/unsupported shaders.
All actual background faces clear blast and camera envelopes; the clear host bay
has zero intersecting tower faces. All **118 stage tests pass** offline with
`SkipUnityPluginCopy=true`. Mesh health is clean.

Evidence starts at `tmp/manki-mayhem-stage/pass2/gameplay/01-show.png`; all prior
view names are retained. `pass2/review/` contains the 17 named review captures;
`pass2/host-framing/01-show-host.png` adds yaw 0 at 8° to frame the future giant.
The original 15° shot is retained for comparison and crops the giant's upper body;
future mode framing needs the lower pitch or wider framing. No gameplay camera
code changed. Gameplay luma is 56.8–94.1 with ≤1.79% clipped; all eight recovery
views pass (30.3–50.7). Only the below-KO warehouse view flags dark (21.5).
The brief's Production section records comparison, limitations and receipts.
Human acceptance remains pending and `status: locked` is unchanged.
