---
key: training
status: accepted
locked: 2026-10-29
accepted: 2026-10-29
approved_by: maintainer
approval_scope: round 2, options 1 + 2 combined from the 2026-10-29 layout board ("id like 1 + 2 so i can test out platforms and recovery but still have a more interesting stage"), on top of round 1's setting A "Crash-Test Lab"; pick-based lock, the maintainer judges the built stage in Training
blockout:
  - tmp/training-rework-20261029/board.html
  - tmp/training-layouts-20261029/board.html
---

# Training: Crash-Test Lab

On 2026-10-29 the maintainer said the Training map looked bland next to the Tutorial and
the other maps and asked how to make it more fitting and useful. The baked arenas showed
why it also taught the wrong numbers: its 20 m walls put the top KO line 40 m above the
floor (PvP maps 20 to 23.7 m), its floor was 1,681 m² (four of six PvP maps 610 to
710 m²) and its 1.0, 2.0 and 3.5 m blocks matched no PvP map. Round 1 picked shell 1, a
copy of the usual PvP shell, and setting A, a crash-test lab, and that build became the
Crash Pad below.

The maintainer called round 1 "pretty good, definitely better" but expected the layout or
content to change for actual training, "not just change the theme". Round 2 offered three
layouts built around drills, and the maintainer combined option 1 (a drill yard with
heights, a trench and a shelf) with option 2 (recovery tests around the edges). The picks
lock this brief. Acceptance comes from the maintainer playing it in Training. One
factual correction followed the build: the first version called the trench lips ledges,
but ledge grabs are disabled in the current game, so shell items 3 and 4 now say lips and
shelf. The maintainer accepted the built stage on 2026-10-29 after playing it ("Training map
view and edge recoveries", GOOD).

Training mode itself does not change: the key stays `training`, it stays the only
`trainingOnly` roster entry, and Training still opens on it by default.

Yaw: the in-game camera's yaw 0 looks north (+Z). The player starts facing north.

```
 N   control window, "DAYS WITHOUT A KO: 0" board                                (hall wall)
     ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ light curtain, KO line z=+23 ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄
          DRILL YARD     trench           CRASH PAD                  RECOVERY RANGE
       ┌──────────────┐┊   ┊┌────────────────────────────────┐
       │▓3 m▓ 1.5 m   │┊   ┊│ ┌deck 1.5┐          ┌deck 1.5┐  │      ▢ 4 m
       │▓▓▓▓  ◎ CPU 2 │┊ 3 ┊│          ◎ dummy               │
 shelf▐│              │┊ m ┊│          ┆ lane                │          ▢ 8 m ◎ CPU 3
 -1.5 ▐│              │┊   ┊│          P                     │
       │ DRILL YARD   │└───┘│          ┆   ▬ BEELINE 16 m    │              ▢ 12 m
       └──────────────┴walkway─────────────────────────────────┘
     ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄ light curtain, KO line z=-23 ┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄
 S   high-speed camera gantry, roller door             foam pit below everything
     side KO lines: x=-45 west of the shelf, x=+40 east of the 12 m island
```

## Personality

Every punch gets crash-tested here before it ships.

## Shell

The gameplay shell is the picked options' own numbers, combined. Production keeps it as
the `SPEC` in `tools/blender/training/layout.py`. Heights are checked against the
measured jumps (`tools/MovementReport`, 2026-10-29): full jump 1.99 to 3.14 m, double
jump 3.25 to 5.13 m, running jump 5.67 to 7.57 m.

1. **Crash Pad.** Unchanged from round 1: one rectangular floor, 28 m east-west by 26 m
   north-south (x ±14, z ±13) at height 0, its walls dropping straight to the KO line at
   −12, with the two 1.5 m decks (6 × 5 m, x 6.5 to 12.5 east and west of the centre
   line, z 7 to 12) and the distance lane.
2. **Drill Yard.** A second floor west of the Crash Pad, x −31 to −17, z ±13, height 0,
   walls to −12. Its step rig: a 1.5 m deck (x −30 to −22, z 3 to 12) with a 3 m block on
   its back corner (x −30 to −26, z 8 to 12). 1.5 m is one jump for every fighter; 3 m
   takes a double jump from the floor (Sir Bonk's single jump reaches 3.14 m) or one jump
   from the deck. The rig's faces are 1.5 m and 3 m walls.
3. **Trench and walkway.** A 3 m slot (x −17 to −14) separates the two floors from
   z −9 to the north rim, where it is open, and drops to the KO line. Ledge grabs are
   disabled in the current game (`CONTEXT.md`, Recovery), so a fighter in the trench
   gets out by jumping or using its recovery move back onto a lip. A walkway at height 0
   (x −17 to −14, z −13 to −9) joins the floors at the south end. A standing full jump
   crosses the trench (full-hop drift 3.9 to 6.0 m).
4. **Shelf.** A 0.6 m thick shelf 1.5 m below the Drill Yard's west edge, 4 m deep
   (x −35 to −31, z −9 to 1), open below, like Log Off's shelves. One jump climbs back.
5. **Recovery Range.** Three 4 × 4 m islands at height 0 off the Crash Pad's east edge,
   walls to −12, spread north to south so no jump path crosses another island: the 4 m
   island (x 18 to 22, z 7 to 11), the 8 m island (x 22 to 26, z −2 to 2) and the 12 m
   island (x 26 to 30, z −11 to −7), named by their gap from the edge. Intent: a running
   jump crosses the 4 m gap for every fighter; the 8 m gap is past every running jump and
   takes a double jump; the 12 m gap takes the recovery move. Production tunes the 8 m and
   12 m gaps by up to 1.5 m in tests until that intent holds for the roster and records
   the numbers.
6. **Blast lines.** The north and south KO lines stay at z ±23, so hits from the start
   towards the north keep match distance. The side lines move to x −45, 10 m past the
   shelf, and x +40, 10 m past the 12 m island. The top line rises to 23 m (the 3 m block
   plus 20; Last Stop's is 23.7 m). Kill height −12.
7. **Spawns.** Spawn_01, the player, at (0, −4) facing north; Spawn_02, the first CPU
   and the dummy spot, at (0, 4) facing south; Spawn_03 on the step deck at (−23.5, 7.5)
   facing east; Spawn_04 on the 8 m island at (24, 0) facing west. Training places the
   second and third CPU on those two drills and Reset Position returns them there
   (`TrainingMatch.NextNpcSpawn`), so the drills double as dummy stations.

The islands sit east of the Crash Pad rather than north as on the board. North of the
Crash Pad they would move the north KO line from z +23 to +39, so a forward hit from the
start would fly 35 m instead of 19 m before a KO. East, both match edges and the north
landmark stay as built. Option 2's 6 m strip merges into the island ladder, whose 4 m and
8 m islands bracket it, and its west shelf moves to the Drill Yard's west edge.

## Composition decisions

1. **The test platform.** The Crash Pad, the Drill Yard and each island are steel test
   platforms standing on braced lattices of legs inside a vast, bright test hall. Each
   reads as a raised slab with a plated face, not a room: nothing rises from any edge.
2. **Floor paint.** A mid-grey test floor with a thin 1 m grid and stronger 5 m lines on
   both floors, all paint. Thin yellow-black hazard stripes run along every edge and
   trench lip. A large yellow-black crash-test calibration target marks the dummy spot,
   smaller ones mark the CPU spots on the step deck and the 8 m island and a plain
   painted box marks the player start. "DRILL YARD" is stencilled near the yard's south
   edge.
3. **Distance lane.** Unchanged: from the dummy spot down the centre line to the south
   edge, metre ticks with numbers 5, 10 and 15 counted from the dummy, and a teal
   "BEELINE 16 m" bar at z = −12.
4. **Deck and rig faces.** The Crash Pad's decks and the step rig have yellow-black
   striped sides carrying height boards, ticks every 0.5 m, to 1.5 m on the decks and to
   3 m on the block. Plates read "1.5 m · ONE JUMP" and "3 m · DOUBLE JUMP".
5. **Depth marks.** Every plated face that drops to the KO line, including both trench
   walls and the island faces, carries depth marks every 2 m from the edge down,
   numbered at −4 and −8, with a red KO band at −12. "TRENCH 3 m" is stencilled on the
   Crash Pad's lip and the shelf carries a "−1.5 m" plate on its hazard nose.
6. **Gap labels.** Each island's cap carries its gap in large paint ("4 m", "8 m",
   "12 m"), and a matching arrow with the same number is painted at the Crash Pad's east
   edge facing it.
7. **Light curtains.** Sparse, thin red light-curtain beams stand on the four side KO
   planes, from below deck level to a few metres above it, between slim yellow-black
   emitter posts. They read as a boundary you trip, never as a wall or a surface.
8. **Landmark.** From the start camera (yaw 0), centre and top third: the north hall
   wall's control-room window strip at deck height with a big "DAYS WITHOUT A KO: 0"
   board and the "SLOP LABS · IMPACT TESTING" sign above it.
9. **West wall.** From the start camera, left beyond the Drill Yard: a bench of
   crash-test dummies on a mezzanine at deck height, watching the deck. A gentle bob is
   allowed.
10. **East wall.** Right beyond the Recovery Range: the crash-sled track along the hall
    floor ending in a barrier with a wrecked test car, and a rack of spare dummy parts.
11. **South wall.** Behind the start (yaw 180): a high-speed camera on a gantry aimed at
    the deck, light towers and a big roller door with hazard stripes.
12. **Scene world.** Below: a deep pit of grey-blue foam blocks fills the hall floor
    under and around every platform, its surface below the KO line, so a launched fighter
    blasts before reaching it. Above: overhead trusses with long rows of panel lights,
    rarely on screen. Far: the hall's panelled walls close every direction about 10 to
    15 m beyond the light curtains; the hall widens east and west for the new KO lines.
    Nothing outside the hall is visible.
13. **Busy / empty.** Busy: the hall walls and their four set pieces, the camera gantry,
    the curtain posts. Empty: every floor, deck, island and the trench apart from paint,
    and the KO corridor apart from the curtain beams on its outer boundary.

## Negative decisions

- No raised props on any walkable surface. The decks, the step rig and the islands are
  the only raised surfaces; targets, lane, labels, arrows and stripes are paint.
- No walls, railings or barriers along any edge, trench lip, shelf or island. Every edge
  stays open.
- No background geometry inside the blast corridor (x −45 to 40, z ±23, −12 to 23).
  Curtain beams and posts sit on or just outside its boundary.
- No second hero landmark beside the KO board and sign.
- No saturated colour or coloured light on any floor, and nothing flashing or scrolling
  near the platforms that competes with move effects. The maintainer judges new moves and
  effects here.
- Nothing of the old grey lab remains.

## Palette & lighting

Bright, even hall light, neutral to slightly cool white from long overhead panels: a high
key light from slightly south of overhead, so shadows stay short under fighters and the
north wall is lit for the start view, plus a soft ambient fill. The Drill Yard and the
Recovery Range get the same light as the Crash Pad; neither reads as a dim annex.
Mid-grey floor with albedo around 0.45, off-white panelled walls kept below clipping,
safety yellow and black, calibration black and white, red only for KO markers, teal for
the Beeline bar, grey-blue foam. The stage carries its own `Stage Lights` rig, so
Training lights it the way every PvP map lights itself.

## Camera vantages

- Start, yaw 0: lane, dummy target, both decks, the KO board and control window, the
  trench lip at the left.
- Dummy spot, yaw 180: the lane numbers, the camera gantry and roller door.
- Drill Yard from the Crash Pad's trench lip, looking west: trench, step rig, CPU 2's
  target, the shelf edge.
- Recovery Range from the Crash Pad's east edge, looking east: the three islands, their
  gap labels and arrows.
- Inside the trench below the lips, looking north along it: depth marks and both lips
  (recovery).
- Off the south edge looking north: depth marks, the curtain and the plated face
  (recovery).
- Thumbnail: elevated three-quarter view from the south showing the Drill Yard, the
  Crash Pad and the Recovery Range, the curtains and the KO board.
