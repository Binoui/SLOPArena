---
key: slopball
display_name: Slopball Court
purpose: arcade
status: locked
locked: 2026-10-17
approved_by: user
variant: static
scope: night_court_visual_pass
blockout:
  - .stage-authoring-cache/slopball/design/night-court/concept-play.png
  - .stage-authoring-cache/slopball/design/night-court/concept-lob.png
  - .stage-authoring-cache/slopball/design/night-court/concept-wide.png
---

# Slopball Court

The club's look moves to **Night Court**. On 2026-10-17 the owner found the
finished underground club too plain ("the room could be cooler") and picked this
direction from three concept renders made at the real gameplay camera. This is the
approved solo Arcade path: implementation is authorized, and in-game visual
acceptance remains with the owner. The 2026-10-15 club composition is superseded.
The accepted solo gameplay contract below still applies unchanged, as do the
[Arcade brief](../arcade.md) and [delivery plan](../arcade-implementation.md).

## Personality

After closing, the club's regulars turn the warehouse court into a floodlit streetball cage for kicking one ball around.

## Composition decisions

1. From every yaw, the four bounce walls read as a floodlit chain-link cage.
   Low sponsor dasher boards run along the floor edge. Chain-link rises from them to
   the ceiling plane, with steel posts and rails just outside the planes, and a cargo
   net spans the ceiling plane. The fence frames the view into the warehouse and
   carries no landmark.
2. The hero wall stands 7 m behind the cage on the sender side (+Z). From the default
   receive view (yaw 0°, facing +Z), it carries one wide graffiti lettering band, SLOPBALL
   with HIT BALL / FEEL GOOD, low enough to read through the fence above the net and
   sender. Above it, seen when the camera looks level (its orbit never looks up), one
   giant smiling-ball mascot mural in the Slopball poster's halftone-and-splatter style. This is the only hero landmark;
   the old dark club sign retires.
3. The side walls (±X) carry quieter mid-height murals (KEEP IT MOVING with a small
   mascot). Behind the player (−Z), a lounge corner has a SLOP CLUB / OPEN LATE neon
   sign, a couch, a vending machine and string lights. Speaker stacks flank the hero
   wall, a shipping container stands on the east side, and drums and tyres sit in the
   corners. Every prop stands in the gap between cage and hall walls.
4. The court keeps its teal player half and terracotta sender half. They are painted
   darker, like a streetball court on sealed concrete, with off-white lines, a dark
   border band and one logo at the centre of the sender half. The receiving area stays
   free of graphics; the floods' light pools brighten the middle of the court.
5. Six flood panels hang from steel trusses above the cargo net and throw visible light
   cones through a light haze. Above the trusses is the dark warehouse roof; below the
   court, solid ground; at distance, the warehouse walls, never sky.
6. The room reacts to play, as presentation only. A hand-flipped combo board on the
   cage beside the hero wall shows the live combo, the flood panels pop briefly on a
   return, and the speakers pulse gently on their own. These read Shared state and
   events and never decide play.
7. Keep the approved compact sender machine and its friendly face, secondary to the
   ball and driven by the Shared serve origin.

## Negative decisions

- No second hero landmark and no crowd or spectator figures: the warehouse is empty
  after hours.
- Nothing inside the 40 × 40 × 20 room except flush surfaces (at most 2.5 cm proud of
  a bounce plane) and the existing net and sender. The fence, boards, cargo net and
  net sit on the planes the ball bounces from, so no ball visibly passes through
  scenery.
- No geometry that suggests a route; the hall and its props sit outside the camera's
  reach.
- No change to collision, spawn, court size or shot tuning; the source scene and baked
  arena keep their hashes.
- Original generated geometry and textures only; no external packs.
- No screen shake or strobing. The return flash is brief and never fills the frame.
- No high-contrast graphics or texture noise in the receiving area.

## Palette & lighting

Night in a deep blue-black warehouse. Cool white floods light the court, which is
the bright stage. The teal and terracotta halves stay recognisable. Murals and boards
use the poster accents (yellow, coral, teal, cream); sodium orange and magenta neon
are sparse accents in the hall. The live ball stays the brightest saturated object,
with its footprint ring clearly visible.

## Camera vantages

- Default receive camera: normal orbit, yaw 0° facing +Z, about 8 m behind the fighter
  at Z −6, 15° down, 40° vertical FOV.
- Looking level from the same position (pitch 0°, the orbit's upper limit).
- Quarter turns at yaw 90°, 180° and 270°, and the angles in between.
- A wide overview from a high inside corner for the cage, floods and murals.

## Accepted gameplay contract

The following numbered contract was accepted during solo gameplay iteration;
this visual pass does not reopen its tuning:

1. Use one enclosed static room with a flat supported floor, walls and ceiling.
   The second playtest revision reduces the expanded 60×60×30 room by a factor
   of 1.5 to 40×40 units with a 20-unit ceiling. A low 1.1-unit net divides the halves. Ball collision follows the
   baked shell and net; Shared constrains the fighter to the player half.
2. Supply one grounded player start and one empty sender ball marker. All four
   normal cooked fighter kits share one capped shot profile with a fresh random
   session seed and a new launcher position/direction/arc for every serve.
   Launcher origins span X ±8 and Z 5..7 at the existing 2.3-unit ball height;
   the empty sender marker is the center of this authored region.
   Opening serves mix short drops and deeper balls: radius .65, gravity 6,
   138-tick easy flight ramping to 108 ticks, ±30 ticks of arc variation, landing
   X ±6 and Z −14 to −2.5, player Z −6. Linear drag .35 and restitution .8 make
   launches lose speed naturally. Serve velocities compensate for that drag.
3. Show distinct quiet floor halves, a readable net/tape, court outlines and a
   simple launcher that repositions to the next Shared serve origin during the gap.
   Use original primitive meshes, URP materials and local lighting. Add a bright ground ring directly below the live ball.
   A camera-only mirror of baked collision keeps orbit and aim views inside the
   room. When a wall retracts the camera into the local fighter, hide that model
   for this camera's render so the court remains visible.
4. Preserve ordinary orbit and ability-aim cameras. Cosmetics have no Colliders
   and own no gameplay. Stage purpose excludes this court from normal PvP.
5. Floor misses, receive-before-redirect ordering, bank returns, fresh shots and
   uncounted fresh serves belong to Shared. A credited ball must clear the
   net after reception and redirection, then land on the sender floor. Crossing
   alone stays live; a subsequent player-floor landing resets the combo and
   difficulty, then sends another random ball. Fighter position, movement,
   cooldowns and projectiles continue. Show the combo and best as small optional
   feedback; keep the game running without a failure/result screen.


Source: `Assets/Stages/slopball/slopball.unity`. Runtime artifact:
`data/arenas/slopball.arena`. Local presentation:
`Assets/Resources/Stages/slopball.prefab`. Playable scene:
`Assets/Scenes/Arcade_Slopball.unity`.

The source scene, collision meshes and baked arena stay protected through this pass.
Contract item 3's "original primitive meshes" now means original generated geometry
from the Night Court generator; the court halves, net, outlines, ground ring and
launcher behaviour are unchanged. Final visual usability remains a human judgment,
separate from structural checks.
