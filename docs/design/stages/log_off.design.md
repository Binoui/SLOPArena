---
key: log_off
status: locked
locked: 2026-10-11
approved_by: maintainer
approval_scope: rendered footprint and woodland composition; continue implementation and add background detail
runtime_camera_review: pending
blockout:
  - .stage-authoring-cache/log_off/design/design-top.png
  - .stage-authoring-cache/log_off/design/design-north.png
  - .stage-authoring-cache/log_off/design/design-east.png
  - .stage-authoring-cache/log_off/design/design-south.png
  - .stage-authoring-cache/log_off/design/design-west.png
---

# Log Off — split-stump design

The maintainer selected the concept, reviewed the native blockout views, and
approved continuing with “very good keep going” on 2026-10-11. This locks the
rendered footprint and proposed woodland composition for implementation. The
maintainer's instruction authorizes that next step while the human in-game camera
and PVP reviews remain pending; screenshot approval does not claim those reviews
passed. The saved gray study is
`Assets/StageDesignPreviews/log_off/LogOffBlockout.unity`.

## Personality

Tiny fighters turn an ancient split tree stump into a woodland chase playground.

## Fight premise

One broad, continuous floor curves around a split opening from its northern edge.
Cross the split with a running full jump to intercept or escape; stay grounded and
chase around its southern tip to avoid committing to an aerial crossing. The floor
around the tip remains the main combat space, with room for all four fighters.

This is a static, playful PVP direction for 2–4 players. The first study has no
additional platforms: its route choice must justify the map by itself.

## Composition decisions

1. **Main stump:** from yaw 0° (camera on +Z, looking south), the two broad shoulders
   occupy the left and right thirds. The split is visible through the center; the
   connected fighting floor beyond its tip remains broad. A roughly 32-unit,
   slightly irregular outline gives the stump a recognizable organic silhouette.
2. **Shortcut:** the split opens toward +Z and narrows toward a tip slightly south
   of the origin. At Z=6 the opening is 3 units wide. That is the initial crossing
   to tune; the landing shoulders remain clear. At yaw 180° the ground route is
   in the foreground, with the two shoulders and split behind it.
3. **Ground route and spawns:** from yaw 90° or 270°, the split reads as a notch,
   not a disconnected island. The lower half of the top stays uninterrupted.
   Four inward-facing spawn ideas occupy X=±7, Z=±5; none starts in the split or
   immediately next to its lips. Gray capsules in the preview scene show scale
   only and are absent from the stage prefab.
4. **Stump depth:** the sides and split walls continue from the floor at Y=0 to
   Y=-10. Low views must show a solid trunk and an open vertical split, not a
   thin floating plate, invisible wall, or apparent under-floor shortcut. These
   are proposed shell dimensions; production must bake and verify them.
5. **Later woodland:** after the route study, compose a warm forest clearing
   around the stump. From yaw 0°, a distant cluster of tall trees should frame
   the left third, with a lower, sparser group on the right and open sky between.
   Yaw 180° should offer a quieter clearing, rather than an equally dense ring of
   trees. Side views need separate, grounded silhouettes and deliberate gaps.
   The first blockout does not add this scenery.
6. **Scene world:** the stump belongs to an enormous tree rooted in forest ground
   at or below the future lower death plane. Distant terrain and tree volumes
   must stay outside the future fight/recovery corridor. Above it, open daylight
   and separated canopy silhouettes sell scale. Do not place a cosmetic floor
   underneath the split at a height where a player expects to survive.
7. **Background detail:** the maintainer approved the woodland first pass and
   requested more background detail on 2026-10-11. Add low understory and grass
   around the existing grove feet, a few fallen logs and small flower/mushroom
   patches, then gentle hills and separated tree silhouettes in the distance.
   Give the tall left grove the richest dressing; the opposite clearing stays
   quieter, with clusters separated by open ground. Ground every addition on
   its actual host. Preserve the stump, lighting intent, routes and recovery
   space; all elevated background bounds stay beyond the side blast planes.

**Busy here / empty here:** the split and its two lips carry the route decision.
Keep the connected southern floor, landing shoulders, and all immediate recovery
space empty. Later woodland detail belongs in distant clusters, separated by
quieter ground and sky.

## Negative decisions

- No extra platform in the first blockout.
- No raised bark rim, stump clutter, or decorative obstacle on a route.
- No bridge filling the split; it must remain a real opening in the proposed shell.
- No moving geometry, wind, bounce pads, hazards, or automatic ledge rescue.
- No second hero landmark or uniformly dense ring of background trees.
- No background geometry through the blast corridor or false safe surface below
  the split.
- Preserve the approved footprint and 3-unit crossing when adding presentation
  and baking gameplay collision.

## Palette & lighting

Production direction: warm afternoon daylight, honey-brown cut wood, darker chunky
bark, and restrained mossy greens. The fight floor remains clearer than the
surrounding woodland. The current blockout uses one neutral gray material and one
study light; its renders judge shape and scale.

## Camera vantages

- Yaw 0°: look into the split and compare both shoulders.
- Yaw 90° and 270°: check notch depth, landing space, and solid side walls.
- Yaw 180°: confirm the broad connected ground route remains readable.
- Elevated orbit: every 15° around the stump.
- Low recovery views: every 45°, including a direct look into the split.
- Top view: proposed footprint, four spawn ideas, and the continuous ground route.

## Movement basis and review

The existing Shared `MovementProbe`, run against the current cooked roster on a
flat probe arena, gives minimum running full-jump travel of approximately 5.67
units. The proposed 3-unit crossing leaves room for a takeoff and landing margin.
This is a sizing reference, not proof of a successful crossing on a baked stump.
Receipts are under `.stage-authoring-cache/log_off/design/movement/`.

The persisted original mesh contains 124 triangles. The geometry study checks its
closed volume, exact top-area triangulation, empty split, connected southern floor,
and four spawn ideas on that floor. Native camera review covers 24 elevated orbit
views, eight low views, and three top/route close-ups in
`.ability-lab-cache/log-off-design-20261011/pass-01/`.

The initial typed design-capture command wrote the five front-matter gray views
and reported `ARENA_MISSING`, as expected before baking. Its displayed blast planes
were estimates from the visual bounds. The subsequent playable implementation is
recorded in [Log Off](log_off.md); its baked arena now owns the actual boundaries.

The rendered blockout approval carries forward into stage authoring. The next
checks use the actual baked shell and Training camera; human two/four-player PVP
acceptance remains separate and pending. Preserve the approved shape when
replacing the gray study with art.

## Woodland asset roles

Use the bundle's forest assets for distant scenery only. Inspect three tree
silhouettes and two rocks as a procurement shortlist, then place only what serves
the composition. Tree hosts are grounded at Y=-10 in uneven distant groves, with
their full bounds beyond the side blast planes and immediate recovery space.
Allow roughly 12–25 units of tree height and separate canopy silhouettes; the
right grove is lower and sparser than the left one. Rocks belong to those
distant ground hosts, below the canopies. Original ground and stump surfaces stay
stage-authored. No inspected asset gains gameplay collision.

The background detail pass reuses those accepted trees and rocks. Missing roles
are low leafy plants (roughly 0.5–2 units high), grass clumps (under 1.5 units),
fallen logs (roughly 3–7 units long and under 2 units high), and small flowers.
These occupy irregular ground patches beside the distant groves, outside the
27-unit guard square. Inspect the existing Fantasy Forest pack for those roles.
Original mushroom clusters and terrain volumes stay stage-authored; the hills
begin beyond the existing grove feet and remain quiet supporting silhouettes.
