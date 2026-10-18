---
key: license_test
display_name: Fighter's License
purpose: tutorial
status: locked
locked: 2026-10-17
approved_by: maintainer
variant: static
approval_scope: hub-yard tutorial shell on a rooftop parking deck with one open edge; gray box ("very good", 2026-10-18), then the art pass it asked for
runtime_camera_review: pending
blockout:
  - .stage-authoring-cache/license_test/design/design-top.png
  - .stage-authoring-cache/license_test/design/design-north.png
  - .stage-authoring-cache/license_test/design/design-east.png
  - .stage-authoring-cache/license_test/design/design-south.png
  - .stage-authoring-cache/license_test/design/design-west.png
---

# Fighter's License test deck

**Status:** on 2026-10-17 the maintainer asked for a tutorial map and chose, in turn,
a new dedicated map, a *hub yard* layout, the *Fighter's license test* theme, a
*rooftop parking deck* setting and *one long open edge*. The layout sketch below was
presented with those choices. This brief locked that bounded scope for the gray box
under the solo implementation exception. On 2026-10-18 the maintainer called the gray
box "very good" and asked for the art pass, which revised decisions 7 and 9 and the
props rule to match the picked option's own words (see Revisions). The in-game camera
review, the art's acceptance and human tutorial acceptance remain pending.

Yaw: the in-game camera's yaw 0 looks north (+Z); the start faces north.

```
 N  ┌──────── TEST CENTER (12 m wall) ────────┐
    │   ■1.5m   ■3m   ▲tower 5.5m             │
    │   ───── lane ─────                       │
    │                    ( TEST RING )    K   ┊ open edge,
    │                          D              ┊ sheer facade
    │        S start line                     ┊ to the kill plane
    └──── safety fence (12 m), parking bays ──┘
```

## Personality

Before anyone may fight, an examiner makes you pass your fighter's license on the roof
of the Department of Fighting.

## Composition decisions

1. **Deck.** A 40 × 30 m asphalt parking deck at height 0 is the only floor. It is the
   roof of a building whose east face drops sheer to the kill plane 11 m below.
2. **Start.** The player starts on a painted line near the south fence, facing north.
   From the start camera the jump course is straight ahead, the test ring ahead-right,
   and the open edge at the far right.
3. **Jump course.** Ahead-left, 3 m in front of the north wall, a 1.5 m block, a 3 m
   block and a 5.5 m lookout tower step up from west to east with 2 m gaps, in orange
   "test equipment" colours. A full jump reaches the first, a double jump from the floor
   or a hop from the first reaches the second, and the tower is a double jump from the
   second. A painted lane line runs in front of the course.
4. **Lookout tower.** The tower is the hero perch for the fast-fall lesson: from it the
   camera looks east-south-east over the ring, the knockout box and the skyline.
5. **Test ring.** Centre-right, a 12 m teal ring holds the dummy for the combat lessons.
6. **Knockout box.** A painted box 5 m from the open edge marks where the dummy waits
   for the final lesson.
7. **Open edge.** The whole east side is open: a yellow-black hazard stripe and a pale
   slab lip, the city falling away below. The guardrail that lined it is broken: its two
   torn ends stay bolted beside the north and south walls and hang over the drop, sheared
   post bases dot the lip and skid marks run to the edge. Recovery and the knockout
   happen here.
8. **Walls.** North: the base of the Test Center (glass front and entrance, the
   department fascia, offices above, a road sign over each obstacle). West: the
   neighbouring building's brick party wall, with the parking ticket booth, the
   shuttered vehicle exit and an equipment cage of cones recessed into its thickness.
   South: a tall barred safety fence with billboards, see-through to the city. All are
   12 m, higher than any fighter can reach from the tower.
9. **Scene world.** The Test Center tower rises beyond the north wall with a large
   FIGHTER'S LICENSE sign on its roof edge; because the gameplay camera never looks up,
   the same sign is also painted big on the north wall above the course. A brick
   neighbour stands west; the street, traffic and a coned-off lane lie 60 m below; east
   of the edge low roofs fall away to a hazy skyline.
10. **Busy / empty.** Busy: the north side (course, tower, sign) and the east horizon.
    Empty: the deck centre and south, open room to run in the first lesson.
11. **Surface.** One continuous asphalt plane; markings (start line, lane, ring, box,
    bays, edge stripe) are paint, never raised.

## Negative decisions

- No second hero landmark beside the tower and its sign.
- No raised props on the deck without collision: cones and the booth sit in recesses in
  the walls' thickness, on the street below or in paint. The only exception is the broken
  guardrail's two torn ends (decision 7), kept beside the walls and below 1 m.
- No intact railing or barrier along the open edge, and no ramp off the deck.
- No reachable wall tops (walls 12 m; tower top plus the highest double jump is 10.6 m).
- No background geometry inside the blast corridor: side planes 10 m past the walls and
  the open edge, kill plane at −11, top at 32.

## Palette & lighting

Bright afternoon with a high sun from the east-south-east over the open edge (azimuth
100°, elevation 58°), so the walls' shadows fall away from the deck and only the barred
fence stripes its south side. Warm grey asphalt, municipal yellow for lines and the sign,
teal for the ring, orange test equipment, a cool hazy city.

## Camera vantages

- Start, looking north: the whole course and the ring.
- Tower top, looking east-south-east: hero view over ring, knockout box and skyline.
- Ring, looking east: the knockout lesson toward the open edge.
- Just off the edge, looking back west at the facade: the recovery lesson.

## Revisions

- **2026-10-18, art pass (pending the maintainer's acceptance).** The picked option read
  "Lane paint, cones, a ticket booth, city below; the open edge is a broken barrier". The
  2026-10-17 lock had dropped the barrier and kept cones and booths off the deck.
  Decision 7 now has the broken guardrail; decision 8 places the booth and a cone cage in
  recesses in the west wall, where no fighter or camera reaches; decision 9 adds the
  painted wall sign and the coned-off lane below; the props rule names the barrier as its
  only exception. The shell, spawns and lesson spots are unchanged.
