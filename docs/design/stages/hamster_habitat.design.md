---
key: hamster_habitat
status: accepted
locked: 2026-10-27
accepted: 2026-10-27
approved_by: maintainer
approval_scope: Layout C "Two Cages" (gray-box, footprint 1.3x), see-through glass tubes, and the art direction from concept C "Lamp left on": Table Manners' bedroom at night from the art-desk corner, the red desk lamp left on over the cages, the kid asleep in the bunk bed, a giant hamster peeking in from outside.
blockout:
  - tmp/hamster-habitat/index.html
  - tmp/hamster-habitat-concepts/C_lamp.png
---

# Hamster Habitat: Two Cages

On 2026-10-27 the maintainer asked for PvP maps that challenge the current form ("a bit
safe layout wise") with sections, verticality and hallways, citing Melee's Hyrule
Temple as iconic despite being a poor competitive stage. From four directions they
chose the hamster habitat, and from three layouts on the
[layout board](../../../tmp/hamster-habitat/index.html) they picked **C, Two Cages**
("I like C especially, want to try that layout"). That pick locks this gray-box. The
setting (bedroom, classroom or pet store) and the hamster (peeking in, asleep as a hill,
not home) are still open and decide the art pass. The maintainer judges the gray-box in
game; acceptance is pending.

## Acceptance (2026-10-27)

From the review captures of the Table Manners-matched night room the maintainer said "Very
good, is it on roster yet? We can put it", and the stage joined the picker roster. This was
decided from captures: no in-game or two/four-player review had happened yet, and the camera
inside tubes, bots and frame time remain unchecked in live play.

## Personality

Fighters the size of a hamster's chew toy scrap across two cages and the plastic tubes
between them.

## Fight premise

There is no main floor. Two small cages face each other across open air: the desk cage
(west, higher) and the shelf cage (east, three metres lower). Each is a mini arena with
one feature, the house on the desk and the wheel deck on the shelf. The tubes are the
stage: fights happen inside a hollow tube over the drop, on top of the tubes, and at
their mouths. The low tube's junction hatch lets a fighter pop out above the gap. A
duel starts with one fighter in each cage, so the first exchange is a crossing.
Competitive intent: **playful**. This is the first stage that deliberately breaks Stage
Concepts' one-main-floor default; the tubes are its declared purpose.

## Composition decisions

Gameplay capture yaw names the direction the camera looks: yaw 90° looks east, from the
desk cage toward the shelf cage.

1. **Two cages, one gap.** The desk cage floor (22 × 21 m, y 0) is on the left and the
   shelf cage floor (22 × 21 m, y −3) on the right of the yaw-90° view, with 21 m of open
   air between them. Both trays drop with solid walls to the KO line. After the first
   walk the maintainer asked for a bigger stage ("at least 30%"), so the whole footprint
   is 1.3× the first gray-box; heights, tube bore and the wheel are unchanged.
2. **Desk cage feature: the house.** A solid hamster house in its north-west corner
   whose roof (y 4.4) is a perch, reached by a double jump from the food bowl beside its
   south wall. The high tube starts on that roof.
3. **Shelf cage feature: the wheel deck.** A raised deck (one jump up) in the north half,
   with a static hamster wheel standing on it. Fighters can run inside the wheel's open
   front; it never turns. A cardboard TP roll lies in the shelf cage's south-east corner
   as a short floor-level hallway.
4. **Low tube.** A hollow tube along the south side from desk floor to shelf floor,
   sloping gently down. At mid-gap a junction has a hatch in its roof and a step block
   inside, so a double jump from the step clears the hatch onto the tube's top.
5. **High tube.** A hollow tube from the house roof that arches high over the gap and
   descends onto the wheel deck. It is the long way round and the highest point on the
   map.
6. **Tube tops are routes.** Both tubes are solid outside too, so fighters can stand and
   run on top of them across the gap.
7. **Gray-box presentation.** The prefab shows the same solids as the collision in flat
   colours. The tubes and the wheel are clear plastic (StageGlass) with opaque hoops, so
   the camera and players see fighters inside.

## Art direction (2026-10-27)

The maintainer wanted the bedroom but "we already have that" (Table Manners): the same room
from another angle, or way different. They picked the same room at night, then concept C
("c is amazing", [C_lamp.png](../../../tmp/hamster-habitat-concepts/C_lamp.png)) over a
moonlit and a nightlight take. The hamster peeks in from outside.

8. **Deep bin cages.** The cages are deep blue plastic storage bins filled almost to the
   rim with wood shavings (hamster bin cages). The left bin stands on the kid's white art
   desk, the right one on a lower bookshelf; both furniture tops sit below the KO line.
   Between them the gap drops to the bedroom floor and rug far below.
9. **The lamp left on.** The red desk lamp (Table Manners' design) stands at the back of the
   desk and throws a warm pool over both bins; the rest of the room is deep blue night.
10. **The giant hamster.** A chunky tan-and-white hamster stands on the desk behind the left
    bin, face and paws raised, shiny eyes catching the lamp. It is background, outside the
    blast corridor and the camera's reach, never over a floor, tube or the gap.
11. **The room at night, Table Manners' room.** The maintainer asked how the two stages match
    (2026-10-27); the room now uses Table Manners' scale, walls, window, door, cloud wallpaper,
    toy box, west bookshelf, posters and train, with the bunk bed (the kid asleep) along the
    north wall of the same corner and the craft table with its cake moved south of the bins.
    Glow stars on the ceiling, fairy lights, the kid's drawings on the furniture.
12. **Readability first.** The bin floors, tubes and wheel are the brightest, warmest
    surfaces; the room stays darker and cooler. Check exposure and the dark-stage
    controller readability like After Hours.

## Negative decisions

- No moving parts: the wheel is a static curved surface. A spinning wheel needs moving
  stage geometry, which Shared does not support.
- No floor or safety net in the gap. Falling between the cages is a KO.
- No furniture surface, hamster or prop inside the blast corridor or the camera's reach;
  furniture tops under the bins stay below the KO line.
- Not added to `roster.json` before acceptance. It is walked in Training through the
  Editor launch in `TESTING-UNITY.md`.

## Known gaps for the review

- **Camera in tubes (solved 2026-10-27).** The maintainer wanted see-through tubes ("way
  more fun for everyone"). The art exports the camera's see-through anchor, so the camera
  crosses walls under 0.3 m (tubes, wheel) and stays outside looking in; the TP roll and
  every other solid are thicker and still stop it. Tubes and wheel are clear StageGlass.
- **Bots.** CPUs chase in straight lines without route-finding and will get stuck at tube
  mouths and cage edges.

## Palette & lighting

Night. Key: the warm desk lamp (amber) from the back of the desk over the bins. Fill: cool
moonlight from the window and a deep blue ambient. Accents: glow-star green, the moon's
silver, the lamp's red shade, the pink wheel and craft table. Table Manners' toy palette,
dimmed and blue-shifted.
