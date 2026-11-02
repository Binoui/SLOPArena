# ADR-0043: Steady camera replaces the chasing cameras

**Status:** Accepted on 2026-10-26 and implemented. It supersedes the 2026-10-16 choice of the fast-stick
manual camera and the 2026-10-22 selectable combat styles.

## Context

On 2026-10-16 the owner rejected framing and fixed cameras because they felt awkward with
camera-relative movement, and kept a fast manual stick. On 2026-10-22 six selectable styles were
added: Manual, Auto Combat, Arena, Air Duel, Side Duel and Shoulder. Auto Combat became the
controller default.

On the 2026-10-25 Steam Deck weekend the players were not fighting-game players. The owner then
reported three problems. Shoulder's low angle hides depth. Auto Combat turns a lot while you move
and feels confused. The player should sit in the middle of the screen. Fighters are launched high
often, and Slopball needs the ball in view.

The code explains the turning. Auto Combat chases the target at up to 120°/s with an 8° dead
zone. Stick input is camera-relative, so every turn bends the current run, which turns the camera
again. A 2026-10-26 Training prototype measured this with six 0.4 s sidesteps 8 m from a dummy.
Auto Combat swung about 83° in total and drifted the player 5 m back. The prototype moved 6° and
kept the line. Evidence is in `tmp/camera-follow-20261026/REPORT.md`, which is ignored.

The owner tuned the prototype live against the CPU and in Slopball, and accepted the result.

## Decision

1. **Steady** is the new style and the default on both mouse and controller. It has these
   properties:
   - It looks down at 35° from a fixed distance, the manual radius, and never zooms out.
   - It centres the player. The focus stays at chest height on a ground reference, which follows
     landings and drops more than 3 m. Ordinary jumps leave it in place.
   - It turns only when the target leaves a ±60° cone, then eases to 40% inside the cone. Its top
     speed is a setting: Calm 20°/s (the default), Medium 30°/s or Quick 45°/s. No new turn starts
     within 3 m, so cross-ups do not flip it.
   - A launched opponent (airborne in hitstun or post-hitstun flight, within 20 m, for up to 6 s)
     or a high ball lifts the focus by up to 6 m.
   - Off-screen opponents show an edge bubble with their height or distance.
   - On a Slopball court it looks down the court from the player's side of the net and frames the
     ball. Slopball's own ball chip covers the ball when it is off-screen.
   - Camera-aimed moves use the fighter's facing, as in couch play (ADR-0041).
   - "Hold run direction" is on by default. A held stick keeps the movement basis it started with
     until the direction changes by more than 40°.
2. **Removed styles:** Auto Combat, Air Duel, Side Duel and Shoulder. Manual (lock assistance,
   fast stick) and Arena remain. The Y key and an R3 hold still toggle between Manual and the
   remembered automatic style.
3. **Migration:** stored values stay integers (Manual 0, Arena 2, Steady 6). Saves without
   `cameraRevision` held the old defaults rather than player choices, so they move once to
   Steady on both devices. Later invalid values also become Steady.
4. **Stage geometry (owner request, 2026-10-28):** walls and platforms no longer pull the orbit
   camera in, in any style. The stage shaders dither away geometry above a fighter's feet inside a
   cone from the camera to each of up to four fighters (`StageSightHoles`), so the view keeps its
   distance and the fighters stay visible. URP/Lit stage materials are swapped at load to a Lit
   copy with the same holes. When the sight line to the player has been blocked for 0.5 s, Steady
   and Arena drift at 12°/s toward the nearest clear angle within 90° that keeps the target in
   view. Player camera input cancels the drift. The aim camera still pulls in.
   **Revised 2026-11-01 (owner found the round hole small and confusing; picked take D):** a
   fighter's hole opens only while stage geometry blocks it (eased in and out). It is an upright oval of
   the same width all along the sight line. Within 2.5 m in front of the fighter (measured horizontally)
   the geometry stays as a 35% ghost with a light seat tint and a seat-colour edge; foreground nearer the
   camera opens fully. Upward faces up to 4 m above
   the fighter's feet stay solid, so the stage players walk on never vanishes. `FighterXRay` draws a
   seat-colour silhouette wherever the stage still covers a fighter (fighters render at Geometry+50,
   after the stage).

## Consequences

- Without a zoom-out, a far or very high opponent is shown only by a bubble. Matches with three
  or four fighters may show several bubbles at once. They have not been tested.
- The online Versus Slopball court uses the same `SetCourt` path as solo Slopball, but nobody has
  tested it online.
- Couch play keeps its own shared camera (ADR-0041), and its ADR still describes it relative to
  the removed Side Duel style.
