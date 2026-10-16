# Arcade Phase 5: Sanctuary art sample and wider spacing

**Status:** first sample and approved wider spacing implemented — 2026-10-15.
Playable visual acceptance remains with the maintainer.

**Scope:** the maintainer selected **A — Cloudtop Sanctuary, with light wear**
for the first R crossing on 2026-10-15. This is a bounded playable art sample;
the rest of the course and Slopball polish follow its visual review.

The [locked stage brief](stages/fightguy_sanctuary.design.md) owns the composition.
Warm stone paving and jade edge bands dress the takeoff and common landing.
Three cedar-and-iron poles retain the ordinary-jump detour. The central sanctuary
gets cedar framing, closed red panels, stone courses and terracotta roof trim,
while retaining its existing massing and flat traversable tops. Distant peaks,
sparse ridge pines, a cloud sea and morning haze establish the wider setting.
Wear stays light, with restrained surface variation rather than heavy damage.

The original art-only pass kept gameplay fixed. After reviewing it, the maintainer
approved spreading the same winding route about 25% wider with varied gaps. That
revision updates the authoritative source, bake and markers while retaining the
art direction. The courtyard's hidden top patch remains trimmed beneath the
overlapping takeoff paving to prevent flickering. Other route surfaces retain
their greybox presentation.

## Recreate and play

Use **ARCADE → SANCTUARY COURSE → PLAY COURSE** from the normal Frontend.
The [Phase 4 handoff](arcade-phase-4.md#play) describes the existing Editor domain
reload workaround and course controls.

The first sample is rebuilt through **Tools → SLOPArena → Arcade → Phase 5 →
Apply Cloudtop First Crossing**, or through an owned gateway batch:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.course.art-sample
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.stage.inspect --stage fightguy_sanctuary --output .stage-authoring-cache/fightguy_sanctuary/phase5-first-crossing/final/inspection.json
```

The command requires the accepted course prefab and inspected FantasyForest
mountain assets. It replaces only its own generated sample root, the named first
crossing surface treatments, courtyard join and pavilion treatment. It does not
bake. Reconstructing the Phase 4 greybox with `--rebuild true` resets presentation;
apply this sample again afterward.

To apply the approved spacing to an existing prepared course, use **Tools →
SLOPArena → Arcade → Phase 5 → Apply Wider Course Spacing**, or:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity -- sloparena.arcade.course.relayout
```

This updates the named source surfaces, start, targets, saves and finish; refits
the existing presentation; bakes; then refits the first-crossing art if present.
It preserves the central sanctuary's dimensions and the existing gameplay scene.
It requires stopped Edit Mode and saved scenes. Unlike greybox reconstruction,
it retains the accepted presentation sample.

Original detail meshes and stage-local materials are saved under
`Assets/Stages/fightguy_sanctuary/Presentation/`. The Resources presentation prefab
and local art retain the project's existing ignore policy. A code-only checkout
needs the verified asset workset provisioned before recreating this appearance.

## Asset sources

Paving, timber treatment, pavilion details, wind-shaped pines and clouds are
original authored geometry. Only the distant mountain meshes come from JustCreate
FantasyForest: `Mountains_01` and `Mountains_02`, verified through
`.asset-catalog-cache/fightguy-sanctuary-cloudtop/{workset,inspection}.json` and
the inspected report/contact sheet. A bare tree from the probe was rejected and
is not a stage dependency. Imported peak instances have stage-local URP materials
and no Colliders or vendor scripts. See the
[asset source register](../assets/credits-and-licenses.md).

## Human review and next pass

Review the first fork from the normal gameplay camera: whether stone and timber
feel right, both route choices are obvious, the next landing stays visible, and
the pavilion/background support orientation. Rotate through reverse and low
views while jumping. Native and edit-mode checks provide implementation evidence;
the maintainer's playable visual review is still separate.

After that review, carry the accepted material and architectural treatment around
the climb and rooftop descent. Final target positioning and Slopball polish remain
separate follow-up work within Phase 5.

## Wider spacing revision — 2026-10-15

The maintainer approved **about 25% wider, with varied gaps** after playing the
first art sample. The walkable course footprint grows from **62.1 × 61.75 m** to
**77.5 × 77 m**: **24.8% wider and 24.7% deeper**. The central sanctuary stays the
same size. The same three forks wind around it, then four staggered roofs return
to the courtyard. Two additional connecting surfaces provide easy transitions.

The ordinary route now mixes short hops, full jumps, running takeoffs, rising
double jumps and alternating forward/sideways drops. The first pole hop is short;
the second transfer is visibly longer and needs a second jump. The terraces mix
height and diagonal transfers, and the later islands demand longer double jumps.
The first R crossing retains its 12 m empty gap and broad landing. The northern
E shortcut and single summit jump → E → R remain quicker options. All 20 targets,
three cumulative saves and the finish follow their common approaches/landings.

Calibration uses the actual cooked FightGuy kit, with no movement changes. A
flat-ground probe measures approximately 1.81 m of travel for a standing short
hop, 4.03 m for a standing full jump, 5.67 m for a running full jump, and 7.70 m
for a standing double jump. These are specific input traces, not universal jump
limits; route tests use the actual elevations and takeoff positions. Four focused
cases check the long pole transfer: standing and running single jumps fall short,
while both corresponding double jumps land.

Both final routes complete through the normal native Frontend/course/retry flow
with all 20 targets, all three saves and zero falls. Scripted ordinary inputs take
**5,399 ticks (1:29.98)**; the ability route takes **4,754 ticks (1:19.23)**.
Retry clears progress and retains the first best; the quicker finish updates it.
The runs use an isolated records directory, restored afterward. These times are
reproducibility evidence, not human pacing or difficulty acceptance.

The final bake has **336 collision triangles** and one grounded start. Source and
bake hash match at `971bdf6784dbe71d602f7d45c5f03e20a7381b3de2ee3a1d28a85bb527b61db1`.
Stage inspection reports no diagnostics, missing references, unsupported shaders
or cosmetic Colliders. The source/presentation transforms of all four central
sanctuary surfaces and the gameplay-scene hash remain unchanged. The existing
first-crossing art is refitted to its moved surfaces. All 18,242 background
triangles remain outside the expanded blast corridor; cloud top is −31 m, below
kill height −19 m. Course geometry/marker changes use the existing record
compatibility rules to archive earlier-layout times.

The **138 focused Shared course/ArenaShipping tests**, three Server Arcade admission
tests, Shared/Server builds and Unity compilation pass. Agent visual review covers
six inspection views, 24 elevated angles and eight low angles for each of neutral
massing and final dressing, plus native views at the forks, shared landings and
roof descent. Native views provide the actual gameplay color/framing reference.
The connector seams meet without coplanar top overlap. The final native console
has zero errors, temporary review assets are removed, the original stopped
Frontend and Play Mode settings are restored, and the gateway is free/settled.

Current local evidence: `.stage-authoring-cache/fightguy_sanctuary/spacing-final/`,
`.ability-lab-cache/fightguy-sanctuary/spacing/`,
`tmp/arcade-phase5/spacing-final-*.png` and `/tmp/sloparena-course-spacing/`.
Human/controller review of spacing, jump difficulty and route readability remains
pending; no player build is claimed by this local tuning pass.

## Original art sample verification — before the spacing revision

Unity compilation and stage inspection pass, with no missing references,
unsupported shaders or cosmetic Colliders. The source scene, baked arena and
gameplay scene retain their pre-art hashes. All 68 original visual transforms
are unchanged; the surface/material exceptions are the first crossing, its
overlapping courtyard join, pavilion and stage-local atmosphere/light.

A normal-speed native ability route completed all 20 targets and three saves
without falls in **4,180 ticks (1:09.66)**, matching the greybox run. Final
native captures recheck the first R crossing and shared landing after the
surface refinements. No Shared rules or baked geometry changed, so the existing
ordinary-route evidence and gameplay record compatibility remain applicable.

All 18,242 background triangles have bounds disjoint from the blast corridor;
the cloud top remains below kill height. The 17 original ridge pines are placed
against actual mountain triangles using five trunk-footprint support samples.
Agent review covers the six inspection views, a 24-angle elevated sweep and
eight low views of the neutral massing and final dressing. All 16 brief decisions
pass the conformance check. These renders are structural evidence; the native
gameplay views supply the actual in-game color and framing reference.

The final console has zero errors. Temporary review scenes/materials are removed,
the original stopped Frontend and Play Mode options are restored, the verification
record directory is restored, and the gateway is free/settled. Human visual,
physical controller and player-build acceptance are not claimed.

Local evidence: `.stage-authoring-cache/fightguy_sanctuary/phase5-first-crossing/`,
`.ability-lab-cache/fightguy-sanctuary/phase5-first-crossing/` and
`tmp/arcade-phase5/cloudtop-final-*.png`.
