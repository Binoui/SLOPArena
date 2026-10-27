# Manki Mayhem implementation plan

**Status:** Proposed 2026-10-27 with
[ADR-0044](../adr/0044-manki-mayhem-hazard-director.md); awaits the maintainer's approval.
Nothing below is built. The [feature brief](manki-mayhem.md) owns rules and presentation;
this page orders the work, names the files and tests, and lists the decisions still open.
Version numbers are those at the time of writing; other work may move them first.

## Where the code stands

| Area | Today | Mayhem needs |
| --- | --- | --- |
| Formats | `MatchFormat` has Stock, two Slopball and two course formats; `CreateRule`, `Admits` and `StartFor` switch on them. | `MankiMayhem` with its rule and arena check; starts at the arena's four spawns. |
| Stage | `manki_mayhem`: flat 24 × 20 m deck, four spawns at (±5, ±4), PvP purpose, not in `roster.json`. The prefab's `MankiHostAnchor` is at (0, 6.5, 41.5). | A Mayhem Arcade section (host origin, play area, deck top); purpose becomes Arcade. |
| Arcade section | Exactly one of ball or course; 1–16 allowed packages; section versions 1–4 mean a ball, 5–6 a course. | A third kind behind version 7, byte-identical old bakes, empty allowed list for Mayhem. |
| Simulation | Arcade components built in the `ServerSimulation` constructor, hooked into `Tick`; the Slopball Launcher is a seeded generator. | `MankiMayhemSimulation`, built only under the Mayhem rule. |
| Projectiles | `Hitbox` spheres and capsules in `SpellResolver`; Round Bomb explodes on contact or landing; Bazooka homes. | Sticky fuse and expanding ring. |
| Hazard owner | `ResolveHits` handles a non-fighter owner; dispatcher and `ProjectileVFXManager` skip owners without a renderer. | Reserved host ID with a Unity presentation entry. |
| Collision | Baked static triangles; the net clamp is the only runtime constraint; course targets don't block. | `RuntimeSolidSet` with a swept constraint and hit routing. |
| Rollback | World Snapshot 9, World Frame 4, protocol 19; ledgers for Slopball and course events. | Snapshot, frame and protocol bumps; a Mayhem ledger. |
| Friendly fire | A boolean in the start contract, co-op Slopball only; Master's Lobby-only preference. | Off, Shoves, On. |
| Online hosting | `PvPMatch` hosts `OnlineSlopball` and `OnlineCharacterCourse`. | Mayhem presentation hosted the same way. |
| Local play | `TrainingMatch` runs Solo's four seats with CPUs under a stock rule; the CPU policy targets the nearest fighter and ignores hazards. | The Mayhem rule offline; CPUs that avoid telegraphs. |
| Camera | Steady by default; `CouchCamera.Fixed` holds one yaw at 30° pitch and frames fighters. | A show camera: yaw 0, lower pitch, deck and host framed. |

## Delivery: the first slice in six steps

The first slice is everything the maintainer selected: three cards, the loop, scoring,
call cards, the big host and the show camera, online. It lands in six steps. Each step
ends at a check and needs its own go-ahead. Steps 1–2 are offline; step 3 brings the
coordinated protocol release in; steps 4–6 add the remaining cards and the show.

### Step 1 · Shared round loop and SCATTER!

| Where | Files |
| --- | --- |
| Shared | `src/Shared/MatchFormat.cs` (format, wire name, `Admits`, `CreateRule`, `StartFor`); `MatchRule.cs` (`MankiMayhemMatchRule`, `FriendlyFireMode`); `ArcadeStageDefinition.cs` and new `MankiMayhemDefinition.cs` (third kind, section v7); new `MankiMayhemSimulation.cs` (director, phases, deck bag, scores, bench) and `MankiMayhemRounds.cs` (SCATTER!); new `ServerSimulation.Mayhem.cs` (construction, `BeginMankiMayhem(seed)`, `EvaluateMatch`, benching, Shoves); `ServerSimulation.cs` (constructor, `Tick` hooks, attack filter, Shoves in `ResolveHits`); `ServerSimulation.Courses.cs` (inactive combination); `Hitbox.cs`, `SpellResolver.cs`, `Snapshots/SnapshotCodecs.cs` (sticky fuse); `ServerSimulation.Snapshot.cs` (snapshot 10); new `AI/MayhemThreats.cs` and a hook in `AI/HeuristicBotPolicy.cs` (leave landing rings). |
| Stage | `client/Unity/Assets/Scripts/Runtime/World/ArcadeStageAuthoring.cs` (Mayhem block: host origin marker, play area); `Scripts/Editor/SlopArenaStageShellCommands.cs` (bake); markers in `Assets/Stages/manki_mayhem/manki_mayhem.unity`; rebaked `data/arenas/manki_mayhem.arena`. |
| Tests | New `tests/Shared.Tests/MankiMayhemTests.cs`: phases, bag order per seed, scoring, bench and return, friendly fire Off/Shoves/On, host cleanup between rounds. New sticky-fuse tests: attach, follow, fuse blast, carrier KO. `ArcadeSnapshotTests`: restore at every tick of a seeded round equals the continuous run. `WorldSnapshotContractTests`. `MankiMayhemArenaTests`: section round-trip, Arcade purpose, Stock refuses it. Byte-identical rewrites of the existing Slopball and course sections. A seeded four-CPU headless Match that reaches an outcome. |
| Check | `dotnet test tests/Shared.Tests/ -p:SkipUnityPluginCopy=true` and a headless report: rounds, hazards, survivors and scores per seed. No human check. |

### Step 2 · Offline: Local Play with CPUs and base presentation

| Where | Files |
| --- | --- |
| Unity | `Runtime/World/TrainingMatch.cs` (Mayhem rule, seed, Solo seats, results); `Runtime/UI/MatchConfig.cs` and a dev entry first (menu entry if selected); new `Runtime/World/MankiMayhemPresentation.cs` (rings, bombs, fuse, stamps; explicit dependencies like `OnlineSlopball`); new `Runtime/World/MankiHostPresenter.cs` (Manki's model scaled at the anchor, existing clips on events); `Runtime/World/TimelinePresentationDispatcher.cs` (host entry); `Runtime/Combat/ProjectileVFXManager.cs` (host owner); `Runtime/Camera/CameraMount.cs` (show camera); `Runtime/UI/HUDManager.cs` (call banner, score rows, OUT); `Runtime/UI/SoloEndCardView.cs` (standings). Ignored `TESTING-UNITY.md` checks. |
| Tests | Unity compile and console gate; a gateway scenario that starts Mayhem offline with three CPUs, runs several rounds and captures the show view; the CPU leaves landing rings in a Shared bot test. |
| Check | **Maintainer:** play SCATTER! alone against three CPUs. Judge the loop, the card, the camera pitch against ring readability, and the stand-in host. |

### Step 3 · Online: Rooms, GameHost and rollback

| Where | Files |
| --- | --- |
| Shared | `MatchStartRequest.cs`, `MatchStartedConfig.cs`, `SteamMatchDescriptor.cs` (format and friendly-fire mode); `WorldFramePacket.cs` (format 5, Mayhem events); `Rollback/NetplayPredictor.cs` (Mayhem ledger); `MatchResultPacket.cs` (Mayhem marker); `SimulationProtocol.cs`. |
| GameHost | `src/Server/MultiMatchOrchestrator.cs` (format, arena, 2–4 fighters, friendly fire); `MatchInstance.cs` (seed, withdraw on disconnect, results); `MatchControlServer.cs`. |
| Master | In `SlopArena-MasterServer`: `Rooms/RoomFormats.cs` (format, `IsArcade`, `Fits` 2–4); `Rooms/RoomManager.cs`, `Rooms/RoomModels.cs`, `Hubs/LobbyHub.cs`, `Lobbies/LobbyModels.cs`, `Lobbies/HttpMatchLauncher.cs` (friendly-fire rule and projection); `appsettings.json` (`Room:AdmittedArcade`). |
| Unity | `Runtime/Network/LobbyClient.cs`, `Runtime/ClientSession.cs`, `Runtime/UI/LobbyRoomUI.cs` (rule), `Runtime/UI/StageSelectController.cs` (ARCADE card), `Runtime/UI/CharSelectController.cs` (free pick), `Runtime/World/PvPMatch.cs` (hosts the presentation from ledger events), `Assets/Resources/Stages/roster.json` (`arcade` entry). |
| Tests | `MatchStartRequestCodecTests`, `WorldFramePacketTests`; `NetplayPredictorTests` with the `NetplayLab` on the Mayhem arena under delay, jitter and loss (each event starts once, contradicted ones retract, benched fighters resume input on the right tick); `tests/Server.Tests/FullWorldNetplayTests.cs`: a real `MatchInstance` with two `NetplaySession` peers plays a seeded Mayhem Match to the same scores; `ArcadeStageAdmissionTests` (format, arena, size, rule); Master tests for format fit, pick and rule projection. |
| Check | Two local clients through Room → ARCADE → Mayhem → Character Select → Match → Results. |

### Step 4 · JUMP!

| Where | Files |
| --- | --- |
| Shared | `Hitbox.cs`, `SpellResolver.cs`, `Snapshots/SnapshotCodecs.cs` (ring shape, radial sweep); `MankiMayhemRounds.cs` (slams, pairs on later passes); `AI/MayhemThreats.cs` (CPUs jump rings). |
| Unity | Ring and slam-point views; host slam reaction. |
| Tests | Ring geometry: one hit per fighter, a jump clears it, a fast ring can't skip a fighter. Restore equivalence and `NetplayLab` with rings. |
| Check | Offline, then two clients. Snapshot and protocol bump if step 3 has shipped. |

### Step 5 · BREAK OUT!

| Where | Files |
| --- | --- |
| Shared | New `RuntimeSolidSet.cs` (boxes, swept constraint, hurtboxes, snapshot); `ServerSimulation.Mayhem.cs` (constraint after movement, hit routing); `MankiMayhemRounds.cs` (cages, the cracked wall, a bomb per cage); `AI/MayhemThreats.cs` (CPUs hit the wall away from the bomb). |
| Unity | Wall views: pop up, crack per hit, shatter, sink. |
| Tests | Walking, running, dashing and the strongest roster launches cannot cross a wall; fighters slide along faces; a wall spawning against a fighter resolves inward; hit counts break walls; every roster fighter can break out within the round with at least one move (a per-fighter table like `MankiMayhemArenaTests`). Restore equivalence and `NetplayLab` with walls. |
| Check | Offline, then two clients. |

### Step 6 · The show and the release

| Where | Files |
| --- | --- |
| Unity | `MankiHostPresenter.cs` (full reactions: shout, wind-up, throw, slam, laugh, hype; dedicated clips later); named CRT screen surfaces from `tools/blender/manki_mayhem/presentation.py` in `Assets/Resources/Stages/manki_mayhem.prefab`, driven with the call card; the booth bench for knocked-out fighters if selected; sounds; Results polish. |
| Release | A protocol-coordinated playtest release (`skills/sloparena-build-export/SKILL.md`) with Master's admission entry. |
| Check | Presentation review captures at the show camera, two clients, then a friends playtest. |

## Later slices

| Slice | Adds |
| --- | --- |
| TAKE COVER! | Crates as cover-shaped solids; host-owned homing rockets with a reticle per fighter. |
| WEAVE! | Lane motion (ADR-0044 decision 4); authored lanes; axe, barrel or tyre props. |
| GET HIM! | Manki on the deck as a hittable object with a hit count per fighter; most hits wins the round. |
| Round mixing | Two patterns in one round on late passes. |
| Selected proposals | Whichever of last one standing, the booth and Manki's favourite were deferred. |

## Test path for the maintainer

| When | Setup | What it proves |
| --- | --- | --- |
| After step 1 | Nothing to run; the headless report. | Rounds, scoring and determinism. |
| After step 2 | **Alone, offline:** Local Play against one to three CPUs on this machine or the Steam Deck. | The loop is fun; card, camera and telegraphs read. |
| From step 3 | **Alone, online pipe:** a development GameHost, the Editor as entity 1 and a headless `NetplaySession` peer, as in `skills/sloparena-netcode/SKILL.md`. Mostly for agents. | Rollback, events and results through the real GameHost. |
| From step 3 | **Two clients:** a local Master and GameHost ([testing](../testing.md), local full-stack setup), the Editor plus a development player build, or the laptop as the second client on the LAN. | The Room flow, prediction feel and Results. |
| After step 6 | **Friends:** a playtest release. Master admission is configuration, so the operator can remove Mayhem from `Room:AdmittedArcade` if it misbehaves without another build. | Real network, mixed skill, "another round?" |

CPUs need only to leave landing rings, jump rings and hit a wall. They are for testing
and Local Play; online Rooms stay human-only.

## Risks

| Risk | Mitigation |
| --- | --- |
| A predicted aim differs from the server's and a telegraph pops | Aim at the call; telegraphs fade in for longer than the Prediction Window. |
| The low show camera hides floor rings | Choose the pitch in step 2 against ring readability; rings get height and glow. |
| Strong launches tunnel through walls | Swept test; tests at the roster's strongest launches. |
| Benched fighters resume on the wrong tick online | Bench state is in the snapshot; `NetplayLab` covers bench and return under loss. |
| The host's arm and the bomb disagree | Wind-up events lead each launch by a fixed lead; the host clip is retimed to it. |
| `ServerSimulation` grows another special case | Mayhem lives in its own component and partial file, like courses. |
| Protocol and Master releases drift | One coordinated release per bump; Room entry refuses old clients. |
| Snapshot size with four fighters and many hazards | Measure in step 3 against ADR-0039's budget. |
| BREAK OUT! favours some kits | The per-fighter break-out test; tune walls, not kits. |

## Open decisions for the maintainer

**Decided 2026-10-27:** the maintainer approved every recommended default below ("I approve. Go for it").

| # | Decision | Options | Recommended default |
| --- | --- | --- | --- |
| 1 | Match length | Fixed number of rounds · first to N points · timed | **Fixed number of rounds**, a whole number of deck passes; the value is picked in play. |
| 2 | Ties | Shared win · one sudden-death round between the tied · most last-standing bonuses | **Shared win** in the first slice (it exists); sudden death later if ties feel flat. |
| 3 | Damage percent between rounds | Carries over until a KO · resets every round | **Carries over.** Rounds get deadlier as the Match goes, and a knocked-out fighter returns at 0%: a built-in catch-up. |
| 4 | Positions between rounds | Back to the four spawns · stay where you are | **Back to the spawns**, like a Versus point reset: clean telegraph reading and room for BREAK OUT! cages. |
| 5 | Last one standing (proposal) | First slice · later · no | **First slice.** It is cheap and ends dead rounds early. |
| 6 | Knocked-out fighters (proposal) | Wait in the booth until the next round · respawn on the deck without scoring | **The booth.** It keeps the deck readable and gives the host a stage moment. |
| 7 | Manki's favourite (proposal) | First slice · after the first playtests · no | **After the first playtests**, once a runaway leader is actually seen. |
| 8 | Shield against hazards | Blocks them like normal attacks · hazards are unblockable · per hazard | **Per hazard:** bombs blockable, JUMP! rings unblockable (the card says jump). |
| 9 | Escaping BREAK OUT! over the top | Walls too tall to clear · clearable with kit mobility · a lid | **Clearable with kit mobility but not a plain double jump**: any valid escape counts, as on courses. |
| 10 | Where the friendly fire rule is set | Room Lobby, like the co-op setting and Ball Only · on the Mayhem card at Stage Select | **On the Mayhem card**: the rule sits with the Room Pick (ADR-0040 decision 2) and the Lobby gains no third toggle. |
| 11 | Shoves and Hitstun | Push with Hitstun · pure push without Hitstun (like Hose Down) | **With Hitstun**, so a shove can set someone up for a bomb or the edge. |
| 12 | Local Play entry | Dev path only · an Arcade menu entry with CPU seats | **Dev path in step 2**, a menu entry if playing against CPUs is fun. |
| 13 | Show camera toggle | Locked · Y or R3 switches to Steady | **Locked** in the first slice: everyone reads the same view. |

## Verification

Follow [testing](../testing.md) for each step: Shared builds and tests with
`-p:SkipUnityPluginCopy=true`, `dotnet build src/Server/` after GameHost changes, Master
tests in its repository, the Unity compile and console gate through the gateway, and the
checks named above. Keep at most five short human checks in the ignored
`TESTING-UNITY.md`.

## Docs and terms

On approval, ADR-0044 moves to Accepted. When step 3 lands, update the
[Online Arcade brief](online-arcade.md) format list and add Manki Mayhem, Call Card,
Host Entity, Hazard and Runtime Solid to `CONTEXT.md` once they exist in code. The stage
docs drop "plain flat stage until the mode exists" when step 1 bakes the section.
