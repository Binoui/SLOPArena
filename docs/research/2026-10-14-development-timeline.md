# SlopArena development timeline for a vlog

Research cut-off: **14 October 2026**. This is a script research index: what changed, when the surviving evidence places it, and where to recover the details. It combines Git history with original Antigravity, Hermes and OMP records and locally sampled video. It is not a claim that every feature was ready or every planned playtest happened. A searchable [visual timeline](2026-10-14-development-timeline.html) accompanies this guide. The [Downloads source guide](2026-10-14-downloads-source-guide.md) adds original design exports, art/model iterations, early builds and existing video-preparation presentations. The [tools and models timeline](2026-10-14-tools-and-models-timeline.md) dates the changing AI workflow and links the underlying model/session evidence.

Dates below use the local calendar in Europe/Paris. Git author dates indicate when a change was authored; commit dates sometimes indicate a later squash, cherry-pick, or integration. Dated plans and ADRs establish a decision, not implementation. The original snapshots linked below are better historical evidence than today's living documentation, which often has later corrections.

The normal history in this checkout starts on **26 May**, with a substantial existing prototype. Earlier Antigravity material places the concept and MoveBox implementation plan on **22 May**. The first commit is therefore an archive boundary, not the conception date. The early Godot checkout has differently rewritten commit hashes; this document uses hashes from the current SlopArena main history.

## 1. Conception and the Godot prototype

### 1 — 22 May: Project Antigravity / MoveBox

The earliest recovered planning material calls the project **Project Antigravity (Arena Combat)**. The concept combines Melee movement, Diabotical-style momentum, and V Rising spells, initially framed as top-down keyboard-and-mouse combat. The implementation plan proposes moving the prototype from GDScript to Godot C#, with pure C# Shared code and a separate .NET UDP physics server. This is the best starting point for the origin story, before the name SlopArena.

**Where to look:** Antigravity conversation/artifact directory `~/.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/`, especially `gdd.md`, `implementation_plan.md`, and their `.metadata.json` files. The recovered metadata records updates on 22 May at 12:16:04 UTC and 12:10:54 UTC respectively; the conversation points to the original `MoveBox` project directory. These local artifacts are outside Git. For the first preserved Git state, use the [MoveBox README at `1f64c64`](https://github.com/Binoui/SlopArena/blob/1f64c64/README.md).

**Script lead:** What game did you actually want to play that existing games were not giving you? Compare the original camera/spell concept with today's fighter.

### 2 — 26 May: The networked MoveBox becomes SlopArena

The first preserved commit already contains a Godot 4.6 C# client, a 60 Hz UDP server, Shared physics and packet code, local prediction, spell categories, a spellbook, and debugging ghosts. Its README explicitly says **“MoveBox (Project Antigravity)”**. Later that day the project, solution, namespaces and engine title are renamed **MoveBox → SlopArena**, and French code/comments are cleaned up. The server was present at the beginning; June did not invent networking from scratch.

**Where to look:** [First snapshot `1f64c64`](https://github.com/Binoui/SlopArena/commit/1f64c64), its [README](https://github.com/Binoui/SlopArena/blob/1f64c64/README.md), [original `gdd.md`](https://github.com/Binoui/SlopArena/blob/1f64c64/gdd.md), [Godot project settings](https://github.com/Binoui/SlopArena/blob/1f64c64/project.godot), and [the initial server](https://github.com/Binoui/SlopArena/blob/1f64c64/Server/Program.cs). The [rename commit `92a1d3f`](https://github.com/Binoui/SlopArena/commit/92a1d3f) shows the before/after names. The original GDD proposes a classless universal grimoire and drop-in FFA; treat that as the initial vision, not the final game's implemented mode.

**Visual lead:** Cyan predicted circle versus red server ghost; spellbook/action bar; original Godot scene and title.

### 3 — 27–29 May: From movement sandbox to a DKO-style 3D fighter

Movement is reworked around dash dancing, sprinting, fast fall, air dodge, tech rolls and a combo-breaker trinket. The commit explicitly calls this **“platform fighter 3D.”** Some of these mechanics were later removed or replaced; this is an early exploration of the movement vocabulary. On **29 May**, your Hermes conversation explicitly settles on recreating DKO first: put the grimoire aside, use character kits, movement within attacks, a double jump and a simple dash. This gives the design change a clearer date than the restored June code alone. [Original Hermes discussion](../../../../../.hermes/sessions/session_20260527_185140_25c543.json); search for `je veux juste refaire DKO` and `le grimoire`.

**Where to look:** [Movement rework `90b0fdf`](https://github.com/Binoui/SlopArena/commit/90b0fdf), especially `Shared/MovementProfiles.cs`, `Shared/PhysicsConfig.cs`, and `Scripts/Entities/PlayerController.cs` in that snapshot. Its commit message records the intended feel and numeric values. Later that day [settings and key rebinding `d20a433`](https://github.com/Binoui/SlopArena/commit/d20a433) and [numbered spell slots `6ae5993`](https://github.com/Binoui/SlopArena/commit/6ae5993) show the prototype becoming easier to use.

### 4 — 1 June: Community-created fighters, an Unreal detour, then back to Godot

At 00:09 local time on 1 June, you explain the name SlopArena as embracing AI-assisted development and imagine an open-source game where people contribute fighters through PRs, comparing it to Steam Workshop. Minutes later you choose Unreal as more suitable for competitive play. There was therefore an **UE5 experiment before the Unity migration**. The 1 June commit explicitly returns to Godot 4.6 C# and undoes that migration. Its message says the experiment was preserved as `ue5-experiment`, but that tag is not present in the current local tag list. A second commit restores lost DKO-style gameplay: three character classes, LMB three-hit combos, charged RMB heavies, air attacks, Q/E/R/F abilities, and class switching. This is an observable shift away from the earlier classless spellbook vision.

**Where to look:** [Return to Godot `3971844`](https://github.com/Binoui/SlopArena/commit/3971844) and [restore DKO gameplay `1fee0ca`](https://github.com/Binoui/SlopArena/commit/1fee0ca). Their messages explain the reset and lost-commit recovery. The [community/Unreal decision conversation](../../../../../.hermes/sessions/session_20260601_000841_378f94.json) records both the ambition and the engine choice. The [return-to-Godot conversation](../../../../../.hermes/sessions/session_20260601_232134_e49cbf.json) records your reason at 23:38: Linux/Hyprland installation friction, too much editor clicking, and a preference for faster code-led iteration. Search `revert le passage à unreal`. These records date the decision and attempted use on 1 June; earlier installation groundwork may precede them.

### 5 — 2–6 June: Shared simulation, character definitions, then Manki

The prototype is split into a pure C# tick simulation with character state, input state and tick-based timers. A data-driven character system replaces scattered class behavior. KayKit art, Smash-style damage percentages, camera-relative movement, animations and air attacks follow. By 6 June the roster is deliberately stripped back to **Manki only**, with his animation pipeline as the working reference.

**Where to look:** [Character definitions `6ce38bc`](https://github.com/Binoui/SlopArena/commit/6ce38bc), [Shared simulation `82b4b48`](https://github.com/Binoui/SlopArena/commit/82b4b48), [KayKit / damage-percent transition `e9179f3`](https://github.com/Binoui/SlopArena/commit/e9179f3), [Manki animation pipeline `66bf8eb`](https://github.com/Binoui/SlopArena/commit/66bf8eb), and [Manki-only cleanup `7bd1012`](https://github.com/Binoui/SlopArena/commit/7bd1012). For contemporary design research, see [DKO kit reference `507c368`](https://github.com/Binoui/SlopArena/commit/507c368) and [kit-design principles `d9be477`](https://github.com/Binoui/SlopArena/commit/d9be477).

**Script lead:** The first recurring character, and the point where the toy began acquiring a reusable game model. For the personal art story, Hermes session `20260604_194101_a19379` records paying for 3DAIStudio and looking for an art direction; the 13 June session `20260613_123412_e0528d` explains Manki evolving from martial artist into a chaotic bomber monkey, then the animal-kung-fu idea behind Bunny. Both survive in the Hermes database; see [the local source map](#local-source-map).

## 2. A multiplayer game, then the engine change

### 6 — 10–11 June: Fixing the server and creating the Master backend

On 10 June the server stops using the old physics implementation and starts calling the same `Simulation.SimulateTick()` as the client, fixing an explicit prediction/desynchronization problem. Separately, the Master backend is designed and created: ASP.NET Core, SignalR, PostgreSQL/EF Core, server registration, heartbeats and match tracking. On 11 June it is extracted into **SlopArena-MasterServer**. This establishes the enduring distinction between a game server that simulates a match and a Master that manages online services.

**Where to look:** [Shared server simulation fix `cde945a`](https://github.com/Binoui/SlopArena/commit/cde945a); the [original server architecture design at `86270d9`](https://github.com/Binoui/SlopArena/blob/86270d9/docs/superpowers/specs/2026-06-10-server-architecture-design.md); [Master initialization `89a7f4b`](https://github.com/Binoui/SlopArena/commit/89a7f4b); [registration/match tracking `988598a`](https://github.com/Binoui/SlopArena/commit/988598a); and [repository split `88440c3`](https://github.com/Binoui/SlopArena/commit/88440c3). For later work continue in the [separate MasterServer repository](https://github.com/Binoui/SlopArena-MasterServer). The design's spectator/scaling/community-server ambitions are plans, not proof those features shipped.

### 7 — 12–15 June: Server-accurate bodies, baked animation, a second fighter and direct-IP PvP

Server authority expands from movement into the whole match and body geometry: bone-attached hurtboxes, pure C# `ServerSimulation`, and animation samples baked at 60 fps for the headless server. **Bunny**, a martial-artist rabbit, is added on 14 June. By 15 June there are separate Training/PvP match paths and an IP field to connect to an opponent.

**Where to look:** [Authoritative skeleton/hurtbox refactor `55704e4`](https://github.com/Binoui/SlopArena/commit/55704e4) and [baked skeleton pipeline `d6a8768`](https://github.com/Binoui/SlopArena/commit/d6a8768), both authored/integrated on 12 June; [Bunny addition `4b7611d`](https://github.com/Binoui/SlopArena/commit/4b7611d); [online PvP path `1c42d29`](https://github.com/Binoui/SlopArena/commit/1c42d29); [direct-IP UI `b543b26`](https://github.com/Binoui/SlopArena/commit/b543b26). Historical files include `Shared/BakedAnimationData.cs`, `Scripts/Tools/BakeSkeletonTool.cs`, `Scripts/World/PvPMatch.cs`, and `docs/characters/bunny.md` in the appropriate snapshot. The [18 June friend's build fix `7a0c18d`](https://github.com/Binoui/SlopArena/commit/7a0c18d) is a useful human-sharing clue, but does not prove a successful remote match.

### 8 — 18–24 June: The ability framework and OMP become part of the workflow

Abilities evolve from special-case data into a lifecycle framework: projectiles and mines own their spawning behavior, then `ServerAbility` / `AbilityFactory` appear, followed by a larger server-authoritative ability refactor. Manki receives dedicated implementations rather than placeholders. On 24 June the repo gains `.omp/` instructions and seven project-specific skills. Your [first OMP conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl) opens on 24 June with “i just installed omp. i was using hermes agent.” You explain that you heard OMP was better for coding and import project knowledge/skills. This establishes the tool transition directly, rather than inferring it only from configuration commits.

**Where to look:** [Mine/polymorphic ability work `eac1264`](https://github.com/Binoui/SlopArena/commit/eac1264), [ability framework introduction `2b29450`](https://github.com/Binoui/SlopArena/commit/2b29450), and [server ability refactor `a501a4f` / PR #9](https://github.com/Binoui/SlopArena/commit/a501a4f). Its long squash message preserves the design iterations. Read the [contemporary ability architecture](https://github.com/Binoui/SlopArena/blob/a501a4f/docs/systems/ability-architecture.md). [OMP project setup `9132426`](https://github.com/Binoui/SlopArena/commit/9132426) adds `.omp/AGENTS.md`, `.omp/RULES.md` and the initial skills.

**Script lead:** Making the assistant a project-specific collaborator, and making abilities easier to build repeatedly. This framework is historical: today's content primarily uses cooked timelines.

### 9 — 25–30 June: Godot → Unity 6

The decision is visible on **25 June at 13:26** in [the OMP setup conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl) (line 891): you ask about Unity because its editor looks better while the server is already code-based. At 13:39 you say you are downloading Unity and authorize starting; at 14:26 you have created the Unity project. The substantive migration is committed just after midnight on **26 June**: `Shared/` and `Server/` move under `src/`, the client becomes `client/Unity/`, and Unity gets a training scene, URP, Cinemachine, InputSystem, rendering and baking tools. Godot cleanup and phased porting continue over the following days. A brief source/symlink experiment ends on 30 June with the shared `netstandard2.1` DLL as the canonical Unity boundary.

**Where to look:** [Migration `783b476`](https://github.com/Binoui/SlopArena/commit/783b476); [cleanup `4c75c3d`](https://github.com/Binoui/SlopArena/commit/4c75c3d); the [early Unity architecture plan at `4c75c3d`](https://github.com/Binoui/SlopArena/blob/4c75c3d/docs/plans/2026-06-26-unity-client-architecture.md); [last Godot script purge `4b9613b`](https://github.com/Binoui/SlopArena/commit/4b9613b); and [Shared DLL cutover `9dd3da0`](https://github.com/Binoui/SlopArena/commit/9dd3da0). Compare the Godot `main.tscn` with Unity's `Assets/Scenes/Arena_Offline.unity` at the migration. The [25 June migration conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-25T13-06-03-636Z_019efee3-34f4-7000-b145-032d264d8350.jsonl) covers the folder structure, headless server, local simulation and rollback requirements. Use it together with O01 for the personal reason and early migration difficulties; these commits establish the saved implementation.

### 10 — 2–9 July: Unity animation and the shared Training/PvP bridge

Knockback and stage-chain abilities are refined, ledge snapping arrives, and **Animancer replaces generated AnimatorControllers** on 4 July. Aiming cameras, weapon attachments and crosshairs follow. On 9 July Unity gets `ISimulationBridge`, a network bridge, `PvPMatch` and the `Arena_PvP` scene, so Training and online play can share match code.

**Where to look:** [Knockback pass `37c1036`](https://github.com/Binoui/SlopArena/commit/37c1036), [Animancer presentation `e4287a2`](https://github.com/Binoui/SlopArena/commit/e4287a2), [remove old AnimatorGenerator `ada8c2c`](https://github.com/Binoui/SlopArena/commit/ada8c2c), and [Unity PvP bridge `fe21fcd`](https://github.com/Binoui/SlopArena/commit/fe21fcd). Original scripts sit under `client/Unity/Assets/Scripts/Runtime/`. This is the first Unity network bridge, not yet the later August three-track rollback implementation. The [3 July Animancer conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-03T13-01-51-420Z_019f2812-3bbc-7000-a502-318a2514645c.jsonl) captures your preference for working through code with AI, alongside the concern that regenerating an AnimatorController would overwrite manual changes.

### 11 — 13 July–1 August: FightGuy, Kistu and Nilus build out the early roster

On 13 July Bunny's data, abilities, assets and documentation are replaced/reworked into **FightGuy**. The earlier character sheets survive in `Downloads/sloparena/rabbit/` and `fightguy/`; use the [Downloads source guide](2026-10-14-downloads-source-guide.md) to compare the visual designs rather than only the code names. **Kistu, the Kitsune Blade** is authored on 27 July and integrated on 28 July. **Nilus, the Void Stalker** is authored on 28 July and integrated on 1 August, giving the early four-character roster Manki/FightGuy/Kistu/Nilus. Art and placeholder animation bindings are still being integrated; four entries does not mean four finished kits.

**Where to look:** [FightGuy transition `9f72cc4`](https://github.com/Binoui/SlopArena/commit/9f72cc4), including the `BunnyData.cs → FightGuyData.cs` and documentation changes; [Kistu `f9b2189`](https://github.com/Binoui/SlopArena/commit/f9b2189); [Nilus `4e381f9`](https://github.com/Binoui/SlopArena/commit/4e381f9); and [1 August demo/art checkpoint `570c2ca`](https://github.com/Binoui/SlopArena/commit/570c2ca). Read the historical `docs/plans/2026-07-27-kistu-implementation.md`, `docs/plans/2026-07-28-nilus-implementation.md`, and character docs in those snapshots. Today's Kistu successor is Wibou; Nilus was later retired. The [10 July Bunny-to-FightGuy conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-10T15-43-55-967Z_019f4cb3-223f-7000-ae38-3e8f54c6feef.jsonl) dates the decision before its 13 July integration.

## 3. Online demo, gamefeel experiments and authoring tools

### 12 — 1–2 August: The full online match flow and distributable demo infrastructure

This is a concentrated online-play push: guest authentication, server browser, SignalR lobby, multiplayer character lock-in, embedded host-and-play, roster-based starts, 2–4-player matches, stock mode, HUDs, results and return-to-lobby. Release scripts bundle a server and baked stage data, while deployment targets an official server. It is the point where networking becomes a player-facing sequence rather than an IP textbox. In [the 2 August build discussion](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-02T14-53-53-353Z_019fc2f7-9549-7000-993e-9b273a93ef16.jsonl), you describe the desired experience as downloading an EXE and playing solo or hosting/joining, and consider hosting the backend on **Alfred**, your home mini-PC.

**Where to look:** [Guest auth `a1d0a41`](https://github.com/Binoui/SlopArena/commit/a1d0a41), [lobby `17ded71`](https://github.com/Binoui/SlopArena/commit/17ded71), [embedded host `75d41ae`](https://github.com/Binoui/SlopArena/commit/75d41ae), [2–4 players `3532bf6`](https://github.com/Binoui/SlopArena/commit/3532bf6), [stock mode `309a6b5`](https://github.com/Binoui/SlopArena/commit/309a6b5), [results/rematch flow `26c2048`](https://github.com/Binoui/SlopArena/commit/26c2048), and [deployment pipeline `ccdb134`](https://github.com/Binoui/SlopArena/commit/ccdb134). The [August PvP roadmap](../plans/2026-08-01-pvp-roadmap-v2.md), [EXE release plan](../plans/2026-08-02-exe-release-plan.md), and ADRs 0003–0009 preserve the decisions. They were later superseded, so use Git snapshots for the original assumptions.

### 13 — 3–5 August: A new rollback/prediction system for Internet latency

An input relay and the **LocalTrack / PredictedTrack / RawTrack** model arrive on 3 August. The stated motivation is online input lag: Unity had been displaying raw server state. Local actions stay immediately responsive; suitable opponent movement can be predicted; complex actions retain authoritative state. Tick-input queues, real-codec convergence tests and rollback fuzzing follow on 5 August.

**Where to look:** [Input relay `830f29e`](https://github.com/Binoui/SlopArena/commit/830f29e), [rollback implementation `cf2cc37`](https://github.com/Binoui/SlopArena/commit/cf2cc37), its [contemporary design](https://github.com/Binoui/SlopArena/blob/cf2cc37/docs/plans/2026-08-02-rollback-netcode.md), [input queue `d048c0d`](https://github.com/Binoui/SlopArena/commit/d048c0d), and [convergence harness `9cc12ff`](https://github.com/Binoui/SlopArena/commit/9cc12ff). ADR-0010 records the first proposal; ADR-0011 records why that proposal had to be narrowed. Earlier Godot commits also use the word “rollback”; this August milestone is the later, explicit three-track implementation, not the first ever prediction/reconciliation code. Your [4 August two-client test](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-04T21-19-56-155Z_019fcea5-bcfb-7000-a8df-cb3ed2fca455.jsonl) records getting two clients on one PC into a game connected to Alfred, then hitting missing-entity errors. This is a concrete early online-system test, with its limits visible. The preserved [demo.1 ZIP](../../../../../Downloads/SlopArena-0.2.0-demo.1.zip) has entry timestamps on 2 August; [demo.2](../../../../../Downloads/SlopArena-0.2.0-demo.2.zip) has entries on 4 August. Their player guide preserves the pre-keyboard-pivot download and control flow.

### 14 — 7 August: Hitstop, combo influence and Burst

Combat gains pair-specific hitstop, deferred launches, directional combo influence, and a dual-use **Burst** for escaping or extending combos. Unity freezes animations with the simulated hitstop. This is a distinct gamefeel experiment before the next larger pivot. Combo Influence is superseded during the Melee hit-response work; Burst is later replaced by shield/grab/air-dodge controls in September.

**Where to look:** [Hitstop `83695ed`](https://github.com/Binoui/SlopArena/commit/83695ed), [animation freeze `d92c45b`](https://github.com/Binoui/SlopArena/commit/d92c45b), [Combo Influence `9b7354a`](https://github.com/Binoui/SlopArena/commit/9b7354a), [Burst `7dd8df3`](https://github.com/Binoui/SlopArena/commit/7dd8df3), and [ADRs 0012–0014 introduction `bb8c3d1`](https://github.com/Binoui/SlopArena/commit/bb8c3d1).

### 15 — 10–11 August: Leaving automatic DKO-style combat behind

The combat pivot disables auto-gap-closing **Warp**, removes press-based automatic combo chains, preserves movement through attacks, and makes recovery an explicit move. Inputs become keyboard-first, with short hops and fast fall; FightGuy gets separate normals and a Rising Dragon recovery. The historical ADR describes the trigger plainly: solo feedback that the game felt “less free” after the automatic approach/hover layer was added. It is a useful narrative turning point because it is an explicit rejection of recently built behavior.

**Where to look:** [Attack-data pivot `e2571c2`](https://github.com/Binoui/SlopArena/commit/e2571c2), [mechanics pivot `0f51f12`](https://github.com/Binoui/SlopArena/commit/0f51f12), [keyboard input `2269a22`](https://github.com/Binoui/SlopArena/commit/2269a22), [FightGuy kit `173e097`](https://github.com/Binoui/SlopArena/commit/173e097), and the [original ADR-0015](https://github.com/Binoui/SlopArena/blob/e2571c2/docs/adr/0015-timing-model-combat-pivot.md). Warp here is historical, not a current mechanic. That ADR's original rejection of blocking also changed later; do not read it as today's defense design.

### 16 — 11–12 August: You ask for Ability Lab, then its first saved implementation

At **01:07 on 11 August** you ask for a tool because the hurtboxes have been “vibecoded” and you want to place them yourself, visually, while stepping an attack frame by frame. The [original request](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-10T23-04-04-511Z_019fedeb-3c9f-7000-975d-850633c596d9.jsonl) is the clearest explanation of why Ability Lab exists. An OBS recording from 11 August already shows the working Lab; its first preserved Git commit is on 12 August. **Ability Lab** begins as a Unity scene and editor window for scrubbing animation ticks, visualizing server-accurate hitboxes/hurtboxes, positioning a dummy, and editing hitboxes. Its central promise is that the preview uses the same Shared geometry resolvers as the server. At this stage saving rewrites the character's **C# data source**; JSON and packages come later.

**Where to look:** [First Ability Lab commit `4cb7767`](https://github.com/Binoui/SlopArena/commit/4cb7767) and its [original tool manual](https://github.com/Binoui/SlopArena/blob/4cb7767/docs/systems/ability-lab.md). Historical paths: `Assets/Scenes/lab.unity`, `Assets/Scripts/Editor/AbilityLabWindow.cs`, `Assets/Scripts/Runtime/Tools/AbilityLab.cs`, and `src/Shared/CSharpCharacterWriter.cs`. [Issue #119](https://github.com/Binoui/SlopArena/issues/119) is the design/implementation discussion pointer.

**Visual lead:** Scrub an attack and show green body capsules and orange hitboxes; the tool makes an invisible simulation visible. Use [the original 11 August Lab recording](../../../../../Videos/obs%20recording/2026-08-11%2022-21-30.mp4) (~30 seconds).

### 17 — 13–20 August: A Melee-inspired feel engine and measurable tuning

The [12 August normals discussion](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-12T15-53-36-753Z_019ff6ad-daf1-7000-9cde-80720404d8db.jsonl) explains splitting automatic LMB phases into distinct `1 / 2 / 3 / 4` moves, with `A / E / R / F` for specials and ground/air variants. In [the 12–13 August Melee discussion](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-12T21-44-28-059Z_019ff7ef-129b-7000-a31d-3a191aa3785c.jsonl) you ask whether the Melee decompilation can help; at 00:14 on 13 August you explicitly choose Melee as the base and say you want to get away from the DKO feel. The next pass changes hit response, knockback, DI/SDI, movement and frame timing around that Melee-inspired model adapted to 3D. IASA, aerial landing lag, auto-cancel windows, bone-tracked hitboxes and blast zones are added/refined. Authoring and integration dates differ: major Shared implementation commits were authored 13–15 August and integrated together on **15 August**. Analysis tools then grow into trajectories, kill percentages, combo graphs, movement sheets, hitbox reach charts, bot-match spectators, A/B tuning comparisons and CPU difficulty levels.

**Where to look:** [Research / ADR set `8753482`](https://github.com/Binoui/SlopArena/commit/8753482); [hit response `88e95ac`](https://github.com/Binoui/SlopArena/commit/88e95ac); [movement `89c26f6`](https://github.com/Binoui/SlopArena/commit/89c26f6); [frame timing `40a8535`](https://github.com/Binoui/SlopArena/commit/40a8535); [bone-tracked normals `7f1d72b`](https://github.com/Binoui/SlopArena/commit/7f1d72b); [SpectateView `a784cf8`](https://github.com/Binoui/SlopArena/commit/a784cf8); [A/B reports `a5f4b9d`](https://github.com/Binoui/SlopArena/commit/a5f4b9d); [CPU levels `b0b554c`](https://github.com/Binoui/SlopArena/commit/b0b554c). Read [Melee research](melee-movement-audit.md), [flight sweep](flight-model-sweep.md), [implementation plan](../plans/2026-08-13-melee-feel-implementation.md), and [handoff](../plans/2026-08-14-melee-feel-handoff.md). Generated HTML reports under `docs/generated/` can supply visuals; use the August snapshots to avoid showing later tuning as the original version. The [original six-page HUD specification](../../../../../Downloads/SlopArena_UI_Specification.pdf) is explicitly introduced as freshly generated in [the 15 August UI conversation](../../../../../.omp/agent/sessions/-orca-workspaces-SlopArena-GUI/2026-08-15T10-14-33-672Z_01a004ea-85c8-7000-9495-cab140af8a10.jsonl) (line 4). It preserves the short-lived Burst-era screen/input model and your design discussion.

### 18 — 21–23 August: Workshop-first architecture and Ability Lab → JSON

A seven-ADR set is accepted on **21 August** and enters Git on **22 August**: community fighters, stages and presentation content are intended to become first-class, with deterministic primitives and packages instead of arbitrary creator code. On 23 August Ability Lab moves from rewriting C# to editing **JSON**, followed by startup JSON overrides. This is implemented authoring/content infrastructure, not evidence that Steam Workshop publishing was already available. The [21 August discussion](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-21T12-00-29-930Z_01a02431-aaea-7000-b367-418e1b77196d.jsonl) identifies the initiating ADR as coming from an online ChatGPT conversation; your [original exported ADR](../../../../../Downloads/sloparena/art/ADR-0001_%20Workshop-First%20Community%20Content%20Architecture.md) preserves that framing before repository numbering. The [23 August JSON request](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-23T11-05-04-745Z_01a02e4b-a5e9-766a-95c4-463124df85d6.jsonl) spells out the immediate problem: Ability Lab should stop treating `FightGuyData.cs` as its editable document.

**Where to look:** [Workshop/content checkpoint `360e7e2`](https://github.com/Binoui/SlopArena/commit/360e7e2) and the [original ADR-0022](https://github.com/Binoui/SlopArena/blob/360e7e2/docs/adr/0022-workshop-first-content-architecture.md); ADRs 0023–0028 explain capabilities, primitives, packages, multiplayer synchronization, ModKit and built-in compatibility. [Lab JSON migration `0062992`](https://github.com/Binoui/SlopArena/commit/0062992) and [startup overrides `56a3b82`](https://github.com/Binoui/SlopArena/commit/56a3b82) capture this short-lived intermediate authoring/runtime arrangement.

### 19 — 26 August–1 September: Cooked character packages, Bonk, and the complete source-to-runtime path

A mismatched FightGuy animation exposes three competing definitions: C# data, JSON and Unity animation bindings. ADR-0029, accepted 26 August, responds with one package source, a Shared compiler, deterministic Unity cooking, immutable runtime artifacts and a match content catalog. **FightGuy** cuts over on 27 August; the Lab editor is reworked on 28 August; **Kistu** gets a package in that day's pipeline expansion; **Bonk's package** first appears on 29 August; **Manki** moves fully onto cooked content on 1 September. The canonical 16-slot ground/air projection is recorded in ADR-0030. The visual-identity thread starts earlier: [at 00:32 on 25 August](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-24T22-30-51-921Z_01a035e5-dd11-7170-a3d9-44c4e4e97e42.jsonl) you ask to replace Kistu's model with Wibou. This is separate from the later formal name change and package-ID integration.

**Where to look:** [Original ADR-0029](https://github.com/Binoui/SlopArena/blob/7bec5fd/docs/adr/0029-character-authoring-and-cooking.md), [FightGuy cooked cutover `7bec5fd`](https://github.com/Binoui/SlopArena/commit/7bec5fd), [Lab rework `7226e3e`](https://github.com/Binoui/SlopArena/commit/7226e3e), [pipeline expansion `67b9258`](https://github.com/Binoui/SlopArena/commit/67b9258), [Bonk package / authoring expansion `9d53ccd`](https://github.com/Binoui/SlopArena/commit/9d53ccd), and [Manki cutover `aa981c0`](https://github.com/Binoui/SlopArena/commit/aa981c0). Read the [cutover plan](../plans/2026-08-26-fightguy-character-cooking-cutover.md), [canonical-slot decision](../adr/0030-ability-lab-canonical-slot-projection.md), and [current character-authoring guide](../characters/adding-a-new-character.md). Bonk is already visible in [an animation-editor recording labelled 27 August](../../../../../Videos/obs%20recording/2026-08-27%2013-13-40.mp4); distinguish that art/setup work from his first package in Git. The [28 August Bonk pipeline conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-28T16-52-31-253Z_01a04949-8995-7778-acea-3fc0d30a4ab4.jsonl) includes your feedback on agent-assisted character creation. For Bonk's earliest package source use [the 29 August package manifest](https://github.com/Binoui/SlopArena/blob/9d53ccd/client/Unity/Assets/CharacterPackages/bonk/package.json), not `git log --follow` alone: rename detection can misleadingly follow a similar older FightGuy file.

**Script lead:** An animation bug that rebuilding could never fix, and the toolchain built to stop that class of problem recurring.

## 4. Returning to a playable game and reaching Steam

### 20 — 5 September: The playable friends demo reset

The project explicitly resets priorities to **a playable four-character friends demo as soon as possible**. The admitted roster is Manki/FightGuy/Kistu/Bonk; Nilus is legacy compatibility. The audit finds a concrete delivery problem: publishing only FightGuy despite a four-package roster. It retains useful architecture but freezes further platform expansion and asks for a rough friend match before polishing everything. This is a product decision, not a successful remote-play acceptance receipt.

**Where to look:** [Baseline/reset `cbeb2a0`](https://github.com/Binoui/SlopArena/commit/cbeb2a0) and the [original reset plan at that commit](https://github.com/Binoui/SlopArena/blob/cbeb2a0/docs/plans/2026-09-05-playable-demo-reset.md). Today's [living reset plan](../plans/2026-09-05-playable-demo-reset.md) contains later fixes and updated roster names. The original snapshot preserves the actual blocker and the scope change.

**Script lead:** Recognizing that adding infrastructure had outpaced getting friends into a match. Your [5 September release-readiness conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-05T16-54-03-619Z_01a0727d-d263-7587-a61a-7aa1b9fee493.jsonl) immediately asks for a shareable EXE, Alfred deployment and reliable full-roster packaging. The [30 September product discussion](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-09-30T18-14-22-098Z_01a0f386-5492-75d0-b651-b1110ed4e2b7.jsonl) revisits the same tension and explicitly agrees to focus on making the game fun first.

### 21 — 7–15 September: Complete roster packaging, live draft previews and kit debugging

Release packaging is made roster-complete on 7 September; Ability Lab gains live draft previews and package presentation/VFX bindings. Contact-coverage tools and a detailed Bonk investigation follow on 11 September. CPU behavior becomes move-kit-driven, with special plans and delayed follow-ups, during 14–15 September. This phase is about making the authored fighters work and finding concrete gaps. In parallel, the [separate website repository](https://github.com/Binoui/SlopArena-web/commit/76ee310) gains its demo landing page on **31 August**, then [bilingual/feedback support](https://github.com/Binoui/SlopArena-web/commit/8daa6c6) on **15 September**. The [2 September stage conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-02T14-30-46-024Z_01a06287-8e08-7408-8b24-c1ade9461e7a.jsonl), [15 September headless bot experiments](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-15T09-21-37-242Z_01a0a45f-31da-75a9-b35e-ad7ab5e1f24f.jsonl) and [15 September Blender/model-polish conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-15T09-44-04-116Z_01a0a473-bf14-7293-8a41-1afdb10c97d5.jsonl) help recover the practical work beyond the architecture.

**Where to look:** [Roster-complete cook and live previews `14dfdcc`](https://github.com/Binoui/SlopArena/commit/14dfdcc), [presentation previews `6531856`](https://github.com/Binoui/SlopArena/commit/6531856), [contact tooling `d431045`](https://github.com/Binoui/SlopArena/commit/d431045), [Bonk fixes `04fa99d`](https://github.com/Binoui/SlopArena/commit/04fa99d), and [CPU kit planning `c3e4011`](https://github.com/Binoui/SlopArena/commit/c3e4011). [Bonk investigation](2026-09-11-bonk-investigation.md) is especially useful for a bug-story section; [testing documentation](../testing.md) records what was actually exercised.

### 22 — 17–24 September: A recognizable demo stage and a persistent social frontend

The **Slop Pit** is committed as an accepted stage on 17 September, alongside persistent launch chat. The frontend is then consolidated into a single scene and shared shell with persistent identity/social UI. A local verification pass on 17 September exercises match → Results → rematch using Unity plus a protocol peer and fixes an accidental Training owner in PvP and a 50 Hz versus 60 Hz loop mismatch. Contextual Down behavior, crouching and sliding arrive on 24 September. The original [frontend export](../../../../../Downloads/sloparena/SlopArena_Frontend_Shell_Implementation_Plan.md) and [23 September Down plan v3](../../../../../Downloads/sloparena/sloparena-down-action-implementation-plan-v3.md) preserve the detailed choices before implementation; the frontend plan is passed into OMP in [the 21 September conversation](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-21T18-46-26-240Z_01a0c54a-74c0-764c-b713-51213443099d.jsonl).

**Where to look:** [Slop Pit `a7f33bb`](https://github.com/Binoui/SlopArena/commit/a7f33bb), [persistent chat `6e4375f`](https://github.com/Binoui/SlopArena/commit/6e4375f), [single frontend scene `0ef6512`](https://github.com/Binoui/SlopArena/commit/0ef6512), [shared shell `d8abb46`](https://github.com/Binoui/SlopArena/commit/d8abb46), [Down mechanics `63a727e`](https://github.com/Binoui/SlopArena/commit/63a727e), and [crouch/slide `8101ec5`](https://github.com/Binoui/SlopArena/commit/8101ec5). Read [frontend/social plan](../plans/2026-09-20-frontend-social-flow.md), [ADR-0032](../adr/0032-single-frontend-scene-with-persistent-social-ui.md), [Slop Pit design](../design/stages/slop_pit.design.md), and the [17 September evidence as preserved at `f830ad2`](https://github.com/Binoui/SlopArena/blob/f830ad2/docs/testing.md). This was a local protocol-peer pass, not two packaged friends on the Internet.

### 23 — 24–28 September: Steam distribution, moving off Alfred, Rooms and shields

SteamPipe setup is discussed on 24 September. At 00:58 on 25 September you report having bought an OVH VPS; the [25–26 September Steam discussion](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-25T17-38-48-577Z_01a0d9a5-fa81-75cb-ad0b-6e68ff096073.jsonl) explains the reasoning: simpler distribution for testers leads to moving public hosting away from your home server. The dedicated backend gains a pinned container/deployment path and live restricted-UDP/off-host-recovery evidence on 25 September. The Steam relay proof records a Linux client exchanging 600/600 messages with the OVH host at about 31.6 ms mean RTT. Steam-authenticated sessions and **SteamNetworkingSockets** gameplay replace public guest/raw-UDP access in the 26 September implementation. Persistent **Rooms** arrive on 27 September, separating a social group from the physical GameHost. Shield, grab, air dodge and Steam room invites/rich presence arrive on 27–28 September, replacing the earlier Burst/dash-facing defense controls.

**Where to look:** [Restricted deployment `5ba1be4`](https://github.com/Binoui/SlopArena/commit/5ba1be4), [live UDP proof `e3a6dbe`](https://github.com/Binoui/SlopArena/commit/e3a6dbe), [recovery `d7983c7`](https://github.com/Binoui/SlopArena/commit/d7983c7), [Steam routing `d3eb6ee`](https://github.com/Binoui/SlopArena/commit/d3eb6ee), its [historical Steam implementation/route evidence](https://github.com/Binoui/SlopArena/blob/d3eb6ee/docs/plans/2026-09-25-steam-playtest-integration.md), [Rooms `124fe4b`](https://github.com/Binoui/SlopArena/commit/124fe4b), [shield / invites `3086dce`](https://github.com/Binoui/SlopArena/commit/3086dce), and [air dodge / shield bubble `e2e6be0`](https://github.com/Binoui/SlopArena/commit/e2e6be0). [Steam room presence](../steam/room-presence.md) and [production hosting](../systems/production-hosting.md) explain the resulting system. The relay probe is transport evidence; it is not a finished two-account gameplay/rematch test. At **16:44 on 26 September**, the same OMP conversation (line 5247) records a friend running the Windows build and playing locally but unable to connect. This is an early external tester attempt, before the October Steam friend sessions.

### 24 — 1–6 October: Steam installs reach friends; real sessions expose sync failures

**Kistu becomes Wibou** in the 1 October checkpoint, including package IDs, assets, abilities and documentation. The model-replacement request was already made on **25 August**; [the formal rename request](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-29T11-07-44-009Z_01a0ecd9-6009-7590-9b08-a7635702bff0.jsonl) is on **29 September** (line 7). Use those dates for the decision/visual story, and 1 October for its saved integration. Windows Playtest release repairs bring client/server content and protocol checks into agreement. The documented **playtest.4** rollout activates Steam's default branch on BuildID **25663418**; **playtest.5** follows on 4 October with BuildID **25702912** and a compatible VPS release. The 4 October gameplay checkpoint removes Nilus's assets, data and ability implementations, leaving the current four-character lineup FightGuy/Manki/Wibou/Bonk.

**Where to look:** [Wibou rename / checkpoint `31929e2`](https://github.com/Binoui/SlopArena/commit/31929e2), [release repairs `f830ad2`](https://github.com/Binoui/SlopArena/commit/f830ad2), [Nilus retirement / playtest.5 baseline `fe98065`](https://github.com/Binoui/SlopArena/commit/fe98065), and [rollout receipt `d848a89`](https://github.com/Binoui/SlopArena/commit/d848a89). [Testing evidence at the receipt](https://github.com/Binoui/SlopArena/blob/d848a89/docs/testing.md) preserves both playtest.4 and playtest.5 sections; ignored `build/playtest/0.2.0-playtest.4/` and `build/playtest/0.2.0-playtest.5/` hold detailed local receipts and screenshots if still present. These receipts explicitly leave packaged two-account match/rematch unverified. Steam branch activation and human gameplay acceptance are separate milestones. Your [1 October publication conversation](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-01T16-37-03-724Z_01a0f853-9a6c-7223-8093-1ca5936a3845.jsonl) records a main-game review submission hurdle despite Playtest validation, then a request for **20 keys**. By [4 October at 12:27](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-04T10-26-29-354Z_01a10673-696a-7636-8608-421ef28aae25.jsonl), friends have installed through Steam. At [17:05 that day](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-04T14-44-56-105Z_01a10760-06a9-717c-a47b-1d4c791fb09a.jsonl) you report getting into a match with a friend but seeing a frozen remote Manki while the friend sees movement without attacks. On [6 October](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-06T18-12-48-187Z_01a1126b-0dbb-765a-b2bb-325ae7b9f29c.jsonl) another attempt gets both players in, but neither can move; camera and chat work. These are confirmed friend-test attempts and observed blockers, not a verified clean match/rematch. The [4 October Discord/session recording](../../../../../Videos/obs%20recording/2026-10-04%2016-50-55.mp4) is promising material for this chapter (~68 minutes; sampled frame shows streamed gameplay).

### 25 — 5–14 October: Iteration tools and releases become easier to repeat

Ability Lab embeds a resizable scene viewport directly into **Moves** on 5 October. Release work then moves to protected CI orchestration with a licensed local Unity client build pinned to exact source, coordinated backend deployment and manual Steam activation. On 11 October dormant legacy ability/Warp/Nilus runtime paths are removed and combat tuning becomes immutable per match. The latest checkpoint includes HUD/spatial-indicator refinements, **Log Off** woodland presentation, public contributor/agent workflow cleanup, and a **resumable Playtest release controller** on 14 October. A separate owner console gains [private match-log inspection](https://github.com/Binoui/SlopArena-console/commit/cbc4c42) on 11 October and a [hosted observation UI](https://github.com/Binoui/SlopArena-console/commit/025fb6b) on 13 October, giving you more useful evidence when friends hit online problems. Replay/game-mode documents are proposals/contracts at this cut-off, not evidence that replay gameplay or every proposed mode is implemented.

**Where to look:** [Embedded Lab viewport `e7cacd8`](https://github.com/Binoui/SlopArena/commit/e7cacd8), [protected releases `e3a8e02`](https://github.com/Binoui/SlopArena/commit/e3a8e02), [licensed local artifacts `7d36109`](https://github.com/Binoui/SlopArena/commit/7d36109), [legacy runtime removal `eb509a9`](https://github.com/Binoui/SlopArena/commit/eb509a9), [immutable tuning `ed636d3`](https://github.com/Binoui/SlopArena/commit/ed636d3), [HUD `882da31`](https://github.com/Binoui/SlopArena/commit/882da31), [Log Off `d2e112b`](https://github.com/Binoui/SlopArena/commit/d2e112b), [replay/mode design `d4fd203`](https://github.com/Binoui/SlopArena/commit/d4fd203), and [resumable release `b1d583c`](https://github.com/Binoui/SlopArena/commit/b1d583c). Read the [simulation audit](2026-10-11-simulation-code-audit.md), [release controller ADR](../adr/0034-resumable-playtest-release-controller.md), [release pipeline](../systems/release-pipeline.md), [Log Off design](../design/stages/log_off.design.md), and [replay design](../plans/2026-10-13-replay-v1-design.md).

## How to dig deeper without losing the original story

Use the commit or historical file link beside each milestone first. For the exact original text, these read-only commands work from the repository root:

```bash
git show --format=fuller --stat 4cb7767
git show 4cb7767:docs/systems/ability-lab.md
git log --since=2026-08-10 --until=2026-08-16 --format='%h %aI %cI %s'
git diff 4cb7767 0062992 -- docs/systems/ability-lab.md
```

`git show --format=fuller` separates author and integration dates. `git show <commit>:<historical-path>` recovers deleted/renamed files without touching the checkout. Compare the **original** and **later** versions when telling a pivot story. Avoid `git log --reverse -N` for origins: it reverses the selected latest N commits, not the entire history. A snapshot proves source existed; an acceptance receipt or conversation is needed to establish what you actually played, felt, shipped or abandoned.

The richest repository sources for a script are:

- **Intent and decisions:** original `gdd.md`, `docs/plans/`, `docs/adr/`, and surviving `docs/superpowers/specs/` / `plans/`. Read dated originals rather than treating every checkbox or aspiration as completed work.
- **Specific bug stories:** [Bonk investigation](2026-09-11-bonk-investigation.md), [character cooking ADR](../adr/0029-character-authoring-and-cooking.md), [simulation audit](2026-10-11-simulation-code-audit.md), and historical test/commit explanations.
- **Measured before/after visuals:** `docs/generated/` move-data, movement, self-play and A/B reports; `docs/research/` Melee audits and trajectory sweeps; the original Ability Lab scene/manual.
- **Game identity and art:** [visual language](../design/visual-language.md), [visual production roadmap](../plans/visual-production-roadmap.md), character docs, `docs/design/stages/`, and package assets. Current files often contain October changes; recover older snapshots for chronological footage.
- **Release facts:** `docs/testing.md`, `docs/systems/release-pipeline.md`, dated Steam plans, Git tags, and local `build/playtest/` receipts. A Git tag alone does not prove branch activation or a friend's successful match.
- **Your words and frustrations:** Antigravity, Hermes and OMP session archives. Git often explains what changed, but those conversations are the stronger source for why you changed your mind and what each tool felt like to use. Treat model co-author lines as evidence for a particular change, not a complete inventory of all assistants used.

The records now explain the Unreal reversal, Unity choice and several actual friend-test attempts. The remaining gaps are what preceded the earliest 22 May record, the full extent of the UE5 experiment, and the first **successful, complete** packaged two-account match/rematch. The sampled October footage does not by itself settle that last point. Preserve the difference between “I built the system,” “I deployed a build,” and “friends successfully played it.”


## Local source map

This section is a local script-research aid. These links point into this machine's archives, not redistributable repository assets. Conversation titles are useful search labels; some sessions have no title. Raw OMP filenames use UTC, while this report uses Europe/Paris, and a session can continue on a later day. In particular: the Lab request is **11 August local time**, and the Melee commitment is **13 August at 00:14**.

### Antigravity: the earliest surviving design

- [Original GDD](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/gdd.md) and [earlier French GDD version](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/gdd.md.resolved.0): initial vision and movement/combat pillars.
- [MoveBox implementation plan](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/implementation_plan.md): GDScript → C#, Shared library and .NET server.
- [GDD metadata](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/gdd.md.metadata.json) / [plan metadata](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/implementation_plan.md.metadata.json): 22 May timestamps.
- [Godot setup screenshot](../../../../../.gemini/antigravity/brain/3dcc278c-adb8-4260-936a-fc0b307ccebb/media__1779452308444.png): missing `Main.cs` dependency error; useful early troubleshooting footage, not a gameplay screenshot.
- Conversation ID `3dcc278c-adb8-4260-936a-fc0b307ccebb`, preview **Implementing Arena Combat Prototype**. [Summary database](../../../../../.gemini/antigravity/conversation_summaries.db) points to the original `MoveBox` workspace. The original `.pb` conversation still exists under `~/.gemini/antigravity/conversations/`, but was not readable as plain text; the Markdown artifacts are the usable evidence here. `antigravity-backup/` contains another copy of the same artifacts, not an independent earlier origin.

### Hermes: May–June decisions in your own words

| Date / topic | Where to look | Search or database locator |
| --- | --- | --- |
| 25 May: early Godot UI and movement | [Godot Game Development Assistant](../../../../../.hermes/sessions/session_20260525_232352_7eb5ce.json) | `MoveBox Arena Sandbox` |
| 27–29 May: choose DKO-style combat | [Git Pull Summary of Changes](../../../../../.hermes/sessions/session_20260527_185140_25c543.json) | `je veux juste refaire DKO; le grimoire` |
| 1 June: AI/open-source vision and Unreal choice | [Collaborative AI Game Vision](../../../../../.hermes/sessions/session_20260601_000841_378f94.json) | `steam workshop; on va faire de l’unreal` |
| 1 June: return to Godot | [Improving Assistant Communication Style](../../../../../.hermes/sessions/session_20260601_232134_e49cbf.json) | `revert le passage à unreal` |
| 4 June: first art direction / 3DAIStudio | [Hermes state.db](../../../../../.hermes/state.db) — SlopArena Art Direction Planning | Session `20260604_194101_a19379`, message `6156` |
| 11 June: explain AI use publicly | [Hermes state.db](../../../../../.hermes/state.db) — AI Disclaimer for SlopArena README | Session `20260611_155409_39b1a5`, message `13219` |
| 13 June: Manki identity and Bunny concept | [Hermes state.db](../../../../../.hermes/state.db) — Sloparena Martial Artist Concepts | Session `20260613_123412_e0528d`, message `19546` |

Several later Hermes sessions survive only in the database. To recover one without modifying it, Python's built-in SQLite support is enough; change the session ID to the row you want:

```python
import sqlite3
from pathlib import Path

path = Path.home() / ".hermes/state.db"
connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
rows = connection.execute(
    "SELECT id, role, content FROM messages "
    "WHERE session_id = ? AND role IN ('user', 'assistant') ORDER BY timestamp, id",
    ("20260613_123412_e0528d",),
)
for message_id, role, content in rows:
    print(f"\n--- {message_id} / {role} ---\n{content or ''}")
connection.close()
```

### OMP: decisions, bugs and milestones

Top-level project sessions live under `~/.omp/agent/sessions/-Documents-projects-SlopArena/`. From late September, also look under `-Documents-projects-sloparena-workspace/`; parallel worktree sessions use `-orca-workspaces-SlopArena-*`. The following direct links narrow the search to particularly useful sessions. A [CSV source index](2026-10-14-development-sources.csv) contains 32 selected session paths and locators, including the Downloads follow-up.

| ID | When / story | Conversation | Useful location |
| --- | --- | --- | --- |
| O01 | 24–25 Jun: OMP and Unity | [Setup OMP For Coding](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-24T08-43-34-367Z_019ef8cc-885f-7000-b3d8-04ea8ebf7614.jsonl) | Lines 6: coming from Hermes; 891: Unity decision; 915: download/start; 982: project created |
| O02 | 25 Jun: migration | [Migrate Godot To Unity Scene](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-06-25T13-06-03-636Z_019efee3-34f4-7000-b145-032d264d8350.jsonl) | Lines 4–53: shared/server boundaries and engine-independent game goal |
| O03 | 3 Jul: animation workflow | [Animancer discussion (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-03T13-01-51-420Z_019f2812-3bbc-7000-a502-318a2514645c.jsonl) | Lines 5–30: generated controllers, code preference and cost |
| O04 | 10 Jul: roster change | [Replace bunny with fightguy](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-07-10T15-43-55-967Z_019f4cb3-223f-7000-ae38-3e8f54c6feef.jsonl) | Lines 7: original replacement request |
| O05 | 2 Aug: downloadable online demo | [Building PvP demo exe and server](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-02T14-53-53-353Z_019fc2f7-9549-7000-993e-9b273a93ef16.jsonl) | Lines 5–32: EXE experience and Alfred hosting |
| O06 | 4 Aug: two-client test | [Fix KeyNotFoundException with two clients](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-04T21-19-56-155Z_019fcea5-bcfb-7000-a8df-cb3ed2fca455.jsonl) | Lines 6: in game via Alfred, followed by missing-entity errors |
| O07 | 11 Aug: Lab origin | [Build character hurtbox editor tool](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-10T23-04-04-511Z_019fedeb-3c9f-7000-975d-850633c596d9.jsonl) | Lines 5: human placement and frame-by-frame viewing |
| O08 | 12 Aug: independent normals | [Reworking all character abilities](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-12T15-53-36-753Z_019ff6ad-daf1-7000-9cde-80720404d8db.jsonl) | Lines 5, 26: split LMB phases, 1/2/3/4 plus specials |
| O09 | 12–13 Aug: Melee pivot | [Explore Melee decomp for tech data](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-12T21-44-28-059Z_019ff7ef-129b-7000-a31d-3a191aa3785c.jsonl) | Lines 5: decomp idea; 131: commit to Melee as base |
| O10 | 21 Aug: Workshop ambition | [Workshop ADR discussion (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-21T12-00-29-930Z_01a02431-aaea-7000-b367-418e1b77196d.jsonl) | Lines 4, 46: original ChatGPT/exported ADR |
| O11 | 23 Aug: JSON persistence | [Ability Lab JSON request (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-23T11-05-04-745Z_01a02e4b-a5e9-766a-95c4-463124df85d6.jsonl) | Lines 7: replace C# source rewriting |
| O12 | 28 Aug: Bonk and agent workflow | [Bonk Package Pipeline Spike](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-28T16-52-31-253Z_01a04949-8995-7778-acea-3fc0d30a4ab4.jsonl) | Lines 625: difficulties with agentic character creation; 627: animation-folder concern |
| O13 | 5 Sep: demo reset follow-through | [Demo release readiness (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-05T16-54-03-619Z_01a0727d-d263-7587-a61a-7aa1b9fee493.jsonl) | Lines 7: EXE, deployment and full roster |
| O14 | 2 Sep: stage production | [Create stage from scifi city demo](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-02T14-30-46-024Z_01a06287-8e08-7408-8b24-c1ade9461e7a.jsonl) | Lines 289: turn an asset-pack scene into a stage |
| O15 | 15 Sep: headless experiments | [Run aerial and jump-in experiments](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-15T09-21-37-242Z_01a0a45f-31da-75a9-b35e-ad7ab5e1f24f.jsonl) | Lines 9: bot matchups and telemetry request |
| O16 | 15 Sep: AI models / Blender polish | [Repair Shoulder Patches, Preserve Materials](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-15T09-44-04-116Z_01a0a473-bf14-7293-8a41-1afdb10c97d5.jsonl) | Lines 7: generated models, artifacts and hands-on model repair |
| O17 | 17 Sep: chat and stage checkpoint | [Clean Changes and Commit Slop Pit](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-17T13-03-15-774Z_01a0af76-d53e-723b-9ead-078852bd9802.jsonl) | Lines 9: chat implementation across game and Master repos |
| O18 | 25 Sep: off-site hosting | [OVH VPS implementation (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-24T22-57-47-882Z_01a0d5a3-a96a-73e3-884d-09fce44f9b18.jsonl) | Lines 5: purchased VPS; following user turns discuss deployment |
| O19 | 25–26 Sep: Steam and external test | [Deploy Isolated Playtest Build for Proton](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-25T17-38-48-577Z_01a0d9a5-fa81-75cb-ad0b-6e68ff096073.jsonl) | Lines 6 and following turns: distribution/VPS reasoning; 5247: friend plays local but cannot connect |
| O20 | 1 Oct: publication hurdle | [Check Sloparena Playtest Publication Status](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-01T16-37-03-724Z_01a0f853-9a6c-7223-8093-1ca5936a3845.jsonl) | Lines 129: main-game review hurdle; 152: request 20 keys |
| O21 | 4 Oct: friends install | [Friends install and visual bugs (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-04T10-26-29-354Z_01a10673-696a-7636-8608-421ef28aae25.jsonl) | Lines 7: Steam installs, missing Wibou effects, Bonk lag and T-pose |
| O22 | 4 Oct: first recorded match attempt | [Troubleshoot Multiplayer Sync Issues](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-04T14-44-56-105Z_01a10760-06a9-717c-a47b-1d4c791fb09a.jsonl) | Lines 6: frozen remote Manki and missing attacks |
| O23 | 6 Oct: next friend attempt | [Fix multiplayer movement after clock changes](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-06T18-12-48-187Z_01a1126b-0dbb-765a-b2bb-325ae7b9f29c.jsonl) | Lines 6: no movement while chat and camera work |
| O24 | 30 Sep: focus on fun | [Reconcile SlopArena Vision With Demo](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-09-30T18-14-22-098Z_01a0f386-5492-75d0-b651-b1110ed4e2b7.jsonl) | Lines 11, 114: scope discussion and playable priorities |
| O25 | 30 Sep: graphic identity | [Unify SlopArena’s Visual Style](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-09-30T17-49-51-372Z_01a0f36f-e38c-70d2-8dc8-105999294e5f.jsonl) | Lines 12 and following turns: visual direction discussion |
| O26 | 4 Oct: release request | [Release Everything on Steam and VPS](../../../../../.omp/agent/sessions/-Documents-projects-sloparena-workspace/2026-10-03T22-50-49-289Z_01a103f6-8249-74eb-bce5-a677771c256a.jsonl) | Lines 5: release request; subsequent verification/rollout work |

| O27 | 15 Aug: original UI PDF | [Review SlopArena UI specification](../../../../../.omp/agent/sessions/-orca-workspaces-SlopArena-GUI/2026-08-15T10-14-33-672Z_01a004ea-85c8-7000-9495-cab140af8a10.jsonl) | Lines 4: freshly generated PDF, build GUI and discuss it |
| O28 | 21 Sep: frontend export | [Frontend-shell specification (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-21T18-46-26-240Z_01a0c54a-74c0-764c-b713-51213443099d.jsonl) | Lines 6: original Downloads plan |
| O29 | 25 Aug: Wibou model | [Kistu-to-Wibou model change (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-08-24T22-30-51-921Z_01a035e5-dd11-7170-a3d9-44c4e4e97e42.jsonl) | Lines 5: new model; session filename is 24 August UTC |
| O30 | 29 Sep: Wibou identity | [Kistu-to-Wibou name change (untitled)](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-29T11-07-44-009Z_01a0ecd9-6009-7590-9b08-a7635702bff0.jsonl) | Lines 7: explicit rename request |
| O31 | 26 Sep: video planning | [Create a Sloparena Promotional Clip](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-26T13-59-12-848Z_01a0de03-4ad0-7551-b3da-139891ed6430.jsonl) | Lines 344: Steam artwork and choosing story/slides first |
| O32 | 29 Sep: splash artwork | [Update Character Splash Art and Crops](../../../../../.omp/agent/sessions/-Documents-projects-SlopArena/2026-09-28T22-11-33-040Z_01a0ea12-c230-7754-91c4-f55c6cd67331.jsonl) | Lines 10: request new splashes and portrait crops |

To read a session cleanly, parse its JSONL instead of scrolling through image payloads and tool logs. This prints user/assistant text and keeps the original line numbers:

```python
import json
from pathlib import Path

path = Path.home() / ".omp/agent/sessions/-Documents-projects-SlopArena/2026-08-10T23-04-04-511Z_019fedeb-3c9f-7000-975d-850633c596d9.jsonl"
with path.open() as stream:
    for line_number, line in enumerate(stream, 1):
        entry = json.loads(line)
        message = entry.get("message", {})
        if message.get("role") not in ("user", "assistant"):
            continue
        content = message.get("content", [])
        body = content if isinstance(content, str) else "\n".join(
            block.get("text", "") for block in content if block.get("type") == "text"
        )
        if body.strip():
            print(f"\n--- line {line_number} / {message['role']} ---\n{body}")
```

### Older checkouts, source art and workspace drafts

- [Preserved Godot checkout](../../../slop-arena-%284.6%29): old scenes/scripts/art and June-era docs; its checked-out history ends around 19 June. Useful for inspecting the old game, not the current game's commit hashes.
- [Downloaded character/source-art folders](../../../../../Downloads/sloparena): `monkey/`, `new_manki/`, `rabbit/`, `fightguy/`, `kistu/`, `wibou/` and exported design plans. Good places to find first models and concept comparisons; file modification times alone do not establish when a design was accepted.
- [Workspace planning/evidence](../../../sloparena-workspace/_bmad-output): particularly [Ability Lab product spec](../../../sloparena-workspace/_bmad-output/specs/spec-ability-lab-product/SPEC.md), [Lab UI screenshots](../../../sloparena-workspace/_bmad-output/specs/spec-ability-lab-product/evidence), [gameplay-identity notes](../../../sloparena-workspace/_bmad-output/forge/sloparena-gameplay-identity/forged-idea.md) and `release-automation/`. These are rich October design sources; a spec by itself does not prove implementation.
- [15 September local-work archive](../../../SlopArena-local-archives/20260915-211836/local-work.tar): inspected its member list. It contains model/polish comparison images and video under `art/blender/exports/`, plus menu review screenshots under `.impeccable/`. Particularly useful members: `art/blender/exports/manki-accessories-x8nsiqfo/01-front-comparison.png`, `art/blender/exports/manki-deformation-ivb6v_mo/bound-clips-three-view.mp4`, and `.impeccable/review/pre-match/final-home-1080.png`. These members were not extracted or visually checked during this research.
- Sibling histories: [MasterServer](../../../SlopArena-MasterServer) for backend/chat/Rooms; [website](../../../SlopArena-web) for the 31 August landing page and September feedback/Steam work; [owner console](../../../SlopArena-console) for October match-diagnostic work.

## Footage you can reuse or investigate

I extracted and viewed sample frames from each clip below. Descriptions cover those samples, not the entire video or its audio. Dates here are the recording filenames; durations come from `ffprobe`. The original files were left untouched.

| Recording | Duration | What the inspected frame shows |
| --- | --- | --- |
| [2026-06-04 22-39-06](../../../../../Videos/obs%20recording/2026-06-04%2022-39-06.mp4) | 0:05 | A character/model in an online 3D asset tool; early art production, not gameplay. Sample at 2.47s. |
| [2026-06-11 17-31-01](../../../../../Videos/obs%20recording/2026-06-11%2017-31-01.mp4) | 0:53 | Godot-era yellow/grey blockout arena, small fighters and debug-heavy HUD. Sample at 15s. |
| [2026-06-21 13-35-12](../../../../../Videos/obs%20recording/2026-06-21%2013-35-12.mp4) | 1:22 | Godot Manki/Bunny combat view on the same blockout arena. Sample at 15s. |
| [2026-07-12 16-41-24](../../../../../Videos/obs%20recording/2026-07-12%2016-41-24.mp4) | 0:17 | Unity arena, FightGuy-era combat and bright placeholder effects. Sample at 8.41s. |
| [2026-07-23 18-05-39](../../../../../Videos/obs%20recording/2026-07-23%2018-05-39.mp4) | 1:02 | Unity circular arena and FightGuy with the old LMB/RMB action bar. Sample at 15s. |
| [2026-08-09 13-56-19](../../../../../Videos/obs%20recording/2026-08-09%2013-56-19.mp4) | 0:38 | Green checkerboard training surface and the old mouse-attack HUD. Sample at 15s. |
| [2026-08-11 22-21-30](../../../../../Videos/obs%20recording/2026-08-11%2022-21-30.mp4) | 0:30 | Early Ability Lab window, frame scrubbing, green hurtboxes and orange hitbox. Sample at 14.77s. |
| [2026-08-17 23-41-23](../../../../../Videos/obs%20recording/2026-08-17%2023-41-23.mp4) | 1:23 | Training combat with percentage labels and the new independent-normal controls. Sample at 15s. |
| [2026-08-22 19-19-57](../../../../../Videos/obs%20recording/2026-08-22%2019-19-57.mp4) | 1:32 | Dark/neon prototype stage, prominent percentage HUD and redesigned action icons. Sample at 15s. |
| [2026-08-27 13-13-40](../../../../../Videos/obs%20recording/2026-08-27%2013-13-40.mp4) | 0:41 | Bonk visible in Unity animation-curve editing; early character setup evidence. Sample at 15s. |
| [2026-09-03 21-39-13](../../../../../Videos/obs%20recording/2026-09-03%2021-39-13.mp4) | 1:07 | Neon sci-fi city stage with rooftop platforms and a dense background. Sample at 15s. |
| [2026-09-08 21-08-21](../../../../../Videos/obs%20recording/2026-09-08%2021-08-21.mp4) | 5:06 | Orca/OMP terminal with tests, docs checks and a development report. Sample at 15s. |
| [2026-09-17 12-28-38](../../../../../Videos/obs%20recording/2026-09-17%2012-28-38.mp4) | 1:18 | Slop Pit-era industrial stage and close-range combat. Sample at 15s. |
| [2026-09-30 16-28-45](../../../../../Videos/obs%20recording/2026-09-30%2016-28-45.mp4) | 0:20 | Bonk in training, sword/trail and controller-shaped input HUD. Sample at 9.99s. |
| [2026-10-04 16-50-55](../../../../../Videos/obs%20recording/2026-10-04%2016-50-55.mp4) | 67:46 | Discord viewing streamed SlopArena gameplay during the friends-test period. Sample at 15s. |

| [sloparena_dev_clip.mp4](../../../../../Downloads/sloparena_dev_clip.mp4) | 0:31 | Godot-era Manki, blockout arena and debug volumes/effects. Samples at 10s and ~21.6s; file mtime 11 June, capture date unverified. |

[part2.kdenlive](../../../../../Videos/part2.kdenlive) already references September 8–10 OBS clips and additional camera/Orca/skill footage. Some external sources under `Downloads/vid2/` are currently missing, so that edit may need relinking. [test2.kdenlive](../../../../../Videos/test2.kdenlive) references the 17 September clip.

## A possible vlog chapter spine

This is an editorial suggestion, not a claim about how you felt:

1. **The movement game I wanted to play** — May concept, MoveBox, finding the DKO direction.
2. **Choosing tools while learning game development** — Antigravity → Hermes → OMP; Unreal detour; Unity migration.
3. **Making the server see the same game** — Shared simulation, animation baking and online match flow.
4. **Finding the fun again** — moving away from automatic combos toward independent normals and Melee-inspired feel.
5. **Building a tool so I could design the attacks myself** — Ability Lab's visual origin, then JSON and cooking.
6. **A game, or a platform for people to create fighters?** — community ambition, packages, new characters and the demo reset.
7. **Getting it onto my friends' PCs** — Alfred → VPS, Steam review/distribution, actual installs and sync failures.
8. **Where it stands now** — iteration, match diagnostics, roster/stage polish and what still needs a clean human playtest.

Good candidates for short personal stories are the one-day Unreal reversal, the first model/animation/hurtbox mismatch, the request to take back visual control of hitboxes, the FightGuy animation bug that exposed conflicting definitions, and the friend session where chat worked but neither fighter could move.

## Additional Downloads material for the script

The [Downloads source guide](2026-10-14-downloads-source-guide.md) maps the original character sheets, AI-workflow presentations, early build archives, UI/Workshop/frontend/movement/Steam planning exports, and store artwork to the relevant eras. Its date notes distinguish conversation evidence, explicit document dates, archive-entry timestamps and filesystem timestamps. In particular, the AI presentation decks already contain speaker notes about how your development process evolved; they can seed the personal narrative alongside the implementation history.
