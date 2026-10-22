# Versus Slopball

Requested 2026-10-22: plan and implement PvP volleyball from online co-op Slopball.
This is slice 3 of [online Arcade](../design/online-arcade-implementation.md), using
the already accepted [ADR-0040](../adr/0040-online-arcade-room-pick-and-match-format.md).

## Scope and rules

- Keep the existing Night Court, ball physics, package attacks/projectiles, online
  match lifecycle and full-world prediction. Add Versus 1v1 (two players) and 2v2
  (four players); three-player Rooms retain co-op.
- Players choose Teal or Clay in Character Select. Assign balanced initial camps;
  changing camp clears that player's lock. Match admission requires equal camps.
- Each camp stays on its own side of the net. Camp starts mirror the existing solo
  start, preserving the authored stage and solo record fingerprints.
- First to 21 points, as selected by the user, without a win-by-two requirement.
- Teal serves first. The conceding camp serves next; in 2v2, rotate that camp's
  server through its members in entity order. The ball waits at the server's start
  until that player hits it. Touches are unlimited, including setups before crossing.
- A floor contact awards the other camp one point. Reset ball percent, fighter
  damage, positions, active attacks/projectiles and transient combat state between
  points. Cooldowns also reset so a point starts cleanly.
- Cross-net fighter combat is enabled by default. A Room's Ball only rule disables
  fighter attacks/grabs against rivals. Teammates cannot damage, block or grab one
  another. No teammate boosts or touch limit in this slice.
- Show camp colours, both scores, serving player and match result. Keep the leader's
  Return to Lobby action available for both online Slopball formats.

## Implementation choice

Use the approved focused Shared rally component alongside `ArcadeBallSimulation`.
`ServerSimulation` selects co-op or versus rules; GameHost and prediction use the
same immutable setup. Carry camps and rules through Master Room snapshots, match
start, Shared codecs and client setup. Capture rally/serve/score state atomically
in World Snapshots and send point/serve events through the existing rollback ledger.
Bump the simulation protocol for the changed contracts.

Extending the Launcher return counter into two competing camps would mix distinct
rules. A separate simulation or Unity-only scoring would duplicate authority and
break prediction. Neither alternative is needed.

## Work and validation

1. Implement deterministic camp starts, held serves, floor scoring, point reset and
   match completion. Cover both camps, teammate filtering and round transitions.
2. Extend setup/results codecs and snapshots; verify malformed setup rejection,
   snapshot restore equivalence, event deduplication and real UDP peers.
3. Add Master admission/camp selection and Room rules; wire GameHost and client
   preparation, team markers, scores, serve prompts and results.
4. Run focused tests while iterating, then Shared and Server builds/suites, Master
   tests, documentation checks, and canonical Unity compile/runtime verification
   through the gateway. Maintain at most five current human checks in
   `TESTING-UNITY.md`.

Preserve all pre-existing dirty work in both repositories. No commit, push,
deployment, character cook, tool installation or Steam publication is included.
Two-account/four-account remote gamefeel acceptance remains a human playtest.

## Verification record

- Shared: **3,321 passed**, two existing skips. Coverage includes first-to-21
  completion, both scoring floors, server rotation, point resets, team filtering,
  snapshot restore equivalence, malformed snapshots/setup, event codecs and a
  reachable held serve for every admitted fighter. Point resets normalize height
  per fighter, keeping tall and short fighters grounded during the inter-point gap.
- GameHost: **52 passed**, including real two-client and four-client UDP Versus
  matches through the prediction, snapshot and event-ledger contracts.
- Master: **210 passed** in the companion repository. Versus tests cover 1v1/2v2
  admission, camp changes/unlocking, balanced launch, Ball only, actual SignalR
  pushes/GameHost requests, and Return to Lobby.
- Documentation checks and whitespace checks pass. Unity compilation reported no
  compiler errors and an empty current error console.
- Live 2v2: one canonical Unity client on Clay with three headless UDP peers,
  using FightGuy, Manki, Wibou and Bonk, reached **1–1**. Prediction was seeded
  and three ticks ahead; all four camp rings and the first-to-21 HUD passed the
  rendered-frame check. The HUD uses camp badges and hides stock counters.
  Ground indicators remove each rig's visual alignment offset before testing
  floor clearance, so Manki's ring remains visible. The composited capture was
  inspected. The clean Frontend scene and prior client settings were restored,
  and the independent Editor lease was released settled.

`sloparena.arcade.slopball.test-online --points 2 --seconds 35` now observes a
disposable online Versus session, waiting for real points and checking camp halves,
camp rings and the first-to-21 HUD. It creates no opponents and changes no gameplay.
Run it under the normal bounded gateway lease. The root `TESTING-UNITY.md` keeps
human acceptance separate from this automated check.

Master, GameHost and clients must ship together on protocol **14** (World Snapshot
4, World Frame 3). This task does not deploy or activate that release.
