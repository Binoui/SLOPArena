# ADR-0041: Couch play in Solo

**Status:** Proposed — 2026-10-26; extended to four seats 2026-10-27

## Context

On the 2026-10-24/25 weekend, friends passed one Steam Deck around and took turns playing
Solo against the CPU. Two people on one machine cannot fight each other. Solo already runs
a local stock match through `LocalSimulationBridge`, Shared already takes one input per
fighter per tick, and Fighter Select already shows a P1 card and a P2 card
(`EDITING P2 // CPU`). What stops a second person is the client's assumption of one human:

- **Input.** `HumanInputActions` is one static action map. Every gamepad and the keyboard
  and mouse drive P1.
- **Camera.** `InputController.BuildInputState` makes movement relative to the camera and
  takes the default aim yaw and pitch from it. `CameraMount` follows P1.
- **Match.** `TrainingMatch` builds P2's input from the CPU policy and presents one
  `PlayerEntityId`, with one aim handler and HUD self. The end card reads YOU WIN or
  CPU WINS.

## Decision

1. **Local Players.** A Local Player is one person on this machine with their own device:
   one gamepad, or the keyboard and mouse. Each gets their own instance of the gameplay
   action map, restricted to that device. Menus keep the shared map, so any device
   navigates. Bindings stay per device type. `InputState` and Shared do not change.
2. **Four seats.** Solo always has four seats, P1 to P4, like an online Room. P1 is the
   local player. P2 to P4 are each Open, CPU or a Local Player; a match needs at least one
   of them filled. P2 starts as a CPU and P3 and P4 start Open. On Fighter Select, P1 adds
   or removes CPUs and sets each CPU's fighter and difficulty. A pad other than P1's that
   presses A takes the lowest seat without a Local Player and picks its own fighter; B
   hands the seat back. In the match a Local Player's input comes from their pad and a
   CPU's from the CPU policy, which targets the nearest fighter still in. The match is a
   free-for-all stock fight; countdown and results stay Solo's and the end card names the
   winning seat. Each seat has a player colour (P1 yellow, P2 orange, P3 teal, P4 lilac)
   on its Fighter Select panel, menu cursor, facing ring and fight card, so mirror
   matches stay readable. Training keeps P1 and one practice CPU.
3. **Couch camera.** With two Local Players, one shared camera keeps both fighters in view
   as much as possible. It builds on the SideDuel style, which already turns side-on to
   the fight, centres between the fighters and zooms out to fit them. The couch version
   frames the pair evenly rather than from P1's side: exactly side-on (90° instead of
   SideDuel's 75°) and at the pair's height instead of P1's. Camera orbit input is ignored.
   Like SideDuel, it turns at a limited speed and holds still while the fighters overlap,
   so stick directions drift slowly instead of jumping. Both Local Players move relative to
   it, and aim comes from the stick and the lock-on target instead of the camera.
   The owner picked the high side-on view (pitch 40°) from the 2026-10-26 prototype as
   the interim default. A proper camera rework is a separate task.

## Consequences

- No Shared, GameHost, Master or protocol change. Online play is unaffected.
- Everything that reads `HumanInputActions` during play (camera, aim, HUD prompts, pause
  and retry) reads a Local Player's instance instead. Match code stops assuming one
  `PlayerEntityId` for presentation: both fighters get their own facing ring, aim and
  lock-on.
- Under the couch camera, aimed specials use the stick (keyboard and mouse keep the hidden
  screen cursor), and nobody orbits the camera. One camera keeps the rendering cost of a
  Steam Deck match unchanged.
- The camera's yaw follows the fight, so both players' stick directions rotate slowly with
  it. The prototype decides whether that turn rate feels right.
- With more than two fighters the couch camera fits every fighter still in. Turning it
  side-on to the fighters' widest spread belongs to the separate camera rework.
- On acceptance, `CONTEXT.md` gains Local Player and couch camera.

## Considered alternatives

- **Split-screen.** Keeps per-player camera styles and camera aim. But the crosshair,
  target rings, Speech Slips and overhead labels all assume one camera; it halves the
  Deck's 1280×800 screen and doubles rendering. Deferred.
- **A shared camera with a fixed yaw.** Keeps stick directions constant, but fighters that
  line up along the view hide each other and depth is hard to read. The prototype can
  compare it.
- **Two players on one keyboard.** Mouse aim and the 16-entry grid do not fit two people on
  one keyboard. Out of scope.

## Later: couch online

Two Local Players in one online Room is a separate, larger change that needs its own ADR.
Online, one Steam account means one fighter throughout. Master keys Room members and Match
records by Steam ID. The client finds its single Roster entry by Steam ID
(`ClientSession.ApplyMatchStarted`). The GameHost binds a Steam connection to one entity
and closes it on input for another (`SteamGameServerHost.ProcessFrame`), and results name
the winner by Steam ID. `NetplayPredictor` keeps one self input history. Couch online would
need Room places for extra Local Players without accounts, one connection carrying all of
a machine's fighters, an input history per Local Player in prediction, results per fighter
(a Master database migration) and a coordinated client, GameHost and Master release.
Decisions 1 and 3 are prerequisites.

## Delivery

1. Prototype: a second pad replaces the CPU in Solo, under the couch camera, for an owner
   feel check.
2. P2 pairing and fighter pick on the P2 card, player colours and P1/P2 results. P2 joins by
   pressing A on a second pad and picks with LB/RB; while P2 is joining or playing, that pad's
   presses never drive P1's menus.

## 2026-10-28 frontend entry

Solo and Arcade share one **Local Play** entry. The flow is Mode, then Fighters, then Stage for Brawl. Slopball, Character Courses and Manki Mayhem start from Fighter Select on their fixed stages. Match-facing Solo and Arcade values stay unchanged.

[ADR-0046](0046-courses-open-to-every-fighter.md) changes Character Courses to Mode, then Course, then Fighters. Every roster fighter can play every course. Courses remain P1-only in this slice.
