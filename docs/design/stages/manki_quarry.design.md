---
key: manki_quarry
display_name: Kaboom Quarry
purpose: arcade
status: locked
locked: 2026-10-18
approved_by: maintainer
approval_scope: "Picks of 2026-10-18: Demolition Quarry setting, lagoon-loop route (flooded pit), explosions hit everything in range (PvP fix), bomb cursor follows the real surface everywhere; implementation of the greybox shell for the owner's play review"
variant: static
blockout:
  - tmp/replays/manki-quarry-01/start.png
  - tmp/replays/manki-quarry-01/pontoons.png
  - tmp/replays/manki-quarry-01/jetpack.png
  - tmp/replays/manki-quarry-01/upper-bench.png
  - tmp/replays/manki-quarry-01/complete.png
---

# Kaboom Quarry — Manki's Character Course

**Status:** locked by the maintainer's picks on 2026-10-18 (one question round). The
first pass implemented the playable shell, course markers and a rough presentation. The
owner then asked for the art pass the same day ("make it Manki, chaotic, fun, a bit
deranged"); it is built and agent-reviewed (see [Art pass](#art-pass-2026-10-18)). Playable
layout, controller difficulty and visual acceptance remain the owner's, as with
[FightGuy Sanctuary](fightguy_sanctuary.design.md).

The owner's brief: give Manki a signature map that shows his personality and uses his
kit. Manki has two projectiles, so targets sit on islands; the islands are reachable by
jumping, but shooting them is better; a shortcut uses E (Jetpack Boost).

The picks: **Demolition Quarry** (over the recommended Firework Cove), the **lagoon
loop**, explosions that hit **every target in range** ("a bug more than a feature",
fixed for PvP too), and the bomb cursor **fixed everywhere** so it follows the real
stage surface.

## Personality

Manki Demolition calls it a quarry; their insurer calls it a crater.

## Composition decisions

1. **One loop round a flooded quarry pit.** From the start camera (north-facing on the
   south loading dock) the pit fills the centre with the crane landmark, the Blast
   Range haul road leaves through the left foreground, and the Blast Face and its
   upper bench rise across the top of the view. The route runs clockwise: south-west,
   west, north, then down the east side to a finish beside the start.
2. **The route moves; the kit breaks.** Every required target is a dynamite crate on
   its own island (rock pillar, cut block, pontoon or raft) off the route. Every island
   can be reached by jumping and has a jump back to the route; shooting it from the
   route with the Round Bomb (A) or the Bazooka (R) is the faster, intended play. No
   target sits on the route itself.
3. **Single crates and clusters.** Lone crates reward a precise shot. Clusters of two
   or three crates inside one blast reward one well-placed explosion (decision 9).
   Near and low islands suit the lobbed bomb (12 m reach); raised and far ones suit
   rockets. Alternate them so the two cooldowns trade off, and let travel between
   groups cover a cooldown rather than an empty wait.
4. **Section 1 — Blast Range (south-west).** A broad gapless haul road from the
   loading dock west to the West Landing. It introduces the bomb (a near low islet),
   the rocket (a raised stack), the blast radius (twin crates) and a far buoy that also
   takes two jumps. Checkpoint 1 on the West Landing.
5. **Section 2 — Pontoon Crossing (west).** Steel pontoons cross the west side of the
   pit north to the North Bench: a short hop, a double-jump gap, a rising diagonal and
   a drop. Crates on a low raft (lob down), a three-crate block (one blast) and a high
   stack (rocket). Checkpoint 2 on the North Bench.
6. **Section 3 — Blast Face (north), the signature fork.** A sheer, freshly blasted
   5.5 m rock face with a scorched launch pad at its foot. Jump → E goes straight up to
   the Upper Bench; E alone and a double jump fall short. The ordinary route climbs
   four stepped quarry benches around the left. All of the section's crates can be
   cleared from the Upper Bench, the common landing, so neither route backtracks.
   Checkpoint 3 on the Upper Bench.
7. **Section 4 — Conveyor Run (east).** Descend from the Upper Bench over the crusher
   roof, conveyor gantries, a gravel hopper and a dump truck to the loading dock, with
   descending forward and sideways transfers. Crates sit on low islets below (dive
   bombing) and one far stack (rocket). The finish is on the loading dock beside the
   start, at the detonator.
8. **One landmark.** A tower crane on the central Last Pillar dangles a comically large
   dynamite bundle over the water. It is visible from every section and is never a
   route. The art pass may blow it up at the finish.
9. **Explosions hit everything in range** (owner, 2026-10-18). A bomb or rocket blast
   hits every crate inside its radius once. This is a Shared combat fix: in PvP the
   same blast hits every fighter in range, including Manki's own rocket jump.
10. **Bomb aim follows the real surface** (owner, 2026-10-18). The ground cursor reads
    the stage collision, so the bomb lands on the raised island or roof under the
    cursor, in the course and on PvP platforms. Client aim only; the bomb's rules are
    unchanged.
11. **Water is death.** The pit's water surface is the kill plane; a fall respawns at
    the last saved checkpoint. Every island stands in open water.
12. **Scene world.** Terraced quarry walls (benches, scree, machines and signs on the
    shore bench) enclose the pit outside the blast corridor, with a dusty sky above and
    turquoise water below (art pass A2, A4, A5).
13. **Greybox first.** The first pass shipped the shell, markers and a rough presentation
    that showed roles by colour and the quarry bowl's massing. The art pass below replaces
    that presentation without touching the shell.

## Negative decisions

- No required target on the route, and none reachable only with an ability.
- No island without a jump back to the route.
- No E or R gate: ordinary jumps and attacks always complete the course.
- No moving platforms, crane-hook swinging or moving conveyors.
- No course-only combat rules: the explosion and cursor fixes apply everywhere.
- No second hero landmark, route clutter, or background geometry in the blast
  corridor; the quarry walls never look like a route.

## Palette & lighting

Dusty daylight: a high warm sun with dust haze. Route surfaces stay cool grey concrete,
granite and painted steel so Manki's burnt orange reads; warm ochre and sandstone live
in the quarry walls and distance. Hazard yellow machines, safety orange accents, rusted
steel, turquoise flooded-quarry water and red dynamite crates with yellow labels. The
crates and the finish keep their gameplay colours.

## Camera vantages

- North-facing start on the loading dock: haul road left, crane centre, Blast Face above.
- North-facing West Landing: the pontoon line and its rafts.
- East-facing North Bench: the Blast Face, launch pad and the benches to the left.
- South-facing Upper Bench: the conveyor descent and the finish below.
- Reverse views at the three checkpoints, quarter turns and intermediate yaws, and low
  views at island edges.

## First pass (2026-10-18)

Implemented: the shell, 18 crates on 15 islands, three saves and the detonator finish,
the rough presentation, the Arcade Course card per fighter, and both kit fixes. Scripted
Manki inputs on the baked arena finish by shooting from the route (51 s), by shooting
with Jump → E (47 s) and by jumping out to every island without A, R or E (96 s); the
Blast Face rejects a double jump and E alone. The Unity replay of the jetpack route
completed with 18 crates, no falls and three saves. The listed captures are agent-reviewed
implementation evidence, not the owner's acceptance; playable layout, difficulty, camera
and visual review remain pending.

## Art pass (2026-10-18)

The owner's brief for the art pass: "make it Manki, chaotic, fun, a bit deranged". Manki
Demolition Co. runs the quarry and has painted, wired and labelled everything in it.
These decisions extend the locked ones above; A2 departs from a negative decision and
needs the owner's verdict.

- **A1. Route pieces are what they are, on their boxes.** Concrete dock and haul road with
  rubber bumpers, pontoons stacked from oil drums and steel pallets, blasted granite
  benches, the crusher shed (BIG CRUSHER, DO NOT HUG), the braced conveyor tower, the orange
  hopper, the dump truck sunk to its axles with googly eyes in its windscreen, a timber
  gravel crib, plank rafts on drums and a striped buoy. Walkable tops are one planar
  ground texture with the route's paint: hazard stripes on every edge that drops into the
  pit, arrows, pontoon numbers, the START box, the jetpack pad (JUMP + E), red crosses
  under every crate, the FINISH box (PUSH THE BIG RED THING), scorch marks, paint splats
  and a chalk outline that says OOPS.
- **A2. Mount Manki (departs from "no second hero landmark").** Manki's own face is cut into
  the north wall and painted by him: spiky orange fur, horns, a red mask with fresh drips,
  one googly eye, one wink, the nose and a grin with the tongue out, his paint roller still
  leaning on it. It sits outside the blast corridor at gameplay height (the camera never
  looks up), grinning over the Blast Face from the jetpack landing and over the dock from
  the finish. If the owner prefers the single landmark, it shrinks into a painted face on
  the wall or goes, with no gameplay effect.
- **A3. The landmark hangs low.** The crane's bundle of three giant dynamite sticks hangs
  at 4.6–9.8 m over open water west of the start's view of Mount Manki, fuse lit and
  sparking, NO SMOKING *EXCEPT FUSES hung on it. The crane's jib and cab (googly-eyed)
  are for the far views; the gameplay camera sees the mast, the cable and the bundle.
- **A4. The yard lives on the shore bench.** The first terrace is low (2.5 m) so the orbit
  camera behind a fighter on an outer island passes over it, and it carries what must
  read: the KABOOM QUARRY sign straight ahead down the haul road and the MANKI DEMOLITION
  CO. billboard behind the finish (both with chasing bulbs), DAYS WITHOUT A KABOOM: 0,
  SAFETY ~~FIRST~~ THIRD, BLAST RADIUS: YES, IT WASN'T ME, monkey-at-work diamonds, an
  excavator holding up a lit stick like a trophy, a TNT water tower with its own fuse,
  drums, tyres, cones, blinking beacons and a fresh blast still smoking.
- **A5. The water is a flooded quarry.** Turquoise, darker in the middle, with lapping foam
  round every piece, the shore and Mount Manki's chin; drums, tyres, planks and hard hats
  float in open water. A rubber duck in an eyepatch bobs right under the dynamite: the
  water's one tall exception, 1.5 m, at least 4 m from any piece and off every jump line.
- **A6. Manki's targets.** Each crate is a red TNT crate with three sticks in its lid and a
  sparking, smoking fuse, too nervous to keep still. A break is Manki's own explosion
  language (flash, comic POW, fireballs, smoke, sparks, planks flying) and his winking
  grin rising out of the smoke. Checkpoint frames are hazard tape (red when locked),
  missed crates get red flares, the detonator lights an orange beam and the finish is the
  biggest KABOOM yet, with hazard-tape confetti and a huge grin.
- **A7. It sounds deranged.** Every crate goes boom and honks a clown horn that climbs the
  pentatonic scale through a section; a section ends in a bike-horn fanfare, a checkpoint
  clicks and dings, missing crates plays the sad trombone, the last crate lights a fuse
  under a slide whistle before a big boom and an air horn, and the plunger sets off the
  big one at the finish.
- **A8. Paint without backgrounds.** Eyes, the grin and graffiti on rock are geometry, so no
  sticker rectangle shows on rock or glass; atlas art goes on boards, posters and stickers
  whose rectangle is part of the joke.

Agent review used the course replay runner (`tmp/replays/manki-quarry-04` to `-06`: start,
haul road, pontoons, Blast Face, Mount Manki, conveyor, crate breaks, checkpoint save and
finish). The replay still completes with 18 crates, no falls and three saves; captures clip
3–10% after the concrete, foam and walls were toned down. These are implementation
evidence, not the owner's acceptance.

## Authoring and acceptance

The shell lives as a `SPEC` in [`tools/blender/manki_quarry/layout.py`](../../../tools/blender/manki_quarry/layout.py)
and reaches the source scene through `sloparena.stage.shell-apply`, then the normal bake.
The [production contract](manki_quarry.md) names the artifacts and checks. Scripted
Shared routes prove reachability with the real cooked Manki kit; they are not human
difficulty, camera or feel acceptance, which the owner gives after playing.
