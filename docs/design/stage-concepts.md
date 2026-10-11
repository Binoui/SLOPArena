# SlopArena Stage Concepts

**Status:** living guide
**Scope:** static PVP stage concepts, gameplay readability, and presentation hierarchy
**Implementation workflow:** [Stage authoring skill](../../.omp/skills/sloparena-stage-authoring/SKILL.md)

**Current picker:** [Stage-select roster](stages/roster.md)

## Purpose

A SlopArena stage must be gameplay-ready before it becomes elaborate. It should give fighters clear, authoritative space to move, fight, recover, and respawn, then use art to make that space feel specific rather than empty.

This guide supplies the design intent that each stage brief applies. It does not prescribe measurements, a universal floor shape, platform count, obstacle density, or one perimeter form. The game does not yet have enough PVP stage evidence to make those rules durable.

The current implementation contract remains authoritative: PVP collision, ground, ledges, blast boundaries, and spawns come from baked Shared/server arena data. Unity stage content is presentation only. See the [Stage authoring skill](../../.omp/skills/sloparena-stage-authoring/SKILL.md) for the required source scene, prefab, bake, inspection, and human-review workflow.

## One-sentence definition

> A stage is a clear fighting space with one memorable place wrapped around it.

## The two-part model

Every concept divides into a **stage** and a **background**. They work together, but they have different jobs.

| Part | Job | Priority |
| --- | --- | --- |
| Stage | The playable world: ground, platforms, props, edges, and all space fighters can plausibly use. | Gameplay first |
| Background | The supporting world: landmarks, depth, atmosphere, and scale that sell the premise. | Attention second |

Do not hide an unclear game space behind attractive dressing. If the player cannot quickly tell where they can stand, pass, land, or recover, the concept is not ready.

### Stage: the playable world

The stage is the gameplay promise made visible. Its presentation must line up with its authoritative functional silhouette:

- Any object in playable space that can obstruct movement, support a fighter, or plausibly look traversable needs matching gameplay geometry in the collision authoring scene.
- Match the functional silhouette: the footprint, blocking faces, landable surfaces, and meaningful height. Omit visual detail that does not affect play.
- Do not bake a literal art mesh merely because it exists. Fine ridges, handles, decoration, and invisible complexity do not improve a fight.
- If a visible prop should not affect play, make it clearly too small to read as an obstacle or move it out of the playable space into the background.
- A cosmetic prefab never supplies gameplay collision. The authoritative shell and the presentation prefab stay separate.

This is not a universal “flat floor” rule. Main-floor profile and obstacle placement are stage-specific decisions. A stage may use uneven ground or distributed large props when the result remains readable and intentional in the gameplay camera.

### Background: the world around the fight

The background establishes location, mood, and scale. It should frame the match rather than ask to be watched instead of it:

- Suggest the place with a few intentional landmarks, depth layers, and silhouettes. Do not model an entire room, street, or landscape by default.
- Keep the most contrast, detail, movement, and visual noise away from the fighters and their immediate routes.
- Put background mass at the perimeter or clearly behind the stage plane so it cannot be mistaken for a usable route.
- Use lighting and value contrast to keep fighters, stage edges, and gameplay-significant props legible.
- A strong hero landmark is useful. A background full of equally loud landmarks is not.

Personality comes from the relationship between the fight and the place: scale, a recognizable landmark, an unusual material, a specific time of day, or a joke visible after the fight reads. Personality is not a license to make the play space ambiguous.

## PVP topology principles

### Competitive intent is a spectrum

SlopArena stages do not all pursue the same degree of competitive neutrality. Each stage brief declares its intended position on this spectrum:

| Intent | Topology priority |
| --- | --- |
| **competitive** | Predictability, broadly comparable positional value, clear recovery routes, and low gameplay ambiguity. |
| **mixed** | Stronger terrain features, asymmetry, unusual routes, or positional advantages while remaining suitable for normal PVP. |
| **playful** | A distinctive fighting situation and memorable topology over tournament neutrality, while still satisfying readability, authoritative gameplay, spawn, and basic usability requirements. |

Presentation complexity is independent from this classification. A competitive stage may have an elaborate background, and a playful stage may use visually restrained art.

Stage briefs may use familiar topology descriptions such as `flat arena`, `tri-platform`, `wide arena`, `asymmetric arena`, `central-feature arena`, `compact arena`, or `distributed-obstacle arena`. These are communication shortcuts, not canonical layouts or dimensional templates.

Platforms, additional floors, or other accessible vertical routes should have an intended gameplay purpose or hypothesis recorded in the stage brief.

### One main combat floor by default

The default PVP topology has one main combat floor. It is the primary place for contesting space and exchanging attacks.

Platforms, additional floors, or other accessible vertical routes are allowed only when the stage brief names the gameplay purpose they create: a route, a contest, a positional choice, or another deliberate interaction. Theme alone is not enough. The brief should also make clear why the main floor remains the main fight space.

No current rule fixes the main floor's profile, obstacle count, or a mandatory open center. Those choices belong to the individual stage concept. They are accepted or rejected through the required human PVP review, not guessed from a scene view.

### Props are topology, not set dressing

Large props can make a stage feel like a place and change how a match is played. Treat them as topology when they occupy the stage:

- A crayon, can, eraser, toy, pillar, table edge, or similar oversized object may be a gameplay prop if its functional collision silhouette is intentional.
- Distributed obstacles are valid. Their placement must still leave players able to read routes, opponents, and attack space in the gameplay camera.
- Decorative versions of those objects belong in the background if they have no gameplay shell.
- Do not use cosmetic colliders, disabled physics objects, or decorative animation to create a gameplay exception.

### Perimeters are a stage decision

There is no single mandated PVP perimeter form. Open ledges, walls, enclosures, and other silhouettes are stage-specific choices.

The stage brief must show the intended visual outline so players can understand the shape they are fighting on. The implementation workflow separately validates the baked boundaries, spawn points, and human PVP behavior. Do not use this concepts guide to invent a universal recovery model or an invisible-wall convention.

## Design from play outward

Use this order when forming a new concept:

1. **Fight premise.** State what the main floor, props, and vertical routes make players decide during a match.
2. **Gameplay shell.** Sketch the authoritative playable geometry and visual outline before detailed art. Mark every gameplay-significant prop and accessible surface.
3. **Readability check.** View the shell from the gameplay camera. Confirm that fighters, routes, edges, platforms, and large props read without relying on background detail.
4. **World framing.** Block out land/building volumes, depth layers, supporting foundations, and intentional gaps around the readable shell. Check the full orbit and low recovery views before choosing dressing; then add only props that explain the place. Build the place, then dress it — not asset-pack scenes connected by filler.
5. **Stage brief.** Record the actual topology, traversability, spawn and boundary plan, visual concept, lighting intent, and any verticality exception in `docs/design/stages/<key>.md`.
6. **Implementation and acceptance.** Follow the Stage authoring skill. Structural checks prove the source/baked/presentation relationship; external human PVP review at two and four players judges actual usability.

A beautiful scene view is not proof. The relevant view is the match camera with moving fighters.

## Worked example: children’s-room table

**Premise:** fighters brawl across a child’s desk or table. The scale makes ordinary objects feel like landmarks without requiring a complex arena.

| Layer | Concept |
| --- | --- |
| Main stage | A mostly rectangular tabletop is the main combat floor. Its edge silhouette is visible and deliberate. |
| Gameplay props | An oversized crayon, can, eraser, or toy may sit on the tabletop only when its authoritative shell matches the surfaces that block, support, or redirect fighters. |
| Verticality | A prop becomes an accessible platform only if the stage brief names the route or positional choice it adds. A stack of attractive toys is not enough reason. |
| Background | A partial bed, shelf, lamp, toy box, or wall corner establishes the room beyond the table. These are sparse, depth-separated landmarks, not a fully modeled bedroom. |
| Readability | The tabletop, fighters, and gameplay props remain the clearest elements. Background furniture has quieter detail and contrast, and does not resemble an alternate playable route. |

The concept succeeds when players immediately understand the tabletop fight, then notice that they are tiny in a child’s room.

## Worked example: moored barge and coherent coastal background

**Premise:** a fight on a moored working boat, with a tropical harbor around it. The accepted playable shell stays fixed; this example concerns background construction, not a new boat simulation or universal recovery rule.

The initial failure was disconnected scenery joined by a flat sand sheet, judged from selected screenshots. The improved result came from giving the scenery geography and support, then placing props on that geometry. More assets were not the first step.

| Layer | Geometry and space | Dressing role |
| --- | --- | --- |
| Playable foreground | Retain the accepted deck, closed hull, raised surfaces, spawns, and authoritative boundaries. | Retain accepted boat presentation; background work does not alter the shell. |
| Immediate surroundings | Leave the fight/recovery corridor clear and preserve open water around the boat. | No cosmetic obstacle or apparently safe island in player-death space. |
| Harbor coast | Connect the village, headland, and harbor with real land volume, sloping shores, grounded pads, and submerged foundations. | Houses occupy pads; pier modules meet the harbor edge; ships sit offshore. |
| Separate low islands | Use irregular silhouettes and believable shore profiles, separated by water rather than forced sand bridges. | Sparse palms grounded on the actual island meshes. |
| Outer bay | A distant rocky ridge, low islands, and small reefs vary height and spacing without closing every gap. | Quiet supporting silhouettes, not another equally loud hero landmark. |

Grass, stone, and sand describe the same continuous landform rather than disguise joins between unrelated scenes. Give shores thickness below the waterline and maintain smooth material boundaries. Parent each island's dressing to its region so moving a region does not strand its props.

Check geometry before detail, then repeat after surfacing and dressing: full intermediate-yaw sweep, low recovery views, numeric support/contact checks, actual background-triangle clearance, and preservation of accepted content. A top-down view or one attractive shot cannot prove a 360° scene works.

The `splash_deck` session's actual textured Edit Mode captures demonstrated this presentation approach; [the stage record](stages/splash_deck.md) records the local orbit, grounding, clearance, and preservation evidence. This is not human in-match/PVP acceptance, a performance result, or a new topology/balance standard. Keep those gates separate.


## Learning loop

These are baseline principles, not frozen balance rules. The game is new; revise this guide when repeated **human PVP review** observations expose a durable pattern about readability, crowding, recovery, verticality, or background distraction.

Do not change the guide because one concept wants an exception. Put that exception and its intended gameplay purpose in the stage brief. Update this guide only when the evidence suggests a better default for future stages.

## Current boundaries

- This guide covers current static PVP stages only. Hazards, moving geometry, and other non-static variants remain blocked until they have explicit Shared/server authority.
- It does not replace the stage-authoring asset, bake, inspect, or external-human-review requirements.
- It does not turn Unity physics, colliders, animation, or background dressing into gameplay authority.
- It does not prescribe a specific camera, performance budget, stage capacity subset, or numerical geometry standard.
- Training-only arenas and archived stage workflow documents are reference material, not general PVP topology rules.
