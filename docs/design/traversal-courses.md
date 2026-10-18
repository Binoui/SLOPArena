# Traversal course design and iteration

Use this guide for solo Arcade courses built around a fighter's normal movement
and abilities. It complements [Stage Concepts](stage-concepts.md): readability,
functional silhouettes and background clearance still apply; PvP combat-floor,
spawn-count and multiplayer acceptance criteria do not become course rules.
Implementation follows the [stage-authoring workflow](../../skills/sloparena-stage-authoring/SKILL.md).

The first worked example is [FightGuy Sanctuary](stages/fightguy_sanctuary.design.md),
with implementation evidence in [Phase 4](arcade-phase-4.md) and the
[Phase 5 spacing/art pass](arcade-phase-5.md). Its winding route, three forks,
target count and dimensions are that course's choices, not templates for every map.

The second, Manki's [Kaboom Quarry](stages/manki_quarry.design.md), inverts the target
rule for a zoner: every crate stands on its own island off the route. Jumping out and
punching always works; bombing or rocketing from the route is the faster play (about
51 s against 96 s for the scripted inputs), clusters reward one well-placed blast, and a
single Jump → E face is the traversal shortcut. Its whole shell, markers included, lives
in [`layout.py`](../../tools/blender/manki_quarry/layout.py) and reaches Unity through
`sloparena.stage.shell-apply` (no course-specific Editor code).

## Resolve traversal intent before building

For a new course or a substantial route revision, ask the few unresolved questions
that change geometry. Reuse answers and authorization already in the conversation.
Offer concrete alternatives or a small sketch; do not start another interview for
an already approved numeric adjustment.

| Decision | What it changes |
| --- | --- |
| Journey and orientation | Winding climb, linear gauntlet, branching rooms or loop; what the player sees ahead/above and how the finish relates to the start. |
| Challenge rhythm | Where signature sections sit and where broad landings or simple links give a breather. |
| Route choice | What ordinary movement can do, what an ability shortcut saves, and where the branches rejoin. |
| Difficulty progression | Short hops, full jumps, running takeoffs, double jumps, height/diagonal/descending transfers; when to introduce ability combinations. |
| Objectives and recovery | Required targets on shared footing versus deliberate traversal attacks; checkpoint prerequisites and reachability after a fall. |
| Scale and art scope | Desired room between platforms, protected landmarks, and the portion receiving an art sample. |

Show the approach, ordinary branch, ability branch and common landing together.
Name routes by their purpose, with a top-down sketch and the important gameplay
camera vantages. A playable first draft does not establish that its routing matches
the user's intent.

## Make the route choice real

If the reward is a quicker ability crossing, keep required objectives reachable
after either branch. A target placed only on the detour can erase the shortcut's
benefit; a target requiring the ability path can erase the ordinary alternative.
Shared approaches/landings are one solution. Required attacks above gaps are a
different design choice to confirm, not a default inherited from a target course.

Teach each chosen traversal option on readable geometry before tightening it.
Sanctuary introduces a broad jump → R gap, then height with E, then one combined
jump → E → R crossing. Another fighter may need different abilities and ordering.
Leave travel or useful target work between sections for normal cooldown recovery.
Preserve normal kit commitments, aim/lock assistance and legal fall resets.

Vary the task as well as the distance: release versus hold jump, running takeoff,
second-jump timing, rising landings, diagonals and descending speed control. Keep
simple transitions simpler than the signature challenges. When widening an accepted
course, move sections and tune individual gaps; uniformly scaling the scene also
changes landing sizes, landmark massing and the introductory ability crossing.

## Calibrate with the actual kit

Use the current cooked fighter and baked arena in Shared simulation. Analytic
apex/range estimates are a starting point; JumpSquat, the short-hop decision window,
initial speed, acceleration, air control, capsule clearance, takeoff height and
ability timing affect the usable landing. Follow the
[movement workflow](../../skills/movement-platform-fighter-3d/SKILL.md) and
[testing modes](../testing.md).

For a gap intended to require a double jump, compare an ordinary single jump and
a double jump from a plausible edge takeoff, including a running single jump.
Record the tested setup and margin. One failed trace is not proof that every
possible single-jump input fails. Do not change fighter physics or ability rules
to rescue geometry unless that separate gameplay change is requested.

Verify both complete branches with real inputs against the actual `.arena`, not
fabricated collision approximations. When a course promises that projectiles beat
jumping, prove both: a route that shoots every target and one that reaches each target
on foot without A/R/E (`MankiQuarryRouteTests`). Calibrate legs one at a time from a
teleported start before composing the full route; it surfaces every failing gap in one run. Include all required targets, checkpoint
events, the finish and falls. Observe ordinary braking and takeoff state: coasting
off a small ledge before pressing jump can look like an impossible gap or an ability
bug. Shield braking is a legal diagnostic input, not a new course mechanic.

Keep focused regressions for the route promises being protected. Exact trace
times are evidence for those inputs; do not treat them as optimal records, human
pacing or permanent balance targets. Character tuning may legitimately require
revisiting the course.

## Iterate the changed boundary

| Change | Applicable work |
| --- | --- |
| Gap, platform, spawn or marker tuning | Update authoritative source and matching presentation/markers; bake, inspect and run affected course/shipping coverage. Recheck both routes before handoff. |
| Materials or art only | Preserve authoritative hashes and record compatibility; inspect and review affected gameplay views. No gameplay bake. |
| New backdrop or substantial composition | Check the complete blast corridor, support/placement and full intermediate/low orbit views under the stage workflows. |
| Kit, contact rules or attempt lifecycle | Use the relevant Shared/package verification in addition to the course check; a course pass alone does not cover the changed system. |

Preserve accepted art with targeted edits and a baseline of protected source,
landmark transforms, materials, atmosphere and gameplay scene. Refit the art to
approved moved platforms; do not reset a dressed course through a greybox rebuild.
Move targets, checkpoint sensors/respawns, finish frames and route arrows with
their owning surfaces. Gameplay changes use existing record compatibility rules;
cosmetic changes preserve comparable times.

Prove reachability cheaply in Shared while tuning, then exercise the final route
in Unity. Use the [course replay runner](../testing/course-replay.md) for saved
input traces, named tick/camera captures, result checks, isolated records and
Editor restoration in one bounded gateway batch. It handles the 600-tick native
chunk limit and preserves one-tick actions when splitting steps. Verify normal
Arcade menu entry separately; the runner opens the local course scene directly.
The low-level Sanctuary commands remain documented in
[Phase 4](arcade-phase-4.md#authoring-and-native-commands).

Native input replays are scripted gameplay, not live human play. Temporary probes
belong in ignored caches or a temporary directory. Keep reusable behavior in the
maintained command/test seam; do not make a future session depend on a previous
agent's `/tmp` files. The runner consumes saved inputs; route planning and input
recording/export remain separate capabilities.

## Review layout before extending the art

Review the takeoff, intended landing, detour and next destination from the actual
gameplay camera, including reverse/low views. Global inspection renders may frame
the distant backdrop so widely that the playable course is tiny; use local route
views to assess gaps. Orbit renders prove structure, not gamefeel or performance.

A first art sample on one representative crossing tests materials, scale and
landmark direction before dressing the entire course. If play reveals a spacing
problem, return to geometry while retaining the accepted art direction. A routine
gap adjustment rechecks affected views; it does not automatically restart asset
procurement or the entire concept interview. Repeat broad composition review when
the change actually affects it.

Handoff states what changed, which routes/inputs passed, what protected data stayed
fixed and what human review remains. Leave a short `TESTING-UNITY.md` checklist.
Approval to implement the bounded design, scripted reachability, visual review,
controller/gamefeel acceptance and player-build verification are separate facts.
