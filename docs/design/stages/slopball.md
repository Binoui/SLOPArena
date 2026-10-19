---
key: slopball
display_name: Slopball Court
purpose: arcade
variant: static
intent: playful
---

# Slopball production contract

Follow the locked [design brief](slopball.design.md): the Night Court, a floodlit
chain-link cage inside a graffiti warehouse. This is a cosmetic pass over the accepted
solo court. Preserve the collision source, baked arena, spawn marker, shot settings and
normal fighter behaviour. The floor, net and shell source meshes keep their asset
identities; the Night Court is separate original presentation.

Collision source: `Assets/Stages/slopball/slopball.unity`. Runtime artifact:
`data/arenas/slopball.arena`. Cosmetic prefab: `Assets/Resources/Stages/slopball.prefab`.
The normal Arcade entry loads this presentation through the existing stage path. The
sender remains the existing `Ball Sender` transform driven by Shared's shot origin, with
its club dressing.

The art comes from [`tools/blender/slopball`](../../../tools/blender/slopball/README.md)
(headless Blender, original geometry and textures, no external packs) and
`sloparena.stage.art-slopball`, which verifies the generator stamp against the source
and arena hashes. Everything a ball can touch lies on a baked plane: dasher-board faces
and chain-link on the walls, the cargo net on the ceiling, the court net in its slab.
The build rejects any other geometry inside the room beyond 2.5 cm of a plane. The
prefab carries no Colliders and no gameplay authority. `SlopballCourtShow` reads
Slopball state and events: the combo board flips to the live combo (capped at 999) and
the floods pop briefly on a return.

Verify source and baked hashes before and after a pass, and inspect the prefab with
`sloparena.stage.inspect` without rebaking. Judge the look from
`sloparena.stage.views.*` captures (`prepare --stage slopball` runs the Arcade scene
with FightGuy and live serves); the camera's pitch is 0–45°, so check the default and
level views. Keep structural evidence and captures in the ignored stage cache. Solo
human visual review remains separate from the normal PvP acceptance process.

## Feel pass — 2026-10-18

The owner found the room good but the ball, hits, scores and misses flat, and the
ball maybe too slow. From a concept board painted over real captures, they took the
recommended set: the mascot ball (A), six beats with sounds, the height cue with a
top-edge chip, and a pace trial. They then took three of the gameplay ideas: every
touch pops (G1), rally heat (G2) and trick-shot callouts (G3); G4–G6 wait. The Shared
rules for G1 and G2 live in the [Arcade brief](../arcade.md#slopball). There is no screen
shake or slow motion.

On 2026-10-19 the owner retired G1's automatic return lob to preserve the squash-room
feel. Hits now keep their direction and angle; wall and ceiling rebounds shape bank
shots, and another attack can rescue a poor shot before it hits the player floor.

- **The ball** wears the mural's face. Its painted front always turns to the camera,
  like the course lanterns, and it changes face by beat: a grin in flight, "uh-oh"
  while it drops toward your floor, a wince through a hit, a laugh when it scores,
  X_X when it lands on your side. It squashes and stretches on a spring and streaks
  coral while fast. The streak heats every five returns (orange, red, pink, white).
  A curveball streaks green, with a longer tail, while it bends.
- **The ground cue** is the fighters' dotted orange drop line from the ball down to a
  yellow ring with a soft contact shadow that darkens as the ball comes down. When
  the ball is above the screen, a chip on the top edge shows it and its height.
- **Serve:** the sender glides to its next origin, three lights count the last
  0.6 s with beeps, then a puff, POP! and a recoil.
- **Hit:** during hitstop the ball flashes white, shivers and winces, with a coral
  comic star, an impact ring and BOP! (weak) or WHAP! (strong, with sparks). A
  rubber kick plays over the fighter's hit sound, pitched by launch speed.
- **Bounces:** the chain-link rattles with a dust puff, and the net and cargo net swish.
  The client reads Shared's exact ball contacts each tick
  (`ServerSimulation.LastTickArcadeBallContacts`) for sound, squash and trick shots.
- **Score:** a SLOP! splat and droplets on the sender floor, the ball laughing into
  it, the running combo popping up beside the word ("7!"), the board's flip clack and the floods
  (unchanged). Each return plays the next note of a C major pentatonic run; every
  fifth adds a chord stab and a bigger pop. Passing your saved best shows NEW BEST!
  on the gold card with a fanfare, once per run.
- **Miss:** the ball pancakes with X_X on a red SPLAT!, with a slide whistle and two
  wood knocks (never a buzzer). A toast says "COMBO 7 DROPPED" with your best.
- **Rally heat (G2):** four pips on the panel fill one per five returns and empty on a
  miss. Each step plays a riser into a bright stab and calls HEAT UP! (MAX HEAT! at
  four) under the combo pop. The floods, beams and key light warm from white through
  amber to pink. The sender's countdown lights take the next serve's colour (amber
  lob, cyan zinger, violet moonball, green curveball), and the cue line names it.
- **Trick shots (G3):** a scoring return calls up to three tricks under the combo pop,
  with a quick run of bells. JUGGLE ×N means three or more hits on that ball.
  SKY BALL, BANK SHOT and NET CORD mean it
  hit the ceiling, a wall or the net after the last touch. The combo still counts one
  per return.

**Contact correction (2026-10-19):** passive fighter bodies no longer bounce the
ball away before an attack can connect, in solo or co-op. Attack/projectile hits
and court bounces still apply. BODY BANK is retired.

`SlopballPresentationBuilder` (`sloparena.presentation.slopball.build`) bakes the
faces, words, splats and effects into `Stages/slopball/Presentation/Ball/` and the
`Resources/Slopball/SlopballPresentation` set; rebuilds keep GUIDs. Word masks
come from `tools/glyph_mask.py` (ArchivoBlack, `Ball/Source/`), the fifteen clips
from [`tools/sfx/slopball_clips.py`](../../../tools/sfx/slopball_clips.py) into
`Resources/Audio/SFX/Slopball/`. `SlopballPresentation` plays the set from Shared
state, hits and Slopball events after each tick; `SlopballMatch` hands it the ball
anchor, and without the set the court keeps the plain ball and ring.
`sloparena.arcade.slopball.run` and `sloparena.photo.live` report its diagnostics
(clips loaded, sounds played, the ball's face, live effects).

**Pace trial (temporary).** The pause menu's PACE button cycles three serve profiles
and restarts the attempt: NOW (the baked profile), BRISK (the same arcs 1.3× faster)
and LINE DRIVE (gravity 10, drag 0.2, launch scale 3 instead of 2.4, 84→66-tick serves on
3.3–4.7 m peaks, which keep the ball on screen). `SlopballPace` builds the variant in memory
and validates it against the court; each pace is a separate Shared profile with its
own bests. After the owner picks, bake the winner through
`SlopArenaSlopballCommands.ApplyShotProfile` and remove the switch.
