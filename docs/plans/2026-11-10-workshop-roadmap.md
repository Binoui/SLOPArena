# Workshop roadmap: from the current build to shippable Steam Workshop support

**Status:** Phase 0 started on the owner's go, 2026-11-10. Each later phase starts on the owner's
go. Phase 0 ends with ADR amendments that need approval before a later phase builds on them. The
owner's target is to leave the Steam Playtest when Workshop support is ready.
**Builds on:** [ADR-0022](../adr/0022-workshop-first-content-architecture.md) through
[ADR-0031](../adr/0031-character-asset-dependency-classification.md).
**Proposes to replace:** the Editor-first creator route in the
[Workshop creator setup](2026-10-13-public-reference-kit.md).

The goal is that a player with the Steam build can make a fighter, publish it on Steam Workshop,
and play it online with friends. The gameplay half of that goal is built. Fighters are hashed data
packages, the compiler has a `Workshop` profile, and every Match pins exact content. The delivery
half is missing. Nothing loads a fighter from outside the game build, no Steam Workshop call
exists, and a Room cannot name a package.

Five phases close the gap. Every phase after Phase 0 ends in something a player can use, and every
step ends in a check. Phase 0 proves four unknowns. Phase 1 ships model swaps. Phase 2 ships custom
fighters in local play. Phase 3 takes them online. Phase 4 adds stages. Phase 5 adds depth where
demand shows.

## What this plan recommends

1. **Make the installed game the creator tool, and use open formats.** A Workshop fighter is the
   existing cooked gameplay files plus one GLB file for the model and its clips. A creator needs
   the game, a rigged model, and a text editor. A creator does not need Unity. Rivals 2 asks
   creators to install an Unreal modkit and has 148 public items after ten months. Rivals 1 asked
   for sprites and script files and had 2,299 items after ten months (Appendix B).
2. **Ship model swaps first.** A model swap puts a creator's model on a built-in fighter's kit and
   animations. It never reaches the simulation, so it needs no server change and no validator. It
   also builds the parts every later phase reuses: Steam upload and download, the runtime model
   loader, Room sync, and reporting.
3. **Play Workshop fighters online on the official GameServers, in Rooms whose leader turned
   Workshop on.** The GameServer already logs on to Steam as an anonymous game server, and Steam
   lets such a server download Workshop items (Appendix C). Standard Rooms keep the built-in
   roster.

## What shippable means

A stranger with the Steam build and no access to this repository can do all six steps.

1. Make an item with tools they can get for free.
2. Test the item locally in the real game.
3. Publish the item to Steam Workshop from the game.
4. Find and install another player's item.
5. Play the item online with players who did not have it before the Match.
6. Meet a broken or hostile item without a crash, a hang, or a desynced Match.

Phase 1 delivers all six for model swaps. Phase 2 delivers steps 1 to 4 for fighters. Phase 3
delivers steps 5 and 6 for fighters.

## Decisions

No agent can settle these by running something. The owner answered D1, D4, D5, and D7 on
2026-11-10 and started the plan with the other defaults unchanged.

| # | Decision | State on 2026-11-10 |
| --- | --- | --- |
| D1 | Lift the expansion freeze for Workshop work. `AGENTS.md` gave priority to the friends demo over platform expansion, and [the demo reset](2026-09-05-playable-demo-reset.md) says "Freeze expansion" for Workshop generalization (line 51). | Lifted. The owner did not know why the rule was still there. `AGENTS.md` no longer carries it, and the demo reset plan notes the change. |
| D2 | The creator route. ADR-0027 and the 2026-10-13 plan chose the Unity Editor as the ModKit. | Approved on 2026-11-11 as [ADR-0064](../adr/0064-workshop-creator-route.md): the installed game plus open formats, with Unity as a second exporter for contributors. |
| D3 | The first content type. | The default stands: model swaps, then fighters, then stages. |
| D4 | The Steam app that hosts the Workshop. The game ships through the Playtest app (5325920) today, and the store app (5324910) is unreleased. | The store app. The game leaves the Playtest when Workshop support is ready. The owner decides when that is. |
| D5 | Content rules. In Rivals 2, 21 of the 30 most subscribed items are other companies' characters, by my reading of their names. | Decided 2026-11-10: guidelines, not rules. The game has no monetization. Publish short guidelines, add an "AI assisted" tag as Rivals 2 has, and leave rights claims to Steam's takedown process. |
| D6 | Where Workshop fighters may play online. Nothing checks their balance. | The default stands: only in Rooms whose leader turned Workshop on. |
| D7 | A glTF importer dependency for the Unity client. `AGENTS.md` requires approval before tools are installed. | Approved for the spike. Check 0.1 picks the package. |
| D8 | What a made fighter is at first: a remix of an existing kit, a kit built from the engine's parts, or a fully original fighter with its own animations. | Decided 2026-11-11: built from parts. A creator's model with moves assembled from the public building blocks. A remix is the same thing started from a copy. |
| D9 | How a creator writes moves. | Decided 2026-11-11: a text file first. The goal is that people author either with an editor UI (the Ability Lab, later in the game) or with AI agents working from a reference. Both write the same source file. |
| D10 | Where a Workshop fighter's animations come from. | Decided 2026-11-11: both the clips in the creator's GLB and a shared library of the game's own clips. Which built-in clips may enter the library depends on who made them and is still open. |
| D11 | Where made fighters appear at Fighter Select. | Decided 2026-11-12: in the same grid as built-ins, with a Workshop badge. |
| D12 | Which modes take made fighters first. | Decided 2026-11-12: Brawl, Training, and couch Matches. Each Arcade mode opens later. |
| D13 | A made fighter's portrait. | Decided 2026-11-12: rendered from the model when the creator gives none. |

## Phase 0. Prove four unknowns

Phase 0 is throwaway work on spike branches. Nothing from it merges except the harness in 0.4 and
the ADR text in 0.5. Steps 0.1 to 0.4 are independent and can run in parallel.

### 0.1 Show a fighter from a GLB file in a player build

The web demo already exports every fighter to GLB with one 60 Hz clip per semantic animation ID
(`client/Web/tools/fighter_glb.py`). Use `client/Web/assets/fighters/fightguy.glb` as the input, so
the spike needs no new art.

1. Load the GLB at runtime in a built player. Do not use the Editor or the generated catalog under
   `Resources/Generated/CharacterPackages`.
2. Give the model the `SLOPArena/GraphicCharacter` look. The pass in
   `client/Unity/Assets/Scripts/Runtime/World/MatchVisualStyle.cs:76` copies only the base map, the
   base colour, and the cull mode, so a GLB with a base-colour texture is enough.
3. Prototype two playback paths and compare them. Path A plays the GLB's own clips by setting
   joint transforms from the samples for the current simulation tick. Path B builds a Humanoid
   avatar at runtime with `AvatarBuilder.BuildHumanAvatar` and plays the built-in Humanoid clips
   through Animancer on the imported skeleton.

**Check.** A player build shows the GLB FightGuy beside the built-in FightGuy through idle, run,
jump, one normal, and one special. Save a side-by-side capture and the frame time of both. Record
which path played each clip, and which importer the spike used. The result picks D7. It also
decides whether path B is good enough for model swaps.

### 0.2 Cook a fighter without the Unity Editor

Today the cook needs the Editor to bake `poses.bin` and to fit capsules
(`client/Unity/Assets/Scripts/Editor/DeterministicPoseTrackBaker.cs`,
`tools/character_sizes/hurtbox_fit.eval.cs`). `poses.bin` holds joint positions relative to the
hips, 19 to 28 points per fighter, for 22 to 34 animations.

1. Bake the same points from the clips in `fightguy.glb` with plain C#.
2. Port the capsule fit so that it runs outside the Editor.

**Check.** Record the largest point error per animation against `content-cooked/fightguy/poses.bin`.
Run one scripted bot Match with each pose file and compare hit outcomes. Compare like with like.
The Unity cook adds the mesh-fitted capsules, so offline compiler output never equals the cooked
bytes. The export reports under `client/Web/assets/fighters/` already show the GLB route within
2.1 mm of Unity's skinned vertices for all seven fighters. If the bake also matches, a creator
needs no Unity install.

### 0.3 Send one item through Steam and back

1. The owner enables the Workshop on the chosen app in Steamworks. Set visibility to Developers
   Only, enable `ISteamUGC` file transfer, and set a Steam Cloud quota, which Steam needs for
   preview images.
2. A development build calls `SteamUGC.CreateItem`, `StartItemUpdate`, `SetItemContent`, and
   `SubmitItemUpdate` on a small folder.
3. A second account subscribes, and the client reads the folder through `GetItemInstallInfo`.
4. The VPS GameServer calls `SteamGameServerUGC.DownloadItem` inside the session it already opens
   with `SteamGameServer.LogOnAnonymous()` (`src/Server/SteamGameServerHost.cs:164`).

**Check.** The SHA-256 of every file is equal on the uploader, the subscriber, and the VPS. Record
whether a Playtest build can see and create items on the store app's Workshop. D4 already places
the Workshop on the store app. This answer decides whether testers who own only the Playtest app
can try Workshop content before release.

### 0.4 Attack the cooked package loader

Write a harness in `tests/Shared.Tests` that mutates each of the seven cooked packages, loads the
result with `CookedCharacterPackageLoader.LoadFiles`, and runs `ServerSimulation` for a fixed number
of ticks under a time limit and an allocation limit. Pass a fresh copy of the input dictionary to
every tick, because `Tick` rewrites consumed presses in place.

**Check.** The harness prints every crash, hang, and budget overrun with the mutation that caused
it. Expect findings. A static read already suggests an empty-stage timeline reaches an unchecked
index at `src/Shared/Abilities/CookedTimelineAbility.cs:373`. The list becomes the backlog for
step 3.1.

### 0.5 Amend the ADRs

Write the amendment to ADR-0027 for the creator route, and one new ADR for the Workshop item
format. The new ADR names the presentation payload, the package ID rule for Workshop items, and
what happens to published items when the cooked schema changes. Attach the evidence from 0.1 to
0.4, then run the `interrogate` skill on the ADR text.

**Check.** The owner approves or rejects each ADR. Later phases start only on approved text.

### Phase 0 status

- 2026-11-10. Step 0.2 passed with conditions. A throwaway bake from the GLB matches Unity's
  `poses.bin` within 0.40 mm on FightGuy, Wibou, and Bonk, and a 160-line port of the capsule fit
  lands within 0.6 mm. FightGuy was re-checked with a separate reader: 0.092 mm at most over 25,783
  point-frames against the cook at `9aff4162`. A five-minute bot Match gave the same hit events and
  the same compared fighter fields (position, velocity, damage, stocks) on every tick. Full world
  snapshots still differed on 2,598 of 18,000 ticks in the mirror Match and on 1,801 of 18,000 in
  the four-fighter Match, and the cause is not identified. The item format must carry four things: each clip's frame count, a
  human-bone map, weapon marker nodes, and one sampler shared by the bake and the renderer.
  Still unproven: clips that never went through Unity's humanoid solve, tails, shells, forms, and
  a C# port. Evidence is in `tmp/workshop-spikes/pose-bake/results.txt`.
- 2026-11-10. Closed from step 0.2. The prototype measured 139 mm on Wibou and 102 mm on Bonk
  between the baked blade tip and the weapon marker in the web demo's GLB. The game itself draws
  the weapon with the same rule the baker uses: the prefab keeps its own scale, at the bone's
  position and rotation (`WeaponAttach.cs:92` and `:398`, `DeterministicPoseTrackBaker.cs:204`).
  So the difference is in the web export, which hangs the weapon under the scaled hand bone. A
  Workshop bake copies the game's rule. Read from code, not captured in game.
- 2026-11-10. Step 0.4 changed shape. The Codex provider refused the brief for a random-variation
  tool twice, and the Opus run stopped itself before writing code when a content filter withheld
  part of its plan. The step now asks for named negative tests in `tests/Shared.Tests`, with each
  known gap skipped and labelled for step 3.1. A fresh Opus run has that brief.
- 2026-11-10. Step 0.4 produced 23 named cases, 41 tests with the helper checks, in two new files
  for `tests/Shared.Tests`. 33 pass and 8 are skipped as known gaps. Nothing is committed. A second
  run with the skips bypassed reproduced all eight gaps, and its output is saved beside the tests.
  A package that adds a `slop.internal.*` capability loads and plays. A timeline with zero stages
  throws at `CookedTimelineAbility.cs:373` when the move starts, inside `ServerSimulation.Tick`.
  2,000 hitboxes on one tick load and cost one tick of 410 to 700 ms, against a median tick near
  0.03 ms. 20,000 extra operations load and cost one tick of 4 to 35 ms. Stored budgets are kept
  as written. Projectile speed and lifetime have no bound. An unknown slot behavior value loads. A
  package with a slot removed loads. Every pose file, bindings, and manifest case is refused
  today. The work is saved in `tmp/workshop-spikes/loader-contract-tests/`.
- 2026-11-10. Limits of step 0.4, from the cross-model review of the decision log. The tests
  measure tick time only, not allocations or memory. Each case edits one fighter, not all seven.
  The wall-clock guard covers the Match and not the loading call, so a load that never returns
  would stall the test run. The tick budget is wall-clock time and may fail on a busy CI runner.
  Fix the last two before the tests merge.
- 2026-11-10. Step 0.1 passed. A Win64 player with no Editor and no generated catalog loaded
  `fightguy.glb` from `StreamingAssets` through glTFast 6.18.0 in 111 to 123 ms. Path A, the
  GLB's own clips set as joint transforms, stays within 0.085 mm of the built-in fighter on every
  tick of five clips, except 0.454 mm on the idle loop's last tick. Path B, a Humanoid avatar
  built at runtime playing the built-in clips, matches the body within 0.05 mm and is off by up to
  30 mm at the thumbs, because the GLB's rest pose is not the avatar's reference pose. Model swaps
  through path B work for `bonk.glb` and `bashie.glb`. `wibou.glb` rests in an A-pose and its arms
  sit about 43 degrees low. Each added fighter costs 0.02 to 0.25 ms of frame time, inside the
  run-to-run spread. Path A serves custom fighters and path B serves model swaps. A GLB for path B
  needs a T-pose rest pose with fingers, a map that names Hips, and a fit to the base fighter's
  height. Unproven: game weapon attachment, effects, sound, tails, shells, forms, `PlayerRenderer`
  states, Steam Deck, memory, and GLBs that Unity did not export. The spike is on the local branch
  `t3/runtime-glb-fighter-prototype` (`aa6daf0a`), and its report and captures are in
  `tmp/workshop-spikes/glb-runtime/`. The step 0.4 tests are on `t3/test-cooked-package-loader`
  (`24d6bd15`).
- 2026-11-10. Found by step 0.1, outside Workshop. Player builds do not contain the
  `SLOPArena/GraphicCharacter` shader. Nothing references it and it is not in Always Included
  Shaders, so `Shader.Find` returns null in the player and `MatchVisualStyle` leaves fighters on
  URP Lit. The spike used a copy under `Resources`. Main is not changed.
- 2026-11-10. Builder note from step 0.1. The cook source hash tracks `Packages/manifest.json`, so
  adding any client package marks all seven cooks stale. Phase 1 must recook when it adds the
  importer.
- 2026-11-10. The owner enabled the Workshop on the store app in Steamworks, with a Steam Cloud
  quota of 1 GB and 1,000 files per user.
- 2026-11-10. Step 0.3, two of three legs pass. A private test item (3837402870) uploaded from a
  small tool as the owner's account, and the same account downloaded it with equal SHA-256 on
  every file. The anonymous game-server download failed with `k_EResultFail`. Two causes are
  still mixed: the Workshop setting "Enable anonymous game servers to download workshop items",
  and the item being private in a Workshop that is visible to developers only. Evidence is in
  `tmp/workshop-spikes/steam-roundtrip/`. The first attempts returned `k_EResultBusy` because the
  settings had been made on the Playtest app.
- 2026-11-11. Step 0.3, server leg retried after the owner enabled anonymous game-server
  downloads. It still returns `k_EResultFail`. The setting is no longer the suspect if the change
  was published in Steamworks. What remains is the item being private, the Workshop being visible
  to developers only, or the change not yet published. The next check is the same download with
  the item set to unlisted.
- 2026-11-11. Step 0.1b started on the owner's word: Dusty's raw Tripo export goes into the spike
  build as a model swap on FightGuy's moves, with no Unity step for the creator. Its verdict
  decides between the GLB route in ADR-0064 and a Unity modkit as the first creator route.
- 2026-11-11. Step 0.1b passed with two creator requirements. The raw Tripo file, converted to GLB
  by Blender at default settings, played five FightGuy clips in a player build with no hand fix.
  Its body joints stay within 24 mm of the shipped Dusty, which is built from the same Tripo
  file. The game needs four rules that read only the file: a bone map from Mixamo names, an
  upright rule, a size rule and a palm rule. Loaded as-is the model lies on the floor, because
  Tripo stores its rest pose face down. Creators must supply Mixamo bone names or a bone-map
  file, and a T-pose rest. The tail and mitten hands are worse than the shipped Dusty and only a
  creator can fix them. The 4096 px texture costs about 64 MB and a 250 ms frame at load, so the
  item rules need a texture budget. Not tested: Tripo's own GLB export, other models, an A-pose
  rest, and the real fighter path through `PlayerRenderer`. Evidence is in
  `tmp/workshop-spikes/tripo-swap/`; the code is on branch `t3/tripo-glb-model-swap`.
- 2026-11-11. Step 0.5 done. The owner approved
  [ADR-0064](../adr/0064-workshop-creator-route.md) and
  [ADR-0065](../adr/0065-workshop-item-format.md) after step 0.1b. ADR-0065 now carries its three
  corrections: no bone-map file for Mixamo names, fingers optional, and a texture budget. The
  `interrogate` review of the ADR text was skipped. Phase 0 is closed except for the server leg
  of step 0.3, which does not change the item format. Phase 1 may start.
- 2026-11-11. Step 0.3, server leg: the item's visibility is ruled out. The owner published the
  Steamworks change, and the anonymous game-server download still returned `k_EResultFail` with
  the test item private, unlisted, and public. The item is private again. What remains is the
  Workshop being visible to developers only on an unreleased app. Model swaps do not need this
  leg, because only clients download them. Retest when step 1.8 opens the Workshop, and before
  Phase 3. If it still fails then, the other route is a game-server login token in place of the
  anonymous login.
- 2026-11-11. Phase 1 started on the owner's word. Steps 1.1 and 1.2 run in parallel in their own
  worktrees: the item manifest and a GLB pre-check in Shared, and the importer with the swap in
  the real fighter path.
- 2026-11-11. Step 1.1 landed (PR #346): `WorkshopItemManifest` and `WorkshopGlbPrecheck` in
  Shared. The owner confirmed the default limits: 30 MB, 250,000 triangles, 250 bones, 4096 px
  textures, 20 materials, 16 images.
- 2026-11-11. Steps 1.2 and 1.3 built. A player wears a creator's GLB over a built-in fighter in
  a real match through `PlayerRenderer`; see [model swaps](../systems/model-swaps.md). FightGuy,
  Sir Bonk, Wibou, Manki, and Dusty accept a swap. Bashie and Nyx refuse with a reason. Swap
  textures shrink to 1024 px on a worker thread, so a mid-match import's worst frame fell from
  358 ms to about 30 ms. A cross-model review found four defects (a node cycle that hangs the
  importer, reads before the size check, a second PNG header, and an unstyled model after
  take-off). All four are fixed, with tests or a player-build run for each. The roster was
  recooked in a disposable project, and only hashes and pins changed, so the match content hash
  changes with this step. For now a swap comes from the `-modelSwap` launch option or the
  `ModelSwaps` folder. Not done: the Steam Deck frame time, the Workshop screen (1.4), Fighter
  Select (1.5), and online (1.6).
- 2026-11-11. Step 1.4 built: a Workshop screen on Home, described in
  [model swaps](../systems/model-swaps.md). A player imports a GLB into a local library,
  previews it in Training, wears it across launches, publishes or updates it on Steam, and lists
  published and subscribed items. The Steam calls sit in `SteamWorkshopClient`, which has no
  Unity types; a console program drove it against real Steam to publish, update, list, download,
  and verify one private item. A model swap item may now carry an optional `humanoid.json`. A
  cross-model review found ten defects, among them a preview that leaked into later matches, a
  saved choice that followed entity 1 online, hashing on the main thread, and updates that could
  widen an item's visibility. All ten are fixed with a test or a player-build run. In a Playtest
  build the Steam parts say the Workshop is not available, because the Workshop lives on the
  store app. The owner chose to merge the screen with its plain look and restyle it with Fighter
  Select in step 1.5. Not verified: publishing from the Windows build, controller and Steam Deck
  input, a long upload, the legal-agreement prompt, and the second-account check.
- 2026-11-11. QA on the Workshop screen failed on two points, both fixed in PR #349: the menu's
  modal state now resets at every Play, and the screen's pickers and navigation work with a
  controller. A real controller and the Steam Deck are still untested.
- 2026-11-11. Phase 1 is parked after step 1.4 on the owner's word. Steps 1.5 to 1.7 polish
  skins and do not bring the creator tool closer, which is the point of the Workshop. Step 1.8
  waits with them. Skins work from the Workshop screen as they are.
- 2026-11-11. Phase 2 starts with decisions D8 to D10 and one proof before any screen is
  designed: one custom fighter built from parts, made by hand and taken into a player build, to
  list what is missing. It is a throwaway and does not merge.
- 2026-11-12. The proof worked. A fighter that exists only as files (a JSON kit, a GLB, and
  four cooked files that the player build wrote itself in about 2 seconds) fought built-in
  fighters in a player build, used all 14 slots, and was KO'd and respawned. The largest gap is
  the match path keyed on `CharacterClass`: the proof had to take over the Salamander slot. An
  agent wrote the kit in about four minutes, but not from the docs alone.
- 2026-11-12. The owner approved the design for fighters identified by their package, recorded
  as [ADR-0067](../adr/0067-fighters-by-package.md), with decisions D11 to D13. Two designs were
  sketched from one census of the code (373 non-test lines in 59 files depend on the fixed
  list) and scored by a third pass. The roll-out has six units. The owner's animation inventory
  and a Steam Deck measurement are needed before units 4c and 4a set their final values.
- 2026-11-12. Phase 2 step 7 built ahead of the others: the
  [authoring reference](../characters/workshop-authoring-reference.md) and a kit checker,
  `tools/KitCheck`. Every source field now sits in one definition in Shared,
  `CharacterSourceSchema`, that the reader and compiler validate from and the reference is
  printed from. An editor can read the same definition later. Two fresh agents, given only the
  reference, the examples, and the checker, each wrote a complete kit that compiled, on the
  second and on the first checker run. Compiling does not show whether a move plays as meant,
  so authors still need the in-game preview. The compiler rewrite gives the same output as
  before on 57,524 altered sources, and a cold cook reproduced the runtime, pose, and binding
  files of all seven fighters byte for byte. Four manifests were recooked, because a manifest
  stores its compile warnings and their paths now name the entry at fault.
- 2026-11-12. ADR-0067 unit 1 built: a local Match can hold a fighter with no selector. An
  admitted fighter is a type only Shared can make. Building a catalog needs a policy, and a
  standard Room accepts only packages pinned by the roster the application shipped. Local
  Matches write handle map schema 3, and new local replays store their content map. The online
  schema 2 bytes and hash are pinned by a test that runs the real server path. The player's
  class and package can only be set together. Nothing a player sees changes, and no fixture is
  reachable outside tests.

## Phase 1. Ship model swaps

A model swap is a Workshop item of type `model-swap`. It holds one rigged humanoid GLB and names
the built-in fighter it dresses. The fighter's cooked package, hashes, hitboxes, and hurtboxes do
not change.

1. **Define the item manifest.** Add `item.json` with a strict codec and tests in Shared. Fields:
   schema version, type, item version, the SHA-256 of each payload file, the base fighter's
   package ID, creator, license, attribution, and an AI-assisted flag.
   **Check.** Unknown fields, missing fields, and wrong hashes fail with a named diagnostic.
2. **Import with limits.** Refuse a GLB above fixed limits for file size, triangles, bones, and
   texture size. Scale the model to the base fighter's height. Map its bones to Humanoid. Attach
   the base fighter's weapons to the mapped hand bones.
   **Check.** A GLB above any limit is refused, and the message names the limit.
3. **Say which fighters accept a swap.** Start with fighters whose presentation does not reshape
   the body. Bashie's shell and Nyx's two forms use their own rig scripts
   (`ShellTuckRig.cs`, `FormPresentation.cs`), so they wait.
   **Check.** Each accepted fighter plays idle, run, jump, three attacks, hitstun, and KO with a
   swapped model in a player build.
4. **Add the Workshop screen.** Import from disk, preview in Training, publish, list my items, and
   list subscribed items. After `CreateItem`, open the item's Steam page when
   `m_bUserNeedsToAcceptWorkshopLegalAgreement` is true, because Steam hides the item until the
   uploader accepts.
   **Check.** A second Steam account finds, subscribes to, and uses an item that the first
   account published.
5. **Pick a swap at Fighter Select** in Solo, Training, and couch play.
6. **Show swaps online.** Carry the published file ID and the payload hash per Room member through
   the Master (`RoomMember` in `Rooms/RoomModels.cs`) and to clients in the match start
   configuration. The GameServer ignores both fields. Each client calls `SteamUGC.DownloadItem`,
   verifies the hash, and shows the built-in model until the item is ready or if it fails.
   **Check.** Two accounts in one Room each see the other's model. A changed file shows the
   built-in model. The GameServer's match content hash and tick time are the same as before.
7. **Let players hide and report.** Add a "Show Workshop models" setting with three values:
   everyone, friends, and nobody. Add a content kind to the in-game feedback of ADR-0062, which
   already sends package ID, version, hash, and a screenshot.
   **Check.** A client set to "nobody" sees built-in models in the same Match.
8. **Open the Workshop.** Add branding and tags, publish the first items, set visibility to
   Everyone, and publish a how-to page on playsloparena.com.
   **Check.** Measure frame time on the Steam Deck with four swapped models against the same Match
   with built-in models, and record both numbers before the release.

## Phase 2. Custom fighters in local play

A Workshop fighter is an item of type `fighter`. It holds the four cooked files that exist today,
one GLB, a portrait, and the editable source.

1. **Make bindings portable.** `client.bindings` stores Unity Editor object IDs today
   (`client/Unity/Assets/Scripts/Editor/CharacterBindingWriter.cs:25`). Add a binding schema that
   names GLB clips and registry IDs. Built-in packages keep the current schema.
2. **Add the built-in presentation registry** that ADR-0023 describes. Give stable `slop.vfx.*`,
   `slop.sfx.*`, and `slop.anim.*` IDs to a chosen set of effects, sounds, and shared motion. A
   package references an ID and never copies the asset. The shared motion must be owner-made or
   CC0, because joint positions derived from it end up inside published `poses.bin` files.
3. **Build local catalogs from the roster plus installed Workshop fighters.**
   Follow [ADR-0067's rollout](../adr/0067-fighters-by-package.md). The catalog builder accepts
   admitted fighters under a match-content policy. Match presentation retains each entity's
   `FighterAppearance` and `FighterUiArt`; shipped descriptions, WIP flags, and Tutorial hints
   come from `card.json`. Generated UI bindings reach the existing art without moving it.
   `LocalFighterLibrary` supplies the Fighter Select cards and package choices for local seats. Installed sources remain unit 5.
   Installed fighters wait for the loader, portable bindings, and runtime cook before entering
   the grid. See [fighter presentation](../characters/adding-a-new-character.md#fighter-presentation).
4. **Cook in the game.** Run `CharacterPackageCompiler` with `CharacterCookProfile.Workshop`, bake
   the points, fit the capsules, assemble, and verify. Show diagnostics on screen. Cook again when
   a source file changes.
5. **Offer "fork a fighter".** Export a built-in kit as editable source. Bonk, Bashie, Nyx, and
   Salamander use no `slop.internal.*` capability today. FightGuy uses two, Manki four, and Wibou
   one, so those seven moves cannot be forked yet. A fork keeps the gameplay and loses the
   fighter's bespoke presentation scripts.
6. **Publish and subscribe to fighters.** Show a Workshop badge at Fighter Select, as the WIP
   badge does today.
7. **Generate the authoring reference from code.** List the 12 operation kinds and the 14 public
   capabilities with their fields, bounds, and budget costs, plus one worked example. Write it so
   that an LLM agent can author a kit from it.

**Checks for the phase.**

- A person or a fresh agent with the Steam build, the public reference, and a rigged GLB makes a
  playable fighter. Record the time taken. That time is the number this roadmap tries to shrink.
- An invalid source shows a diagnostic and never loads. A package that names `slop.internal.*`
  fails with `capability.untrusted`.
- The seven built-in packages keep their hashes. Compare `sloparena.character.cook --dry-run`
  hashes with the manifests.
- A Workshop fighter plays full Solo, Training, and couch Matches against built-in fighters and
  CPUs.

## Phase 3. Custom fighters online

1. **Check trust at load.** `CookedCharacterPackageLoader.LoadFiles` takes no profile, so a
   hand-made cooked package can name an internal capability
   (`src/Shared/CookedCharacterPackageLoader.cs:100`). Give the loader a profile. Recompute budgets
   at load and ignore the serialized values. Add hard limits for file size, pose memory, live
   hitboxes and projectiles per fighter, and projectile lifetime and speed. Fix the findings of
   0.4 and keep the harness in CI.
2. **Fetch packages on the GameServer.** Call `SteamGameServerUGC.DownloadItem`, cache by package
   hash, and build each Match catalog from the roster plus the Room's Workshop fighters.
   `src/Server/MatchContentCatalogProvider.cs` reads only `content-cooked` today.
3. **Let a Room name packages.** Add a Workshop flag to the Room. Let `CharacterSelection` carry a
   Workshop reference. Carry package requirements in the launch request and in `MatchPlayer`,
   which holds only a `CharacterClass` today. `MatchContentHandleRecord` requires a `CharacterClass`
   (`src/Shared/MatchContentHandleMap.cs:9`), so the handle map needs a new schema. The Master and
   Shared each hold the protocol number, so both repositories change in one release.
4. **Admit clients.** A client that lacks an item asks the player, downloads it, verifies the
   hashes, and then reports ready. Any failure blocks the Match and names the item. ADR-0026
   requires this fail-closed rule.
5. **Give the operator controls.** Add a block list in the Master, a switch that turns Workshop
   Rooms off, and a work budget per Match on the GameServer.
6. **Keep Replays playable.** Store the published file ID beside the content identity, so Exact
   playback can fetch the same version. View playback already falls back to a stand-in.

**Checks for the phase.**

- Two clients and the VPS play a Workshop fighter against a built-in fighter in a Workshop Room.
- The server and each client refuse a package with one changed byte.
- The harness runs its full mutation set with no crash, no hang, and no budget overrun.
- Standard Rooms keep their match start time and server tick time. Measure both on the previous
  release and on this one.
- A blocked item cannot be selected, and a Room that holds it cannot launch.

## Phase 4. Stages

1. Bake a `.arena` file from a GLB with named nodes for collision, spawns, and blast zones. The
   bake lives in the Editor today (`SlopArenaStageAuthoringService.cs`).
2. Show the same GLB as the stage visual. Stage visuals are prefabs under `Resources/Stages` today.
3. Reuse the Room sync and the server fetch from Phase 3. The arena digest is already part of the
   match content hash.
4. Validate arena files before loading them. A static read suggests the heightmap size is not
   checked against the data length (`src/Shared/ArenaBinaryFormat.cs:250`).

**Check.** A Workshop stage plays a Stock Match online. The camera cutaway holds on its geometry.
A malformed arena is refused on the server and on each client. Arcade formats stay on built-in
stages.

## Phase 5. Depth, ordered by demand

Build these only when published items show the need.

- A visual kit editor in the game. Ability Lab's preview classes already compile into the player
  (`client/Unity/Assets/AbilityLab/Runtime/`), and its authoring window is Editor code.
- Custom effects as data. The web demo already exports 143 Unity effect presentations to
  `client/Web/app/vfx/effects.json`, which is a starting format.
- Public primitives for the seven `slop.internal.*` capabilities, so that every built-in fighter
  can be forked.
- Package dependencies. The loader rejects them today
  (`src/Shared/CookedCharacterPackageLoader.cs:95`).
- Motion generation for creators, as a documented GenRA workflow.
- Workshop fighters in the web demo, which already loads cooked packages and GLB fighters.

## What this plan leaves out

- No creator code and no custom shaders. ADR-0024 stands.
- No AssetBundles. They need the Unity Editor to build and tie items to a Unity version. Rivals 2
  hid every older mod after its Unreal 5.8 upgrade (Appendix B).
- No staging backend between creators and Steam. Clients and servers verify hashes, so the upload
  goes straight to Steam. That closes an open question in the 2026-10-13 plan.
- No visual editor before Phase 5. Files plus reload in the game is the Rivals 1 workflow, and
  agents can write the files.

## Sizes and order

All durations are guesses. Code volume is not the limit in this repository. The browser demo went
from its first commit to a CI deploy in two days (`5134d60c` on 2026-10-29, `89e60eeb` on
2026-10-30). Owner decisions, Steam setup, and two-client playtests set the pace.

| Phase | Guess | What sets the pace |
| --- | --- | --- |
| 0 | About one week | The Steamworks settings in 0.3 and the ADR review in 0.5 |
| 1 | Two to three weeks | The bone mapping across model sources, and the Workshop page |
| 2 | Three to five weeks | The in-game cook, and how good a forked fighter looks without bespoke scripts |
| 3 | Three to four weeks | The loader limits, and a release that changes the Master and Shared together |
| 4 | Two to three weeks | Camera behaviour on arbitrary geometry |

Aether Studios kept the Rivals 2 Workshop in beta for nine months before its 1.0. Phases 1 to 3
are this roadmap's version of that beta.

## Appendix A. Current state against the ADR promises

Status on `main` at `714d9f8b`, 2026-11-10. In the last column, V means the cited lines were read
for this report, and E means a read-only explorer reported them. No build or test was run for
this table. Steps 0.2 and 0.4 later tested several rows, and "Phase 0 status" has those results.

| Capability | Built today | Missing | Evidence |
| --- | --- | --- | --- |
| Data-only fighters | 12 timeline operation kinds and 14 public `slop.ability.*` capabilities | | `src/Shared/CookedCharacterDefinition.cs:402`, `src/Shared/CharacterPackageCompiler.cs:33` (V) |
| Workshop cook profile | The compiler rejects `slop.internal.*` under `Workshop`, with a test | The cooked loader takes no profile | `CharacterPackageCompiler.cs:249`, `tests/Shared.Tests/CharacterPackageCompilerTests.cs:811`, `CookedCharacterPackageLoader.cs:100` (V) |
| Roster on public capabilities | Bonk, Bashie, Nyx, Salamander | FightGuy 2, Manki 4, Wibou 1 internal capabilities | `rg 'slop\.internal\.' client/Unity/Assets/CharacterPackages/*/character.json` (V) |
| Hashes and manifest | SHA-256 per payload, a cooked content hash, a package hash | Dependencies are rejected at load | `src/Shared/CharacterPackageAssembler.cs:167` (E), `CookedCharacterPackageLoader.cs:95` (V) |
| Limits on content | 8 stages, 64 operations per stage, 256 per timeline, 32 capabilities, at compile time | The loader does not recompute budgets. No limit on file size, pose memory, live hitboxes or projectiles. Static reading, not reproduced | `CookedCharacterDefinition.cs:915` (V), `CookedCharacterPackageLoader.cs:1038` (E) |
| Built-in ID registry | Capability ID and version allowlists | No `slop.anim.*`, `slop.vfx.*`, or `slop.sfx.*` registry | `CharacterPackageCompiler.cs:90` (E) |
| Match Content Catalog | Handles, exact identity, fail-closed checks on client and server | The catalog is the bundled roster. Handle records need a `CharacterClass` | `src/Shared/MatchContentCatalog.cs:110`, `MatchContentHandleMap.cs:9`, `src/Server/MatchContentCatalogProvider.cs:41` (V) |
| Rooms | Selector strings and arena names | No package requirement | Master `Rooms/RoomModels.cs:5` (V) |
| Server content | An anonymous Steam game-server session | No download and no cache. Content comes from the image | `src/Server/SteamGameServerHost.cs:164`, `MatchContentCatalogProvider.cs:14` (V) |
| Client presentation | | Bindings hold Editor object IDs. The player loads a generated catalog from `Resources`. No runtime importer | `CharacterBindingWriter.cs:25`, `client/Unity/Assets/Scripts/Runtime/Animation/CookedCharacterClientAssetResolver.cs:179` (V) |
| Fighter look | One runtime pass builds every fighter material from base map, base colour, and cull mode | | `MatchVisualStyle.cs:76` (V) |
| Presentation without fighter code | Salamander | Six fighters have their own presentation scripts | Explorer inventory of `Scripts/Runtime` (E) |
| Roster and menus | The roster manifest sets the order | Descriptions, the WIP flag, portraits, and tutorial dummies key on `CharacterClass` | `MenuRoster.cs:103` (V), `TutorialMatch.cs:67` (E) |
| Creator tools | Package template, `sloparena.character.create`, Ability Lab editing, cook, verify | Unity, paid Animancer 8.4.0, and the gateway are required. A clean-clone cook is unproven. No installed-game preview | `docs/contributing/unity-setup.md:157` (V) |
| Steam | Web API tickets, rich presence, invites, SteamNetworkingSockets on client and server | No `ISteamUGC` call in either repository | Search of both repositories (V) |
| Stages | A hashed `.arena` artifact, with its digest in match content | Visuals are `Resources` prefabs. The picker roster is not hash-pinned | `src/Shared/ArenaBinaryFormat.cs:41`, `MatchBase.cs:355` (E) |
| Local play | Solo, couch, Training, and local Arcade simulate on the player's machine | Player-hosted online play is not in player builds | `TrainingMatch.cs:418` (E), `ServerBrowserUI.cs:272` (V) |
| Replays and reports | Replays pin content identity. Feedback carries package ID, version, and hash | No content report kind and no block list | `src/Shared/Replays/Replay.cs:15` (E), `src/Shared/Feedback.cs:7` (V) |
| A second consumer | The web demo loads the same four cooked files and plays all seven fighters from GLB | | `client/Web/app/main.js:77`, `client/Web/assets/fighters/*.report.json` (V) |

Recorded effort for one fighter with the owner's full toolchain is about 15 hours for Nyx, with 24
animations and 27 effects (`skills/sloparena-character-workflow/SKILL.md:76`, E). That is not an
estimate for an outside creator.

## Appendix B. What Rivals 1 and Rivals 2 show

All figures were read on 2026-11-10 from the Steam Workshop browse pages for apps
[383980](https://steamcommunity.com/workshop/browse/?appid=383980) and
[2217000](https://steamcommunity.com/workshop/browse/?appid=2217000), the public Steam news feed
for app 2217000, and SteamCharts pages for
[Rivals 1](https://steamcharts.com/app/383980) and [Rivals 2](https://steamcharts.com/app/2217000).
Item counts come from the `total_count` field of the browse page. Player figures are SteamCharts
monthly averages of concurrent players.

| | Rivals of Aether | Rivals of Aether II |
| --- | --- | --- |
| Workshop opened | 2019-09-18 | Beta 2026-01-13 on a separate branch, 1.0 on 2026-10-06 |
| Creator tools | Sprites and GML script files | A modkit built on Unreal 5.8, Blender, and Lua |
| Public items created in the first ten months | 2,299, of which 941 are characters | 148 in total |
| Public items today | 12,373, with 954 created in 2026 | 148, with 97 created since 2026-09-21 |
| Average players before | 120 (January to August 2019) | 339 (September to December 2025) |
| Average players after | 316 over the next five months, 398 over the next twelve | 456 (January to October 2026) |
| Month of the launch | 262 in September 2019, up from 131 | 409 in October 2026, up from 392 |

What the numbers support.

- Demand is real in Rivals 2. Its 30 most subscribed items have 6,561 to 26,351 lifetime
  subscriptions each, in a game that averages about 400 concurrent players.
- Supply is the limit in 3D. Rivals 1 produced about 15 times more items in its first ten months,
  with fewer players. The Rivals 2 count leaves out mods that the studio hid after the Unreal 5.8
  upgrade, so the true gap is smaller.
- The long tail is the Rivals 1 result. Nine years after release it averages 270 players in 2026,
  more than twice its level before the Workshop.

What the numbers do not support.

- They show a launch bump for the Rivals 2 Workshop 1.0 and not yet a lasting rise. Daily peak
  players were 517 to 595 on 2026-10-01 to 2026-10-05 and 711 to 877 on 2026-10-06 to 2026-10-13.
  The mean daily peak was 621 over the 30 days before 1.0, 783 over the first two weeks after,
  and 641 from 2026-10-20 to 2026-11-09 (SteamCharts daily data, read 2026-11-10). The monthly
  average hid this: October moved 4% over September. January, April, and August 2026 each had a
  major update too (2026-01-06, 2026-04-07, 2026-08-04), so the 2026 level cannot be credited to
  the Workshop beta alone. Steam counts players per app, so the share playing Workshop content
  is not public.
- The Rivals 1 rise after March 2020 overlaps with lockdowns and with the Definitive Edition in
  September 2020. The five months before lockdowns still average 2.6 times the earlier level.

How Rivals 2 staged it, from its news posts. The beta shipped characters only, with one example
character in the modkit and one new character on the Workshop. Matches ran in player-hosted
Workshop lobbies. A player who lacked a character got a prompt, and the game downloaded it for
that session. The 1.0 added an in-game browser, a Workshop queue, and custom items. Stages and
skins were planned for later. The
[creation guide](https://rivals2.com/workshop/knowledge-base/character-creation/character-creation-overview/)
has the creator model and rig in Blender and then set up meshes, materials, hurtboxes, and attacks
in the Unreal editor.

## Appendix C. What Steam requires

From the Steamworks [Workshop overview](https://partner.steamgames.com/doc/features/workshop), the
[implementation guide](https://partner.steamgames.com/doc/features/workshop/implementation), the
[ISteamUGC reference](https://partner.steamgames.com/doc/api/ISteamUGC), and the
[Playtest page](https://partner.steamgames.com/doc/features/playtest), read on 2026-11-10.

- A Ready-to-Use Workshop needs no approval per item. A Curated Workshop does. ADR-0026 chose
  automated admission, which matches Ready-to-Use.
- Setup needs `ISteamUGC` file transfer enabled, a Steam Cloud quota for preview images, branding,
  and at least one public item before visibility can be Everyone. Visibility has four states,
  starting at Developers Only.
- An item stays hidden until its uploader accepts the Workshop legal agreement. `CreateItemResult_t`
  reports this in `m_bUserNeedsToAcceptWorkshopLegalAgreement`.
- An item is a folder. Steam advises against zipping it. Metadata set with `SetItemMetadata` comes
  back from queries without a download, so it can carry the package hash.
- A game server can call `DownloadItem`. With an anonymous login, Steam downloads the item and
  caches it temporarily. `BInitWorkshopForGameServer` sets the folder.
- `SetRequiredGameVersions` ties an item to a range of game branches.
- A preview image must be under 1 MB. Key-value tags are limited to 255 characters.
- A Playtest app is a separate app ID with the same technical features as the main app. The page
  does not mention the Workshop. Check 0.3 answers that.
- Limited Steam accounts, which have spent less than 5 USD, cannot submit Workshop content. This
  comes from a web search summary of Steam Support's Limited User Accounts page. The page body did
  not load for a direct read.

Unity facts that the plan relies on, from documentation and not from a prototype. Unity creates
only legacy clips at runtime, so GLB clips cannot go through Animancer directly.
`AvatarBuilder.BuildHumanAvatar` is a runtime API. All fighter clips sampled for this report are
Humanoid muscle clips. Check 0.1 tests all three.

## Appendix D. Routes considered

| Route | A creator needs | It reuses | Main risk |
| --- | --- | --- | --- |
| Unity Editor ModKit (ADR-0027 and the 2026-10-13 plan) | Unity 6, the project, Animancer, and a sample that avoids vendor assets | The cook and Ability Lab as they are | Low supply, as in Rivals 2. A clean-clone cook is unproven. Art still needs a payload format |
| Installed game plus open formats (recommended) | The game, a rigged GLB, and a text editor | The compiler, the fighter material pass, the web demo's GLB exporter, and the server's Steam session | New runtime loaders. Workshop fighters look plainer than built-in fighters at first |
| Server sync first, tools later | Nothing until the end | The catalog and the handle map | The longest wait for anything a player can see. It fixes validation before the format is known |

Rejected inside the recommended route.

- **Fighters before model swaps.** The first release would wait for the cook, the registry, and
  the catalog work. Model swaps ship sooner and build the Steam and loader parts.
- **Player-hosted Workshop lobbies, as in the Rivals 2 beta.** Player hosting is not in player
  builds. Clients also load untrusted packages, so the loader limits are needed either way.
- **Curated Workshop.** ADR-0026 rejects manual approval. A later "featured" list for standard
  Rooms can sit on top of an open Workshop.

## Appendix E. Risks

| Risk | Where it bites | What to watch or do |
| --- | --- | --- |
| Few creators | Every phase | Track the time to a first fighter from the Phase 2 check. Publish templates and an agent-readable reference. Consider a contest, as Rivals did |
| Few players to play the items | Every phase | Tie each phase to a release moment. Track items published, subscriptions, and the share of Matches with Workshop content |
| Third-party characters | Phase 1 onward | Decision D5. Steam handles takedown notices for Workshop items |
| Offensive models seen online | Phase 1 | The visibility setting, the report action, and the block list |
| Engine changes break items | Phase 2 onward | Open formats, an additive API window (the loader accepts 23 API minimums today), and source kept in each item |
| A hostile package on a shared GameServer | Phase 3 | The loader profile and limits, the harness in CI, opt-in Rooms, and the operator switch |
| Paid animation data inside items | Phase 2 | Shared motion from the registry must be owner-made or CC0. Never copy clips into an item |
| Workshop fighters look plain | Phase 2 | Six of seven built-in fighters use bespoke scripts. Offer registry effects first, then data effects in Phase 5 |
| The Workshop cannot be used from the Playtest app | Phase 0 | Check 0.3, then decision D4 |

## Appendix F. How this report was made

Four read-only explorers on `gpt-6.1-sol` each read one slice of this repository and the Master
server repository. Their load-bearing claims were then checked against the source for this report,
and Appendix A marks which ones. External figures were pulled from public Steam pages and parsed
directly. No build, test, Unity session, or Steam partner setting was touched. The crash paths in
Appendix A are static readings that step 0.4 will confirm or clear.
