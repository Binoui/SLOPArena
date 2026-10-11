# Testing and Verification

For gateway operations set `ORCA_TERMINAL_HANDLE` to your own runtime-issued terminal handle; the gateway verifies its incarnation and fails closed if it is missing or mismatched. Use the canonical gateway in [`docs/contributing/unity-cli.md`](contributing/unity-cli.md) and the [shared Editor coordination protocol](file:///home/binoui/Documents/projects/sloparena-workspace/docs/unity-editor-coordination.md). Wait for an independent lease; never inject work into an owner's batch. Runtime ownership status replaces historical Markdown ownership; blocked or unknown is not free.

## Local iteration

Source or asset tuning uses the existing Editor development catalog and the affected
Ability Lab or Training path. A source edit can be compiled in memory without changing
`content-cooked`, generated animation catalogs, or roster pins. Invalid source blocks
preview with diagnostics, not stale fallback. Recompile for changed C#/Shared plugin, or
when Unity requests compilation; do not force a project recompile for prose or every JSON
numeric edit. Read current Unity errors and exercise the changed behavior. Local-only
preview is not persisted package verification.

## Integrated change

Shared edits require focused behavioral coverage while iterating, then `dotnet build
src/Shared/ --nologo` and `dotnet test tests/Shared.Tests/ --nologo` at delivery. Before a
build that copies the Unity plugin, acquire your own bounded gateway hold; run the
build from a separate shell while held, wait for import and compile settlement, then
end the hold. Honor explicit source freezes; coordinate only concrete overlapping
writes or explicitly required stable-source verification under the shared protocol.
No routine Editor-owner messages or blanket acknowledgments. Never copy without
your own lease or nested-claim while holding. Server edits require `dotnet build src/Server/ --nologo` and
`dotnet test tests/Server.Tests/ --nologo`. Unity-facing behavior needs the affected
runtime check and current compile/console evidence, not all unrelated scenes. Accepted
character source/asset changes intended to ship cross the explicit cook/inspect/roster-
refresh boundary before delivery. Documentation/tooling-only edits use the Python checks
from the documentation checker; no Shared build or Unity session is required for this
cleanup.

## Distributable demo

Cook changed accepted packages, verify exact roster identities and all required payloads
in fresh publish outputs, then build and exercise the packaged client/server
join-to-rematch path. The server project and release scripts derive required package IDs
from the roster manifest and verify all four payloads for each admitted package.

### Controller menu navigation

- A / south face button validates the focused control. Home initially focuses a
  mode action; lost page focus is restored to a visible, enabled control.
- B / east face button closes the topmost dialog or menu, otherwise invokes the
  page's Back action, including from top-bar focus or a page text field. Social
  focus returns to the page first. Home has no preceding page.
- Start invokes the page's Continue/Ready/Play action independently of focused
  fighter/stage cards: fighter selection → stage selection → launch; online
  selection → lock-in → leader-only continuation. Disabled/hidden controls stay
  unavailable. Browser continuation joins the last controller-focused room;
  it does not silently create a room or select an arbitrary server.
- Start validates the active identity/address/join dialog instead of advancing
  the covered page. In Settings it accepts the active confirmation or finishes
  Settings; in the shell menu it resumes. Gameplay Start still opens/resumes
  pause. B closes pause, with Resume focused on entry.
- X / west switches top-bar focus; Y / north switches page/social focus.
  Keyboard Escape retains the shell-menu shortcut. Native Steam overlay and
  active rebinding keep input ownership; menu shortcuts do not steal captures.

Test in Unity with a controller: navigate Home → Solo/Training → fighters →
stages without a mouse; select cards with A, continue with Start and return with
B. Repeat from the top bar and after clearing focus. Open Settings, reset/display
confirmation and the address dialog: one B press must close only one layer;
Start must never invoke an obscured page. Verify disabled online continuation,
Room join/lock-in/leader continuation and Results return with a real session.
At 1920×1080 and 1366×768 check focus outlines and controller hints. In Training,
pause, open Settings, return and resume; gameplay must not receive the held menu
button on resume.

2026-10-10 verification: actual runtime sources compiled offline, and the
authorized graceful Editor restart recovered the original retained lease.
The restarted Editor opened the same clean Frontend scene, stopped; native
recompile status was `up_to_date`, `failed=false`, `errors=[]`, and the
pre-Play error console was empty. Entering Play Mode then left the ownership
endpoint unreachable during assembly reload: `sloparena.ui.status` timed out
after 30 seconds, and the gateway's declared stop/release was unverified at
its 300-second deadline. Normal close did not complete. An explicitly authorized
force-close/reopen then restored the same clean Frontend scene, stopped, with
no compilation/reload in progress and an empty current error console. The new
session released its verification lease: free, settled, zero active operations.
Play Mode was not retried; controller/visual acceptance remains unverified.

### Shared build and tests

`src/Shared/` is pure C# `netstandard2.1`. Run after Shared changes:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

Use a focused test filter while iterating, then run the full Shared suite before delivery. Tests should assert observable simulation behavior: state transitions, timing boundaries, collision, damage/Knockback, interruption, deterministic serialization, and catalog identity. Avoid assertions tied only to implementation details or volatile test totals.

Keep mechanic and reporting tests independent of incidental roster tuning. Use explicit
empty-slot fixtures rather than assuming a work-in-progress move remains empty, and assert
facing/lock transitions directly instead of pinning unrelated movement distances. Full-jump
setups must hold through both JumpSquat and the short-hop decision window. Multi-hit tests
should count accepted contacts, not gaps between hitboxes whose active windows may overlap.

Use `TestHelpers.EngineDef` for generic Shared mechanics and reporting tests. It returns
a fresh definition with explicit movement/body values and sixteen empty canonical cooked
slots; install only the timelines or synthetic poses needed by the scenario. Do not load
roster baked data for this fixture. `MankiDef`, `FightGuyDef`, `WibouDef`, and `ResolveDef`
are for intentional roster/package integration. `CombatDef` remains a roster-backed kit
fixture, not the default body for synthetic collision or rollback tests.

Assert the mechanic being protected: authored-duration lock/unlock, accepted contact,
facing/target identity, jump transitions, and deterministic replay. Do not freeze a
character's current damage, startup, body offset, weapon pose, or complete state snapshot
when the test only needs an engine rule. Keep deliberately kit-specific behavior separate;
ordinary numerical tuning does not require golden regeneration.

Exact damage assertions belong in synthetic engine fixtures when damage application,
scaling, or duplicate-hit prevention is the contract. Roster contact/recovery checks
should observe accepted hit identity, victim Hitstop/Hitstun, and attacker recovery.
Do not sum every authored hitbox as expected damage: later hits can miss, and
sweet/sour phases can share hit history. A guaranteed multi-hit combo needs its own
explicit kit requirement and setup. Normal targeting tests must respect authored
acquisition range, bounded turn, and startup cutoff rather than require every move
to snap immediately toward any selected target.

Kit contact scenarios must run through their authored active windows, not a fixed
short tick budget. Check apex release on the observed ascent-to-descent transition.
Cross-up whiffs must pass the opponent's actual position with nonzero momentum
before release; pushbox displacement alone is not cross-up evidence. Stage-directed
recovery may activate a recovery Slot even when an opponent is outward of the stage.

Fixture isolation verified 2026-10-02: the full Shared suite passed with 1,137 tests,
zero failures and two existing skips. A real Shared simulation smoke separated short/full
hops, reflected an explicit fixture jump-force change, and retained the same engine launch
after process-local Manki movement/body perturbation. All 87 protected character-source,
cooked-content and generated-package file hashes matched the pre-cleanup baseline.

Release-preparation cleanup verified 2026-10-08: the full Shared suite passed
1,265 tests with zero failures and two existing skips; Server passed all 27 tests.
The dash traversal regression failed before lifecycle-based cleanup and passed
afterward. The Manki artifact test retains roster/hash admission checks without
pinning current tuning or operation order. Retired Dragon Beam code and positive
admission coverage were removed; unsupported capability rejection remains covered.

Actual Unity runtime and Editor sources compiled offline with the installed Unity
SDK and the latest Shared DLL. An executable smoke against those assemblies loaded
all four pinned packages and exercised idle/shield grab capture/release, scenario
receipt settings, explicit zero-charge input, held leap contact and dash recovery.
These checks are not live Editor/UI, physical input, packaged-player or multiplayer
acceptance. Final plugin refresh and native Lab checks remain pending: an output
consumer failure killed the verification gateway after recompilation, leaving its
lease held/settled with zero operations. Human lease recovery is required; no
timeout takeover or final live-runtime verification is claimed.

### Targeted contract tests

Choose tests that cover the changed boundary:

- movement, jump, forward air dodge, ledge, and air-use behavior;
- hitbox/projectile geometry and collision;
- Hitstun, Hitstop, Knockback, Combo Influence, Clash, and Burst;
- cooked timeline execution, typed operations, capability admission, interruption, and presentation events;
- package compiler diagnostics, deterministic bytes, manifest/hash validation, and Match Content Catalog admission;
- codecs and server/client content requirements.

New observable behavior needs a behavioral test when existing coverage would not fail for a plausible regression. Keep tests in `tests/Shared.Tests/` and use the existing helpers and fixtures.

### Package and cook checks

For a package change, observe status first: `held`/`releasing` means invoke a gateway command and let it wait for an independent lease; `blocked`/unknown means stop. `free`, settled, and zero active operations are availability observations, not a reservation. Each command independently acquires its lease.

```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts status --project-path /home/binoui/Documents/projects/SlopArena/client/Unity
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.character.inspect --target <package>
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.character.cook --target <package>
```

For a rostered package, require a successful semantic result, valid inspect status,
`dirtyOrStale: false`, and matching source/cooked/package hashes. For a source-only probe,
require successful package resolution plus structured diagnostics and semantic cook failure
only when the probe intentionally has unresolved bindings. A failed cook must preserve the
last valid artifact; with no prior artifact, verify the cooked directory remains absent.
Check `content-cooked/<package>/` and the exact roster requirement only when the package is
built-in.

### Bonk pipeline probe evidence

The Bonk probe is not gameplay coverage. Record:

- inspect `success: true`, `packageId: bonk`, and sixteen canonical slots;
- cook semantic `success: true` with the shared dash/hit bindings;
- cooked payload hashes and preservation of `content-cooked/bonk/` after any later failure;
- package-specific stale tracking after Bonk source/catalog/dependency notifications;
- one queue request for repeated notifications, with unrelated package statuses unchanged;
- Unity recompile status and current console errors.

`KitScenario` golden tests begin only after an approved Bonk kit and successful cooked
package exist. The probe deliberately has no damage, timing, recovery, or capability
contract to golden-test.

The maintained FightGuy verifier requires `ORCA_TERMINAL_HANDLE` set to your own
runtime-issued terminal handle. It acquires its own gateway lease and checks the
native inspect result, ignoring the trailing ownership-status object:

```bash
scripts/verify-fightguy-package.sh
```

### Ability Lab and Unity Training

For VFX workflow changes, run **Tools/SlopArena/Tests/Ability Lab VFX Workflow**
in Edit Mode with a valid FightGuy package. Inspect the Console for its explicit
passed message; menu execution alone does not prove the check passed. The check
uses temporary preview objects and does not save/cook package content. Follow
with a native hit/block/whiff scenario pass and verify the original Lab
selection/cursor and clean scene are restored. With an effect visible, recompile
and confirm no duplicate preview effects or prior-domain Playable handles remain.

For integrated Unity-facing changes and accepted package verification:

1. Observe gateway status. If `held`/`releasing`, invoke a gateway command and let it wait for an independent lease; stop on `blocked`/unknown. `free`, settled, and zero active operations are availability observations only. Recompile the Editor when code or the Shared plugin changed, and inspect current Unity errors.
2. Run `EditorDevelopmentContentSelfTest.Run()` (or the named menu item) when
   content-resolution or cook behavior changed. A valid `character.json` edit must
   change the next Editor Training catalog without changing `content-cooked` or the
   generated animation catalog bytes. An invalid operation must report
   `value.out-of-range` at `character.operation.tick` with its code, path, and message,
   block the local catalog, and never use the old persisted package.
3. Confirm `TryBuildPersistedLocalMatchCatalog` still loads the exact persisted roster
   requirement and hashes. Keep the existing `content-cooked` package/release verification.
4. Open the affected package in Ability Lab, preview a valid persisted cooked draft,
   then open Training and exercise movement, the changed move, collision, interruption,
   and landing behavior. Landing presentation crossfades the current aerial pose into
   idle over the replicated `LandingLagTicks / 60` seconds; ordinary zero-lag landings
   transition directly to locomotion, and hitstun interrupts the blend.


#### Native Ability Lab scenario verification

Use the real two-fighter UI or native commands before considering eval. The
command runner uses Shared simulation; never infer hit success from command
success. A focused native pass can exercise these outcomes:

```bash
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.open --target fightguy
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 --opponent idle
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action ground.1 --ticks 60 --distance 1.2 --opponent shield
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action ground.1 --ticks 60 --distance 12 --opponent idle
bun /home/binoui/Documents/projects/sloparena-workspace/scripts/unity-editor-gateway.ts --project-path /home/binoui/Documents/projects/SlopArena/client/Unity -- sloparena.lab.run --action grab --ticks 60 --distance 0.7 --opponent idle
```

Inspect semantic `data.result.success` and diagnostics, plus actual
`scenario.frames[]`, accepted `contacts[]`, grab `interactions[]`,
`presentationEvents[]`, and `deaths[]`. Confirm hit damage/knockback and later
state, block as zero-damage blocked contact, miss as no contact/damage, and grab
capture/release as interaction transitions rather than fabricated hit records.
Frame 0 corresponds to MatchTick 1. Preview and capture with `--action`; capture
must use the matching recorded run/options. It restores prior scenario/cursor,
playback, visibility, camera and render target; verify restoration and cache
outputs where capture is part of the change. Authoring timeline's duration
endpoint remains `durationTicks - 1`, not scenario frame numbering.

Scenarios prepare current source in memory through existing compiler, verified
poses/catalog/rig without save, cook, Undo change or source/cooked writes.
Persisted authoritative preview still depends on valid baked runtime/pose/
catalog/rig data; missing/invalid pose/rig blocks execution. Interpret `dirty`
as workspace edit state and `authoritativePreview` independently. These
isolated scenarios do not prove online behavior or game feel. Use the
[Unity CLI reference](contributing/unity-cli.md) and
[Ability Lab guide](systems/ability-lab.md) for errors, options and output
contracts.

#### Ability Lab scenario evidence (2026-09-30; finalized 2026-10-01)

- Native FightGuy ground.1 at distance 1.2 idle produced one accepted hit at
  frame 5 / MatchTick 6 for 4 damage; distance 12 produced no hit; shield at
  distance 1.2 produced one blocked zero-damage contact.
- Shared grab at distance 0.7 against idle and shield captured at frame 7 /
  MatchTick 8 and released at frame 19 / MatchTick 20 with 6 damage; distance
  12 whiffed. Capturing grab frames 0, 7, 13 and 19 retained the 0.7 m run
  and restored the previous frame 48. Real pair PNGs were inspected at
  `.ability-lab-cache/scenario-verification-grab-20260930`.
- Final Shared dependency build succeeded with 17 warnings and zero errors.
  Focused shield/defense/grab/release tests: 67 passed. Full Shared suite:
  1,111 passed, 43 failed, 6 skipped; no baseline comparison establishes
  attribution, and a green full suite is not claimed.
- Final Unity compilation reported completed, failed=false, errors=[].
  Scenario Outcomes, Scenario Commands, and Scenario Controls menus passed;
  all success markers were observed and current console returned zero errors.
  Checks cover final-Hitstop pose freezing, exact shield collision surfaces,
  guard replay, missing-binding preservation, native contact/interaction
  verdicts, cross-action grab defaults, distinct rendered PNG frames, actual
  UI scrubbing/outcome text, authoring exit, and complete restoration.
  The active Frontend scene remained clean with three roots and Editor stopped.
- The read-only Shared `ServerSimulation.LastTickAttackEntities` diagnostic
  exposes the exact latest attack-collision surfaces, including active
  shields; it complements accepted `contacts[]` and does not change gameplay.
- Desktop screenshot provider did not see Unity; no Unity UI screenshot claim.
  Full Frontend selftest was not run because it saves/cooks content. No online
  match or game-feel claim.

Manual checklist: open FightGuy and view both fighters/Scenario Controls; run
ground.1 at close idle, close shield, and distant idle settings and check
contacts/state; run close and distant grab and inspect paired capture/release
versus whiff; scrub/capture a matching recorded run and confirm prior cursor,
playback, visibility and camera/render target return; verify semantic failures
and diagnostics for invalid action/frame and unsupported Play Mode. Do not save
or cook for these scenarios.

Local iteration uses the transient development catalog and affected runtime path; it
does not require this persisted-catalog self-test for every numerical tuning edit.

The authoritative preview is the cooked Shared path for accepted content. Ability Lab
may show a clearly non-authoritative editing pose for invalid drafts, but Training and
matches must never silently use invalid or stale content.

### Local GameServer/PvP

When the change crosses networking or match composition:

```bash
dotnet build src/Server/ --nologo
dotnet test tests/Server.Tests/ --nologo
```

Exercise a local two-client match where practical. Verify that the GameServer admits the
exact cooked package set, clients receive the same content requirements, attacks resolve
from server state, and respawn/stock flow remains intact. Use [Netcode Architecture](systems/netcode-architecture.md)
for packet and reconciliation details; do not duplicate volatile wire layouts here.

#### Server verification layers

Treat these as separate gates, not interchangeable evidence:

| Layer | Proves | Does not prove |
| --- | --- | --- |
| Shared tests | Deterministic simulation, codecs, content admission | Socket delivery or Unity presentation |
| GameServer tests | Covered server orchestration contracts | A complete live match |
| Master tests (separate repository) | HTTP/SignalR auth, lobby/chat rules, launch requests | PostgreSQL behavior, real GameServer launch, Unity input |
| Local full stack | Actual HTTP/SignalR and UDP lifecycle with real processes | Internet latency/loss, deployment, two-human playability |
| Remote packaged clients | Deployed connectivity and observed human gameplay | Untested failure modes or sustained capacity |

In the Master repository, run:

```bash
dotnet test MasterServer.Tests/MasterServer.Tests.csproj --nologo \
  --logger trx --results-directory /tmp/sloparena-master-results
```

The maintained Master README documents startup, environment variables, and hub
contracts. Its integration tests use real ASP.NET handlers and SignalR clients over
TestServer long polling, isolated EF InMemory databases, and a substituted external
match launcher. Passing them does not prove PostgreSQL transactions or TCP/WebSocket
delivery. Use an isolated PostgreSQL database for the full-stack persistence gate;
do not suppress transaction warnings in production to make an InMemory fixture pass.

#### Local full-stack setup

1. Start a **local Master** using its README, an isolated development database, and
   development-only signing credentials. Record the actual listening URL. Never
   reuse production credentials or point this test at production by accident.
2. Build the GameServer, then make a temporary copy of `src/Server/server.json`.
   Select `deploymentProfile: development` explicitly. Set `masterServerUrl` to
   that listener, `publicIp` to `127.0.0.1` for a same-machine run, an unused base
   `port`, and `arenaDataDir` to the intended baked arenas. TCP control uses the
   base port; UDP match ports span `port` through
   `port + maxConcurrentMatches - 1`. Use reachable public addresses for remote
   clients; do not advertise loopback.
3. Launch the real executable with that explicit configuration:

   ```bash
   dotnet src/Server/bin/Debug/net8.0/SlopArena.Server.dll /absolute/path/to/test-server.json
   ```

   Confirm successful registration and heartbeats in the logs, not merely a
   listening control port. An unavailable Master causes bounded retries; bad
   host credentials or configuration fail startup. Check the selected arena and
   admitted package hashes rather than accepting fallback content as evidence.
4. Configure both clients' Master endpoint **before launch authentication**. Use two
   packaged clients for the remote acceptance pass. One Unity client plus a real
   protocol peer is useful for diagnosis, but is not two-client UI or human-feel proof.
   Use the [Unity CLI workflow](contributing/unity-cli.md) for Editor operations.
5. Confirm distinct player IDs/tags, matching admitted content, and Unity Fixed Timestep
   of 1/60 second. Record the registration/server ID, match ID, and assigned UDP port.

There is currently no maintained one-command full-stack chat smoke harness. The
2026-09-17 pass used a temporary local Master/UDP-peer fixture that was removed after
verification; do not refer to it as an available repository command.

#### Match, membership, and chat acceptance

Run the normal lifecycle first, then repeat it with controlled failures:

| Scenario | Required observation |
| --- | --- |
| Join Room → Character/Arena selection → Match | Two clients receive the same Match admission content; Room chat stays isolated from other Rooms even on one GameHost |
| Combat → stocks/respawn → completion | Damage and outcomes agree with server state; the GameHost's authenticated report returns only the matching Room to Lobby while Results remains viewable |
| Results → Room → rematch | Return validates current membership; picks and Lock-in reset; a new Match starts with fresh IDs while Room conversation and drafts survive |
| GameHost unavailable or full | Room members can prepare/chat without a GameHost; failed start preserves choices for retry, with explicit feedback |
| Room leave, expiry, revocation, or switch | Player returns to browser with an explanation; old Room draft/history is cleared and Room pushes stop; Global and Direct remain usable |
| Hub disconnect/reconnect during gameplay | Same identity/tag; only currently authorized Room restores Server Chat history; gameplay connectivity is separate |
| Offline/rate-rejected send | Draft retained, readable feedback, no automatic replay; uncertain delivery is not presented as confirmed failure |
| Direct, rename, mute | Direct stays private; recipient identity is ID-based; rename does not retarget a conversation; local mute filters the sender |
| Compose during combat | Movement/attacks/camera shortcuts suppressed, simulation and damage continue, Escape preserves draft, held controls require release |
| Invalid or mismatched content | Admission fails explicitly; neither client proceeds with stale/substituted content |

Then exercise the boundaries independently:

- Interrupt **SignalR and UDP separately**. A restored chat connection is not proof of
  recovered gameplay. Record both transports' state and the exact recovery behavior.
- Disconnect the leader during selection and a player during a Match. Check
  leader transfer, roster state, Match cleanup, and whether the remaining client can continue.
- Restart a GameHost and cancel a Match for missing players. Neither cancellation
  invents a winner or resets another Room, and old reports must not reset a rematch.
- Leave a browser open as another player creates, fills, starts and finishes a
  Room; rows must refresh without a manual scan. Cancel a slow Room create/join,
  then re-enter; stale cleanup must not revoke a newly adopted Room.
- Crash a GameHost without deregistration after a Match starts. After 60 seconds
  without a heartbeat and a failed control `/health`, only its open Matches
  cancel with `host_unavailable`; a reachable `/health` must not abort a fight
  merely because Master missed a heartbeat. Recheck no winner and Room chat.
- In explicit Editor development UDP, an unfilled Room Match abort must correlate
  its root Match ID, leave gameplay without Results, and return to that Room.
- Restart Master separately; record lost in-memory Room state and surfaced errors.
- Run two concurrent Matches on one GameHost and repeated join/leave/rematch cycles.
  Check isolation, Room state, result association, and cross-Room chat boundaries.
- On an isolated test network, introduce latency, jitter, loss, and a brief outage.
  Record the actual impairment settings and remove them afterward. No network-fault
  tool is assumed installed; do not alter the development machine's shared route blindly.
- Exercise real credential renewal and expiry without silently creating another guest.
  Keep tokens and signing secrets out of saved evidence.

#### Evidence and next-pass priorities

Capture server/client logs with timestamps, player/server/match IDs, package hashes,
transport state, authoritative and local ticks, observed action/phase, result-report
HTTP status, and Unity console errors. Keep TRX files and screenshots outside Unity
`Assets`. Distinguish process startup, successful admission, actual packet receipt,
and successful UI transition. Do not force a client into Results to claim lifecycle
success. Restore test preferences, Editor scene/view settings, processes, and temporary
configuration when finished.

The 2026-09-17 chat pass completed a real local match → Results → rematch and explicit
Leave using Unity plus a protocol peer. It also repaired accidental Training ownership
in the PvP scene and corrected Unity's 50 Hz fixed loop to 60 Hz. This was not a broad
server reliability sign-off: local prediction still lagged authoritative state
(one sample: local Countdown at tick 5507 versus server GO at tick 5597), and the full
Shared suite still had gameplay/content failures. The next pass should prioritize:

1. Reproduce and diagnose prediction/phase divergence with both clients' tick evidence.
2. Establish the full Shared failure baseline; do not label unclassified failures pre-existing.
3. Verify PostgreSQL result persistence and the complete lifecycle with two packaged clients.
4. Exercise reconnects, restarts, impaired networks, and concurrent match isolation.

The gitignored root `TESTING-UNITY.md` records the session-specific checks. Temporary
evidence under `/tmp/sloparena-chat-evidence/` is local and disposable, not durable CI
coverage or a prerequisite for running this procedure.

### Verification evidence

Record:

- commands run and whether they passed;
- package/cook status and hashes when content changed;
- Unity console status and the Training/Ability Lab surface exercised;
- the local server/client path exercised when applicable;
- any unexercised runtime surface.

Do not claim a live runtime result from a build or static inspection alone.

### Steam Playtest release repair evidence — 2026-10-01

- Full Shared suite after the integrated repairs: **1,165 passed, 6 existing skips,
  zero failures**. Server suite: **26 passed**. MasterServer suite: **158 passed**;
  its registration/launcher suites also passed all **40** cases after independent
  protocol-4 fixtures were finalized.
- The isolated backend smoke exercised actual Master HTTP registration with EF
  InMemory storage: protocol 2 returned 400 without creating a host; protocol 4
  returned 200 and persisted the approved host. It does not prove PostgreSQL or
  live VPS behavior.
- Snapshot smoke retained shield-drop timer 6 through wire decode, corrected air
  timer 999 to 37, and preserved local-only attack timer 500. Legacy snapshot
  collision and lock acquisition are covered by the passing Shared suite.
- Unity reported ready/stopped with zero current console errors. The initial repaired
  Windows `0.2.0-playtest.4` candidate built successfully with **1,309 warnings and
  zero errors**, with its four cooked packages matching the checkpoint roster.
  The operator subsequently approved the coherent Manki renderer/weapon/binding
  update for the final source-pinned rebuild. The packaged VPS endpoint and matching
  Steam native library were checked; developer bootstrap/private config files were
  absent. Final build/source/content identities belong in the release receipt.
- Existing project credits, MIT license, and both font OFL licenses accompany the
  candidate. Release evidence is retained under ignored `build/playtest/0.2.0-playtest.4/`.
- The attempted Proton player smoke was cancelled at the operator's request; no
  subsequent player launch, Steam install, or two-account match/rematch was
  exercised. Source publication, redacted history scan, Steam upload, and live
  deployment are separate gated operations; see the rollout evidence below.

### Steam Playtest rollout — 0.2.0-playtest.4

- Final Windows build: **3 warnings, zero errors**, game source `f830ad2`,
  Master source `f016616`. Steam upload succeeded as **BuildID 25663418**,
  depot **5325921**, manifest **5276771382055694993**.
- VPS release **`steam-playtest-20261001-4`** was deployed through the guarded
  release procedure using the immutable images in the local release receipt.
  Zero active matches were observed before replacement. Both Master and
  GameHost `/ready` passed, as did public HTTPS Master readiness; fresh host
  registration reports **protocol 4** and catalog
  `2d31ca65507f8ce30a2fae29aa1a2bd507da6cce4905a3543f98f678ad8e441a`.
- The database advanced from `20260926000000_AddSteamMatchRouting` to
  `20260927000001_HashGameServerApiTokens`. A fresh off-host archive was
  downloaded, checksum-verified, and restored on an internal-only Docker
  network; migration history, persisted table counts, and isolated Master
  readiness passed. Token hashing is irreversible: the older application pair
  is not a schema-compatible rollback.
- Initial startup failed because production images require explicit
  `Room__AdmittedCharacters__*`, `Room__AdmittedArenas__*`, and
  `Room__CatalogHash`. Adding the approved Manki/FightGuy/Wibou/Bonk roster,
  `slop_pit`/`slop_court`, and the exact catalog pin to the private Master
  environment enabled a successful guarded roll-forward. Existing secrets
  were preserved; no public guest or gameplay UDP fallback was enabled.
- The installed isolated recovery fixture also lacked mandatory Steam auth
  configuration. Its explicit Steam readiness-test fields and inherited
  non-secret room admissions are now represented in `deploy/vps/recovery.py`.
  A readiness test key is not a publisher key and does not prove ticket admission.
  The corrected installed fixture also completed a fresh post-migration
  off-host restore: all seven migration IDs, persisted table counts, and
  isolated Master readiness passed; its temporary containers/network/volume
  were removed.
- Authenticated Steamworks confirms **default → BuildID 25663418**, depot
  manifest **5276771382055694993**, after the Steam mobile confirmation request.
  The separate `test` branch remains on **25547241**. The branch observation
  and cropped screenshot are retained in the local release evidence directory.
- Receipts and private VPS operator evidence retain the observed release,
  image/source identities, backups, recovery, and registration. A packaged
  two-account match/rematch remains unverified; no player was launched.

### Steam Playtest rollout — 0.2.0-playtest.5

- Windows candidate built from game checkpoint `fe98065`: **1,679 warnings,
  zero errors**. All sixteen cooked payloads and the roster matched the source
  bytes; the packaged Shared assembly and compiled VPS endpoint were verified.
  The real Shared loader admitted the packaged four-character catalog.
- Shared suite: **1,189 passed, 2 existing skips**; Server suite: **26 passed**.
  Both image workflows passed their actual runtime smoke gates. Source CI passed
  after documentation-only link repair `42d9bff`; the history secret scan passed.
- VPS release **`steam-playtest-20261004-5`** runs the immutable GameHost,
  Master and migration images recorded under ignored
  `build/playtest/0.2.0-playtest.5/`. Game source is `fe98065`; Master remains
  `f016616`. A fresh off-host backup succeeded and zero active matches were
  observed immediately before replacement. No schema migration was needed.
- Both services' liveness/readiness and external HTTPS Master readiness passed.
  Fresh registration confirms **protocol 4** and catalog
  `b38f85081bc7a33a284b533576fea8a10c5c2c949aa7216375929bdbda677a0d`.
  Only the private Master catalog admission pin changed; credentials were
  preserved. Unauthenticated server discovery returned 401 and guest
  authentication POST returned 404.
- Authenticated Steamworks and the activation banner confirm
  **default → BuildID 25702912**, depot **5325921**, manifest
  **4539020397009519851**, after mobile confirmation. The separate `test`
  branch remains unchanged at **25547241**.
- Local staging was restored. No player was launched; packaged two-account
  match/rematch and a new isolated backup restore were not exercised.
