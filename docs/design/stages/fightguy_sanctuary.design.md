---
key: fightguy_sanctuary
display_name: FightGuy Sanctuary
purpose: arcade
status: locked
locked: 2026-10-15
approved_by: maintainer
variant: static
scope: phase_5_himalayan_art
blockout:
  - tmp/arcade-phase5/spacing-final-main-bridge-approach.png
  - tmp/arcade-phase5/spacing-final-main-terrace-approach.png
  - tmp/arcade-phase5/spacing-final-main-summit-approach.png
  - tmp/arcade-phase5/spacing-final-main-summit-landing.png
---

# FightGuy Sanctuary layout and first art sample

**Status:** the maintainer approved implementing this greybox layout on
2026-10-15: “go for this design”, prioritizing the overall layout and permitting
target positions to be tuned afterward. This locks the solo implementation
scope; revised in-game layout acceptance remains a separate human review.

The maintainer subsequently selected **A — Cloudtop Sanctuary, with light wear**
for the first R-crossing art sample. Decisions 11–16 define this bounded visual
pass. The selected concept is an art reference, not a replacement geometry source.

After reviewing that sample, the maintainer approved retaining the overall layout
and spreading it **about 25% wider, with varied gaps**. Decisions 17–21 authorize
the spacing revision, including its required collision/marker bake. They supersede
the earlier art-only restriction on changing gameplay geometry for this task.

The initial four-area greybox was authorized through the [Arcade brief](../arcade.md)
and Phase 4 implementation, using the same solo-fixture exception as the
[ball lab](arcade_ball_lab.design.md). The maintainer has now played it and
confirmed that it works, while requesting stronger layout, routing and jump
variety. The winding replacement is implemented and verified with the normal kit;
the [Phase 5 record](../arcade-phase-5.md) contains current spacing and verification
evidence. The Phase 4 record preserves the earlier winding layout's checks.

The maintainer subsequently requested a full mountain-sanctuary art pass with
Tibetan influences, more detail and FightGuy's personality, citing Peaks of
Serenity as a mood reference. Decisions 22–25 extend the visual scope beyond the
first-crossing sample and supersede its greybox-only restrictions. Gameplay
geometry and course rules remain fixed.

## Personality

A serene mountain sanctuary has a very athletic definition of a leisurely walk.

## Confirmed interview decisions

- Art polish follows the layout work.
- Use three or four signature challenges, separated by simpler transitions.
- Wind uphill around the sanctuary, showing later areas above the player, then
  descend over rooftops.
- Reward ability traversal with an obvious quicker route across a major gap;
  the ordinary-jump alternative may require three or four perilous jumps.
- Make the first R crossing obvious and forgiving; tighten timing and landing
  precision later.
- Include one advanced jump → E → R section. Earlier shortcuts focus on one ability.
- Required targets must preserve the route choice. After seeing the sketch,
  the maintainer reconsidered the earlier preference for targets above gaps:
  clearing them must not force players onto the risky branch.

The maintainer approved the three-fork route sketch and the revised shared
target placement for implementation; playable layout review remains ahead.

## Composition decisions

1. **One winding journey around a sanctuary.** In the north-facing starting
   camera, the sanctuary occupies the center background, the first route leaves
   through the left foreground, and later ledges rise across the upper view.
   Propose a clockwise ascent along the west, north and east sides, followed by
   a southeast roof descent into the starting courtyard. The finish has its own
   clearly visible marker in that courtyard. Keep a recognizable landmark in
   view as the player changes sides; no required route passes through its center.
2. **West: broken bridge, distance and R.** From the north-facing approach, a
   broad takeoff fills the foreground, the far landing sits ahead in the center,
   and three or four ordinary jumps bend around the left side of a large hole.
   Jump → R crosses the hole directly. Give this first ability route a broad
   landing and a generous timing window. Required targets sit over the common
   approach and far landing, within ordinary attack reach from solid footing.
3. **North: rising terraces, height and E.** From the east-facing approach, the
   destination is high in the center and the ordinary-jump ledges climb around
   the left side; the sanctuary stays to the right. Jump → E skips the stepped
   climb. Use offset ledges and changing jump heights for the ordinary route,
   instead of repeating the first section's distance gaps. Required targets sit
   over the common upper terrace, reachable after either climb.
4. **East summit: one advanced combined crossing.** From the south-facing
   approach, frame the takeoff in the foreground and the raised destination
   ahead, with a three- or four-jump detour around the left edge of the gap.
   Jump → E → R links height and distance across the center. This is the tightest
   ability landing, with a readable approach and landing surface rather than
   concealed timing. Place required targets over the shared far island, with
   safe footing to clear them. Check that ordinary R aim assistance toward those
   targets still supports the intended landing.
5. **Southeast: rooftop return and speed control.** From the southwest-facing
   descent camera, roofs step down through the center toward the visible finish
   courtyard, with the sanctuary to the right. Alternate descending gaps,
   sideways transfers and a smaller final landing. This supplies a fourth jump
   style after the three major forks; it adds no second combined-ability test.
6. **Required targets occupy shared footing.** Preserve the current 20-target
   scope, provisionally grouped five per area. Distribute them over common
   approaches, landings and connecting surfaces; every target must be clearable
   from solid footing on either route. A player may hit a landing target while
   arriving with R or another airborne attack, or clear it after landing.
   Normal attacks and projectiles remain useful. Traversal risk comes from the
   chosen crossing, with no separate risky excursion to finish its target group.
   Every valid damaging hit still counts; no new target type or bonus rule is added.
7. **Simple links and three saves.** Rejoin each fork on an obvious landing with
   its optional checkpoint, retaining the cumulative 5/10/15 target groups.
   Short transitions supply a breather, a view of the next destination and time
   for normal cooldown recovery. Tune actual travel time so the intended route
   flows without an empty wait for E or R. Every unbroken required target must
   remain reachable after a checkpoint save.
8. **Route cues survive without tips.** From each approach, the takeoff, intended
   landing and ordinary detour must read together. Use functional platform
   silhouettes, simple edge marks and target placement; keep checkpoints and
   the finish visually distinct. Rotate the free camera through quarter turns
   and intermediate angles so a clear forward view does not hide another route.
9. **Busy here / empty here.** Concentrate traversal geometry at the three forks
   and roof descent, with target opportunities on common surfaces. Leave the
   crossing gaps empty and keep the next landing legible beyond each target
   group. The central landmark supports orientation without blocking the next
   destination or masquerading as a shortcut.
10. **Scene world and deferred dressing.** Mountain-sky space surrounds and sits
    below the course, with later route surfaces above the initial courtyard.
    Detailed mountains, architecture and distant scenery remain later art work.
    Any subsequent background must preserve the entire blast corridor; any
    apparently usable sanctuary surface needs matching authoritative geometry.
11. **First sample scope.** Dress the broken-bridge takeoff, common landing and
    three detour poles, plus the central pavilion visible from this crossing.
    The remaining route keeps its current greybox presentation for this sample.
    Collision, target/checkpoint positions and course rules stay fixed.
12. **Stone and timber at the first crossing.** In its north-facing camera,
    warm cream-stone slabs fill the foreground and far center landing. Muted jade
    edge bands frame their existing silhouettes. Lightly worn cedar pole tops
    form the left detour, with small dark iron bands below the walkable tops.
    Wear is sparse and shallow; yellow route marks remain easy to see.
    Trim the hidden courtyard top beneath the overlapping takeoff paving to
    prevent a coplanar visual seam; retain the courtyard's pose and material.
13. **One pavilion landmark.** The center-right sanctuary keeps its established
    massing and roof-deck heights. Cream wall panels, dark cedar framing,
    restrained red panels and terracotta roof trim turn the blocks into a
    mountain training pavilion. Keep all traversable tops flat. Doors are closed
    decorative panels, with no suggested entrance or new route through the solid hall.
14. **A world above the clouds.** Distant faceted peaks form the northern
    background and a few separated side/southern silhouettes, with broad sky
    windows between them. Sparse wind-shaped pines sit on actual distant ridges.
    A pale cloud sea sits below the kill height. Background geometry stays outside
    the complete blast corridor, with no nearby rocks or clouds filling a jump gap.
15. **Clear morning light.** Favor warm light on the stone and wood, cool quiet
    mountain haze and restrained jade/red accents. Distant scenery has lower
    contrast than usable surfaces. Preserve the normal free camera and test the
    first crossing, reverse views and intermediate orbit angles at low pitch.
16. **Original details and verified backdrop assets.** Paving, timber treatment,
    pavilion details, pines and clouds are original authored geometry. Use the
    inspected FantasyForest mountain meshes for distant peaks, with stage-local
    materials and no vendor Colliders. Procurement evidence belongs under
    `.asset-catalog-cache/fightguy-sanctuary-cloudtop/`. Preserve the accepted
    gameplay source, arena and gameplay scene while authoring presentation.
17. **More room around the same journey.** Expand the course footprint by about
    25% in width and depth without scaling the central sanctuary. In the starting
    north-facing camera, the first fork still leaves to the left, the sanctuary
    remains the center/right landmark and the upper route remains visible above.
    Move the outer ledges outward to leave larger sky windows between sections.
18. **A varied ordinary-jump rhythm.** At the west fork, mix a short first hop,
    one visibly longer double-jump transfer and a shorter rejoin. The north climb
    mixes full jumps, a higher double-jump step and a diagonal transfer. The later
    island detour includes longer double-jump gaps; descending roofs alternate
    longer forward and sideways transfers. Keep small, forgiving hops in the
    connecting sections rather than another demanding challenge chain.
19. **Ability routes keep their roles.** Retain the forgiving first R crossing
    and broad shared landing. Move the E takeoff to a clear edge of its wider
    approach, and retain one jump → E → R summit option. Required targets,
    checkpoints and finish follow their shared surfaces, preserving the normal
    route choice, target count, cumulative saves and course rules.
20. **Retain the Cloudtop sample.** Refit the existing first-crossing stone and
    timber treatment to the relocated surfaces. Preserve the pavilion, palette
    and distant backdrop, checking scenery against the expanded blast corridor.
    Other route areas stay greybox; do not regenerate away the accepted art.
21. **Calibrate the new spacing.** Use the real cooked FightGuy kit to distinguish
    short hops, full jumps and double jumps. Verify ordinary and ability-route
    completion, with focused counterexamples for the long double-jump gaps.
    Bake the revised authoritative source and let existing gameplay fingerprints
    archive incompatible older times; do not change fighter movement to fit gaps.
22. **Dress the complete existing course.** Replace the sample presentation with
    an original Himalayan monastery treatment across courtyard, west crossing,
    terraces, summit and roof descent. Preserve all authoritative surfaces,
    markers, jump distances and course rules. Keep flat walkable tops and clear
    open gaps; small ornaments do not supply collision or additional footing.
23. **FightGuy's mountain training home.** Whitewashed walls, maroon friezes,
    dark timber, turquoise accents and gold roof details surround the existing
    central temple mass. Prayer wheels, gongs, worn training posts, a courtyard
    medallion and an original rising-fist banner express his martial discipline.
    The reference supplies mood, never copied game assets or architecture.
24. **Mountain world and air.** Original rocky supports, snow-capped distant
    peaks, sparse pines and remote shrines establish altitude. Background faces
    stay outside the full blast corridor and camera envelope; cloud sea stays
    below death space. Retain morning light, cool distance haze and quiet scenery.
25. **Presentation remains separate.** Original Blender geometry and generated
    textures replace the old sample root; keep the existing checkpoint, finish
    and route cues. Review the real gameplay camera across the full route,
    reverse/intermediate orbit angles and low views. Human visual/gamefeel review
    and player-build acceptance remain separate from scripted verification.

## Negative decisions

- No required E/R input gate; the ordinary-jump alternative remains valid.
- No separate route-exclusive required targets that force shortcut backtracking.
- No required target suspended over a gap that demands an exposed jump to clear it.
- No target placement that depends on enlarged aim ranges or altered R steering.
- No uniform staircase of identical jumps or repeated difficult jump chains in the simple links.
- No empty cooldown waits, added fall penalties or changed respawn rules.
- No moving platforms, rope climbing/swinging, pole attachment or ledge grabs.
- No second hero landmark, route clutter, or background geometry through the blast corridor.
- The original first-sample restriction on dressing the remaining course is
  superseded by decisions 22–25.

## Palette & lighting

The full pass uses warm cream stone, whitewash, maroon, cedar, turquoise and gold,
with sparse light wear. Clear morning light and cool distant haze establish the
cloudtop setting. Gold/red objectives and yellow route marks retain their gameplay
roles. The earlier first-crossing sample palette remains historical guidance;
decisions 22–25 now cover all course areas.

## Camera vantages

- North-facing courtyard: first fork to the left, sanctuary and later climb above.
- North-facing broken-bridge takeoff: both crossings and their common landing.
- East-facing terrace base: height difference and every ordinary-jump foothold.
- South-facing summit takeoff: advanced flight corridor, detour and landing.
- Southwest-facing roof descent: descending destinations and finish courtyard.
- Reverse views at all three checkpoint landings, free-orbit quarter turns,
  intermediate yaw angles and low views around exposed landings.

## Authoring and acceptance

The current source is `Assets/Stages/fightguy_sanctuary/fightguy_sanctuary.unity`,
the runtime artifact is `data/arenas/fightguy_sanctuary.arena`, and presentation is
`Assets/Resources/Stages/fightguy_sanctuary.prefab`. The dedicated gameplay scene
is `Assets/Scenes/Arcade_FightGuyCourse.unity`. These paths contain the wider
winding course and complete Himalayan presentation.
`SlopArenaCharacterCourseCommands` regenerates the approved geometry, matching
presentation and markers through the maintained baker. Its Phase 5 relayout command
updates an existing dressed course without resetting it to greybox.

Gap widths, heights, landing sizes and target coordinates were calibrated
against the real cooked FightGuy kit. Further tuning must check both branches, normal target
assistance and hitstop during traversal, cooldown pacing, checkpoint recovery and
all-target completion. Native input probes provide reachability evidence; human
play owns difficulty, camera readability and feel. The earlier scripted times
are evidence of those inputs, not a prediction of human completion time.
Retain the first-clean-run 1–2 minute goal.

Implement the accepted shell through the
[stage-authoring workflow](../../../skills/sloparena-stage-authoring/SKILL.md),
capture the revised route and collect the human review. The implementation
approval does not claim human acceptance of exact geometry, screenshots or feel.

The wider bake passes 138 focused course/shipping checks, including both full
routes and single-versus-double-jump cases at the long pole transfer. The
referenced captures are agent-reviewed implementation evidence; human review
of the revised playable layout remains pending. See the
[Phase 5 record](../arcade-phase-5.md).

The earlier approved first-crossing art sample was implemented. Its
[Phase 5 record](../arcade-phase-5.md) describes reconstruction, provenance,
and protected-data verification. The subsequent full pass uses the
[Himalayan art source](../../../tools/blender/fightguy_sanctuary/README.md);
human visual review remains separate from automated evidence.
