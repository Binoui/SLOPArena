---
key: slopball
display_name: Slopball Court
purpose: arcade
status: locked
locked: 2026-10-15
approved_by: user
variant: static
scope: underground_club_visual_pass
blockout:
  - .stage-authoring-cache/slopball/design/club/massing-v2/design-north.png
  - .stage-authoring-cache/slopball/design/club/massing-v2/design-east.png
  - .stage-authoring-cache/slopball/design/club/massing-v2/design-south.png
  - .stage-authoring-cache/slopball/design/club/massing-v2/design-west.png
  - .stage-authoring-cache/slopball/design/club/massing-v2/design-top.png
  - .stage-authoring-cache/slopball/design/club/play-default.png
---

# Slopball Court

The user selected an underground sports club: concrete, padded walls, warm
lights and bold court graphics. The user approved the in-game composition and finish; the accepted
solo gameplay contract below continues to apply. It uses the existing
[Arcade brief](../arcade.md) and [delivery plan](../arcade-implementation.md).

## Personality

A scrappy underground sports club built an absurdly large court for kicking one ball around.

## Composition decisions

1. From the default receive view (yaw 0°, facing +Z), use padded bays along the
   lower perimeter to frame the left and right edges, with broad concrete wall
   fields above them. Continue the wall rhythm through yaw 90° (+X), 180° (−Z)
   and 270° (−X), including intermediate orbit views. Keep the center and
   foreground empty for the fighter, ball, footprint and movement.
2. On the sender wall (+Z), make one broad club sign the hero landmark, centered
   behind the sender region. Site the lettering low enough to read within the
   default gameplay frame; the current gray sign is a placement placeholder.
   Other wall bays remain quieter, with restrained service markings rather than
   competing title signs.
   Following the user's request for a more fun look, add a smiling ball mascot
   and a playful motto within this same sign, with sparse painted swooshes in
   existing wall bays. These are surface graphics, not additional landmarks.
3. Across the floor, express a maintained sports surface with clear halves,
   broad but sparse court paint, a clean net and readable edge markings.
   Keep small texture noise and high-contrast graphics away from the ball's
   main receive area. Floor treatment changes presentation, never its height.
4. In raised and low upward views, carry the room with a clear overhead grid
   and warm fixtures aligned with it. Wall and ceiling forms stay on the
   established bounce planes; they must not suggest new obstacles or routes.
   Below the court is a solid club foundation; above is an enclosed service
   ceiling; the distant world is the concrete hall rather than sky or a flat
   panorama.
5. Preserve the moving sender's approved origin and rhythm. Dress its existing
   small silhouette as a compact sports machine during production, with the
   ball exit readable against the back wall. It remains secondary to the ball
   and does not occupy a fighter route. Give its face panel a friendly expression
   and small color accents, retaining the accepted machine silhouette.

## Negative decisions

- No second hero landmark, crowded floor props, spectator figures or visual
  effects competing with the live ball.
- No new collision, platforms, spawn positions, court dimensions or shot tuning.
- No geometry that reads as a traversable route outside the existing shell.
- No decorative intrusion that makes balls visibly pass through large fixtures;
  architectural thickness belongs beyond the established wall/ceiling planes.
- Preserve the accepted original floor, net and shell sources; bind cosmetic
  finishes separately and retain gameplay/record compatibility.
- Use original geometry and existing project resources; no new external pack
  dependencies are part of this pass.

## Palette & lighting

Warm concrete, teal padding with sparse coral and mint bays, richer teal/coral
sports-floor colors, off-white court paint and warm fixtures. The user requested
a more playful finish after accepting the club composition; use a brighter,
welcoming practice atmosphere with broad readable lighting; the ball and its footprint remain bright visual anchors.
Use material depth and sparse wear to suggest a used club, with restrained
contrast behind play. Lighting execution follows this approved direction.

## Camera vantages

- Default receive camera: normal orbit, yaw 0° facing +Z from player Z −6.
- Quarter-turn views: yaw 90°, 180°, 270°, with all intermediate angles checked.
- Elevated interior orbit: every 15°, radius 15, Y 5, look-at (0, 4, 0), FOV 65°.
- Low interior views: every 45°, radius 17, Y 1.2, look-at (0, 3, 0), FOV 70°.
- Supporting roof and diagonal views inspect the ceiling and perimeter joins.

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
`Assets/Resources/Stages/slopball.prefab`. Playable fixture:
`Assets/Scenes/Prototypes/Slopball.unity`.

The source scene, collision meshes and baked arena were hashed before this pass
and remain protected. The visual blockout was approved by the user after the
in-game composition review prompt. Production may now apply the agreed finish.
Final visual usability remains a human judgment, separate from structural checks.
